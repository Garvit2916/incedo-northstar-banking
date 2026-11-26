using Microsoft.EntityFrameworkCore;
using Northstar.DataMigration;
using Northstar.Infrastructure.Persistence;

if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
{
    Console.WriteLine(MigrationOptions.Usage());
    return 0;
}

var options = MigrationOptions.Parse(args);
if (options.HelpRequested)
{
    Console.WriteLine(MigrationOptions.Usage());
    return 0;
}

var validationErrors = options.Validate();
if (validationErrors.Count > 0)
{
    foreach (var error in validationErrors)
    {
        Console.Error.WriteLine(error);
    }

    return 2;
}

var report = new MigrationReport
{
    GeneratedAtUtc = DateTimeOffset.UtcNow,
    Mode = options.Mode.ToString()
};
var verifier = new S3BlobVerifier();

try
{
    switch (options.Mode)
    {
        case MigrationMode.DryRun:
        case MigrationMode.Apply:
            return await RunMigrationAsync(options, verifier, report);

        case MigrationMode.Verify:
            return await RunVerificationAsync(options, report);

        case MigrationMode.Rollback:
            return await RunRollbackAsync(options, report);

        default:
            throw new InvalidOperationException($"Unsupported mode {options.Mode}.");
    }
}
catch (Exception ex)
{
    report.Status = options.Mode == MigrationMode.Rollback ? "rollback-failed" : "failed";
    report.Errors.Add($"{ex.GetType().Name}: {ex.Message}");
    WriteReport(options.ReportPath, report);
    Console.Error.WriteLine($"Migration utility failed: {ex.GetType().Name}: {ex.Message}");
    return 1;
}

static async Task<int> RunMigrationAsync(
    MigrationOptions options,
    IS3BlobVerifier verifier,
    MigrationReport report)
{
    await using var source = new AppDbContext(CreateSourceOptions(options.SourceConnectionString!), TimeProvider.System);
    await using var target = new MigrationAppDbContext(CreateTargetOptions(options.TargetConnectionString!));

    var snapshot = await SourceSnapshotReader.ReadAsync(
        source,
        options.IncludeLinkedAgentUsers,
        CancellationToken.None);
    report.Manifest = snapshot.Manifest;

    var preflight = await TargetPreflight.RunAsync(
        target,
        snapshot,
        options,
        CancellationToken.None);
    report.Preflight = preflight.Summary;

    report.Blobs = await verifier.VerifyAsync(
        snapshot.KycDocuments,
        options,
        CancellationToken.None);

    var blockers = report.Preflight.Blockers.Concat(report.Blobs.Blockers).ToList();
    if (options.Mode == MigrationMode.Apply &&
        (!report.Blobs.Enabled || report.Blobs.CheckedCount != snapshot.KycDocuments.Count))
    {
        blockers.Add(
            "S3 blob verification did not cover every KYC document; apply is blocked. The utility never modifies S3 objects.");
    }

    report.Errors.AddRange(blockers);

    if (options.Mode == MigrationMode.DryRun)
    {
        report.Status = blockers.Count == 0 ? "dry-run" : "dry-run-blocked";
        WriteReport(options.ReportPath, report);
        PrintDryRunSummary(report);
        return blockers.Count == 0 ? 0 : 1;
    }

    if (blockers.Count > 0)
    {
        report.Status = "preflight-failed";
        WriteReport(options.ReportPath, report);
        PrintDryRunSummary(report);
        return 1;
    }

    await using var transaction = await target.Database.BeginTransactionAsync(CancellationToken.None);

    if (report.Preflight.ConflictPlan.Action == "quarantine")
    {
        await ConflictResolver.QuarantineAsync(target, report.Preflight.ConflictPlan, CancellationToken.None);
    }

    var migrationAuditEventId = await MigrationWriter.ApplyAsync(
        target,
        snapshot,
        preflight.RoleIds,
        CancellationToken.None);

    report.Apply = new ApplySummary
    {
        Applied = true,
        MigrationAuditEventId = migrationAuditEventId,
        CompletedAtUtc = DateTimeOffset.UtcNow
    };
    report.Status = "applied";

    report.Verification = await MigrationVerifier.VerifyAsync(target, report, CancellationToken.None);
    var verificationFailures = report.Verification
        .Where(pair => pair.Value != "ok")
        .Select(pair => $"{pair.Key}: {pair.Value}")
        .ToList();

    if (verificationFailures.Count > 0)
    {
        await transaction.RollbackAsync(CancellationToken.None);
        report.Errors.AddRange(verificationFailures);
        report.Status = "failed-verification-rolled-back";
        report.Apply = new ApplySummary();
        WriteReport(options.ReportPath, report);
        Console.Error.WriteLine("In-transaction verification failed; the target transaction was rolled back.");
        return 1;
    }

    await transaction.CommitAsync(CancellationToken.None);
    report.Status = "applied";
    WriteReport(options.ReportPath, report);
    PrintApplySummary(report);
    return 0;
}

