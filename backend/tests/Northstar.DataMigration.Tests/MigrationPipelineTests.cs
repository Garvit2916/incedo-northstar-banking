using Microsoft.EntityFrameworkCore;

namespace Northstar.DataMigration.Tests;

public class MigrationPipelineTests
{
    [Fact]
    public async Task ApplyPassesInTransactionVerification()
    {
        await using var targetDatabase = new SqliteTestDatabase();
        await using var target = targetDatabase.CreateContext();
        var report = await PrepareAndApplyAsync(target);

        var verification = await MigrationVerifier.VerifyAsync(target, report);

        Assert.All(verification, pair => Assert.Equal("ok", pair.Value));
        Assert.Equal("quarantine", report.Preflight.ConflictPlan.Action);
    }

    [Fact]
    public async Task ContentVerificationDetectsTampering()
    {
        await using var targetDatabase = new SqliteTestDatabase();
        await using var target = targetDatabase.CreateContext();
        var report = await PrepareAndApplyAsync(target);

        var product = await target.Products.SingleAsync();
        product.Name = "tampered";
        await target.SaveChangesAsync();

        var verification = await MigrationVerifier.VerifyAsync(target, report);
        Assert.Equal("migrated row content differs from source", verification["rows.content.Products"]);
    }

    [Fact]
    public async Task RollbackPassesPostconditionVerification()
    {
        await using var targetDatabase = new SqliteTestDatabase();
        await using var target = targetDatabase.CreateContext();
        var report = await PrepareAndApplyAsync(target);

        target.ChangeTracker.Clear();
        await RollbackExecutor.ExecuteAsync(target, report);

        var verification = await MigrationVerifier.VerifyRollbackAsync(target, report);
        Assert.All(verification, pair => Assert.Equal("ok", pair.Value));
        Assert.False(await target.Users.AnyAsync(user => user.Id == MigrationScope.AdministratorUserId));
        Assert.True(await target.Users.AnyAsync(user => user.Id == MigrationScope.ConflictAdminUserId));
    }

    private static async Task<MigrationReport> PrepareAndApplyAsync(MigrationAppDbContext target)
    {
        await using var sourceDatabase = new SqliteTestDatabase();
        await using var source = sourceDatabase.CreateContext();
        await SeedDataFactory.SeedSourceAsync(source);
        var snapshot = await SourceSnapshotReader.ReadAsync(source, includeOptionalLinkedAgentUsers: false);

        await SeedDataFactory.SeedTargetRolesAsync(target);
        await SeedDataFactory.SeedAppliedPostgresMigrationAsync(target);
        await SeedDataFactory.SeedConflictAdminAsync(target);

        var options = MigrationOptions.Parse(new[]
        {
            "--source", "source.db",
            "--target", "Host=localhost;Database=test",
            "--confirm-admin-conflict", "quarantine"
        });

        var preflight = await TargetPreflight.RunAsync(target, snapshot, options);
        Assert.Empty(preflight.Summary.Blockers);

        var report = new MigrationReport
        {
            Status = "applied",
            Mode = "Apply",
            Manifest = snapshot.Manifest,
            Preflight = preflight.Summary
        };

        await ConflictResolver.QuarantineAsync(target, preflight.Summary.ConflictPlan);
        var auditEventId = await MigrationWriter.ApplyAsync(target, snapshot, preflight.RoleIds);
        report.Apply = new ApplySummary
        {
            Applied = true,
            MigrationAuditEventId = auditEventId,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };

        return report;
    }
}