static async Task<int> RunVerificationAsync(MigrationOptions options, MigrationReport report)
{
    var persisted = LoadReport(options.ReportPath);
    await using var target = new MigrationAppDbContext(CreateTargetOptions(options.TargetConnectionString!));

    persisted.Verification = await MigrationVerifier.VerifyAsync(target, persisted, CancellationToken.None);
    persisted.GeneratedAtUtc = DateTimeOffset.UtcNow;
    var failures = persisted.Verification.Where(pair => pair.Value != "ok").ToList();
    persisted.Status = failures.Count == 0 ? "verified" : "verification-failed";
    WriteReport(options.ReportPath, persisted);

    if (failures.Count == 0)
    {
        Console.WriteLine("Verification PASS.");
        return 0;
    }

    Console.Error.WriteLine("Verification FAIL:");
    foreach (var (check, detail) in failures)
    {
        Console.Error.WriteLine($"  {check}: {detail}");
    }

    return 1;
}

static async Task<int> RunRollbackAsync(MigrationOptions options, MigrationReport report)
{
    var persisted = LoadReport(options.ReportPath);
    if (!persisted.Apply.Applied || persisted.Apply.MigrationAuditEventId == Guid.Empty)
    {
        Console.Error.WriteLine("Report does not describe an applied migration; nothing to roll back.");
        return 1;
    }

    await using var target = new MigrationAppDbContext(CreateTargetOptions(options.TargetConnectionString!));

    var auditEvent = await target.AuditEvents
        .AsNoTracking()
        .SingleOrDefaultAsync(audit => audit.Id == persisted.Apply.MigrationAuditEventId, CancellationToken.None);
    if (auditEvent is null ||
        auditEvent.Action != MigrationScope.MigrationAction ||
        auditEvent.EntityId != persisted.Manifest.Hash)
    {
        persisted.Status = "rollback-precheck-failed";
        persisted.Errors.Add("Applied migration audit event is missing or does not match the report manifest hash.");
        WriteReport(options.ReportPath, persisted);
        Console.Error.WriteLine("Rollback precheck failed; no rows were changed.");
        return 1;
    }

    await using var transaction = await target.Database.BeginTransactionAsync(CancellationToken.None);

    await RollbackExecutor.ExecuteAsync(target, persisted, CancellationToken.None);

    persisted.RollbackVerification = await MigrationVerifier.VerifyRollbackAsync(
        target,
        persisted,
        CancellationToken.None);
    var rollbackFailures = persisted.RollbackVerification
        .Where(pair => pair.Value != "ok")
        .Select(pair => $"{pair.Key}: {pair.Value}")
        .ToList();
    if (rollbackFailures.Count > 0)
    {
        await transaction.RollbackAsync(CancellationToken.None);
        persisted.Status = "rollback-failed-verification";
        persisted.Errors.AddRange(rollbackFailures);
        WriteReport(options.ReportPath, persisted);
        Console.Error.WriteLine("Rollback verification failed; the target transaction was rolled back.");
        return 1;
    }

    await transaction.CommitAsync(CancellationToken.None);

    persisted.Status = "rolled-back";
    persisted.Apply = new ApplySummary();
    persisted.RollbackVerification.Clear();
    persisted.RollbackVerification["result"] = "ok";
    persisted.GeneratedAtUtc = DateTimeOffset.UtcNow;
    WriteReport(options.ReportPath, persisted);

    Console.WriteLine("Compensating rollback committed and verified. Prefer a PostgreSQL restore point when one is available.");
    return 0;
}

static DbContextOptions<AppDbContext> CreateSourceOptions(string connectionString) =>
    new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connectionString)
        .Options;

static DbContextOptions<AppDbContext> CreateTargetOptions(string connectionString) =>
    new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(
            connectionString,
            postgres => postgres.MigrationsAssembly("Northstar.Infrastructure.Persistence.PostgresMigrations"))
        .Options;

static MigrationReport LoadReport(string path) =>
    ReportSerializer.Deserialize(File.ReadAllText(path));

static void WriteReport(string path, MigrationReport report)
{
    report.GeneratedAtUtc = DateTimeOffset.UtcNow;
    var fullPath = Path.GetFullPath(path);
    var directory = Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }

    File.WriteAllText(fullPath, ReportSerializer.Serialize(report));
    Console.WriteLine($"Report written to {fullPath}");
}

static void PrintDryRunSummary(MigrationReport report)
{
    Console.WriteLine($"Status: {report.Status}");
    Console.WriteLine($"Manifest hash: {report.Manifest.Hash}");
    foreach (var (table, count) in report.Manifest.Counts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
    {
        Console.WriteLine($"  {table}: source={count}");
    }

    if (report.Preflight.ConflictPlan.Action != "none")
    {
        Console.WriteLine($"Admin conflict action: {report.Preflight.ConflictPlan.Action} for {report.Preflight.ConflictPlan.UserId}");
    }

    Console.WriteLine(
        $"KYC blobs (read-only): checked={report.Blobs.CheckedCount}, " +
        $"checksum-verified={report.Blobs.ChecksumVerifiedCount}, " +
        $"missing={report.Blobs.MissingCount}, mismatched={report.Blobs.MismatchCount}");

    foreach (var blocker in report.Errors)
    {
        Console.Error.WriteLine($"  BLOCKER: {blocker}");
    }
}

static void PrintApplySummary(MigrationReport report)
{
    Console.WriteLine("Migration applied and verified.");
    Console.WriteLine($"Manifest hash: {report.Manifest.Hash}");
    Console.WriteLine($"Migration audit event: {report.Apply.MigrationAuditEventId}");
    Console.WriteLine("Refresh tokens were not migrated; affected users must sign in again.");
}
