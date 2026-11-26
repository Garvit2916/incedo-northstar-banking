using Microsoft.Data.Sqlite;

namespace Northstar.DataMigration;

public enum MigrationMode
{
    DryRun,
    Apply,
    Verify,
    Rollback
}

public sealed class MigrationOptions
{
    public MigrationMode Mode { get; private set; } = MigrationMode.DryRun;
    public string? SourceConnectionString { get; private set; }
    public string? TargetConnectionString { get; private set; }
    public bool ConfirmAdminConflictQuarantine { get; private set; }
    public bool ConfirmBackup { get; private set; }
    public bool IncludeLinkedAgentUsers { get; private set; }
    public bool ConfirmLinkedAgentUsers { get; private set; }
    public string? S3BucketName { get; private set; }
    public string? S3Region { get; private set; }
    public string ReportPath { get; private set; } = Path.Combine(Directory.GetCurrentDirectory(), "northstar-migration-report.json");
    public bool ConfirmRollback { get; private set; }
    public bool HelpRequested { get; private set; }
    public List<string> Warnings { get; } = new();

    public static MigrationOptions Parse(string[] args)
    {
        var options = new MigrationOptions();

        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument: {token}");
            }

            var separator = token.IndexOf('=', StringComparison.Ordinal);
            var key = separator < 0 ? token : token[..separator];
            var inlineValue = separator < 0 ? null : token[(separator + 1)..];

            switch (key)
            {
                case "--help":
                case "-h":
                    options.HelpRequested = true;
                    break;

                case "--dry-run":
                    options.Mode = MigrationMode.DryRun;
                    break;

                case "--apply":
                    options.Mode = MigrationMode.Apply;
                    break;

                case "--verify":
                    options.Mode = MigrationMode.Verify;
                    break;

                case "--rollback":
                    options.Mode = MigrationMode.Rollback;
                    break;

                case "--source":
                    options.SourceConnectionString = BuildReadOnlySqliteConnection(ReadValue(args, ref index, key, inlineValue));
                    break;

                case "--source-connection":
                    options.SourceConnectionString = BuildReadOnlySqliteConnection(ReadValue(args, ref index, key, inlineValue));
                    break;

                case "--target":
                case "--target-connection":
                    options.TargetConnectionString = ReadValue(args, ref index, key, inlineValue);
                    break;

                case "--confirm-admin-conflict":
                    var conflictValue = ReadValue(args, ref index, key, inlineValue);
                    if (!string.Equals(conflictValue, "quarantine", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ArgumentException("--confirm-admin-conflict only accepts 'quarantine'.");
                    }

                    options.ConfirmAdminConflictQuarantine = true;
                    break;

                case "--confirm-backup":
                    options.ConfirmBackup = true;
                    break;

                case "--include-linked-agent-users":
                    options.IncludeLinkedAgentUsers = true;
                    break;

                case "--confirm-linked-agent-users":
                    options.ConfirmLinkedAgentUsers = true;
                    break;

                case "--s3-bucket":
                    options.S3BucketName = ReadValue(args, ref index, key, inlineValue);
                    break;

                case "--s3-region":
                    options.S3Region = ReadValue(args, ref index, key, inlineValue);
                    break;

                case "--report":
                    options.ReportPath = ReadValue(args, ref index, key, inlineValue);
                    break;

                case "--confirm-rollback":
                    options.ConfirmRollback = true;
                    break;

                default:
                    throw new ArgumentException($"Unknown option: {key}");
            }
        }

        options.TargetConnectionString ??= Environment.GetEnvironmentVariable("NORTHSTAR_TARGET_CONNECTION");
        options.SourceConnectionString ??= BuildReadOnlySqliteConnection(
            Environment.GetEnvironmentVariable("NORTHSTAR_SOURCE_CONNECTION")
            ?? Environment.GetEnvironmentVariable("NORTHSTAR_SOURCE_DB"));

        return options;
    }

    public IReadOnlyList<string> Validate()
    {
        Warnings.Clear();
        var errors = new List<string>();

        if (Mode is MigrationMode.DryRun or MigrationMode.Apply)
        {
            if (string.IsNullOrWhiteSpace(SourceConnectionString))
            {
                errors.Add("--source is required.");
            }

            if (string.IsNullOrWhiteSpace(TargetConnectionString))
            {
                errors.Add("--target or NORTHSTAR_TARGET_CONNECTION is required.");
            }
        }
        else if (string.IsNullOrWhiteSpace(TargetConnectionString))
        {
            errors.Add("--target or NORTHSTAR_TARGET_CONNECTION is required.");
        }

        if ((Mode is MigrationMode.Verify or MigrationMode.Rollback) && !File.Exists(ReportPath))
        {
            errors.Add($"--report must point to an existing JSON report for {Mode} mode.");
        }

        if (Mode == MigrationMode.Apply && !ConfirmBackup)
        {
            errors.Add("--confirm-backup is required before --apply.");
        }

        if (Mode == MigrationMode.Apply && string.IsNullOrWhiteSpace(S3BucketName))
        {
            errors.Add("--s3-bucket is required before --apply so all KYC StorageKey blobs can be verified.");
        }

        if (IncludeLinkedAgentUsers && !ConfirmLinkedAgentUsers)
        {
            errors.Add("--confirm-linked-agent-users is required with --include-linked-agent-users.");
        }

        if (Mode == MigrationMode.Rollback && !ConfirmRollback)
        {
            errors.Add("--confirm-rollback is required before --rollback.");
        }

        if (Mode == MigrationMode.DryRun && string.IsNullOrWhiteSpace(S3BucketName))
        {
            Warnings.Add("No --s3-bucket supplied; KYC blob verification will be skipped in dry-run. --apply requires --s3-bucket.");
        }

        return errors;
    }

    public static string Usage() => """
        Northstar one-time original-account migration utility.

        Dry-run (default; source is read-only and target is inspected read-only):
          dotnet run --project backend/tools/Northstar.DataMigration -- \
            --source "backend/src/Northstar.Api/northstar.db" \
            --target "$env:NORTHSTAR_TARGET_CONNECTION" \
            --s3-bucket my-private-kyc-bucket \
            --report .\migration-report.json

        Apply (requires explicit safety confirmations):
          dotnet run --project backend/tools/Northstar.DataMigration -- \
            --source "backend/src/Northstar.Api/northstar.db" \
            --target "$env:NORTHSTAR_TARGET_CONNECTION" \
            --s3-bucket my-private-kyc-bucket \
            --confirm-admin-conflict quarantine \
            --confirm-backup \
            --report .\migration-report.json \
            --apply

        Verify:
          dotnet run --project backend/tools/Northstar.DataMigration -- \
            --target "$env:NORTHSTAR_TARGET_CONNECTION" \
            --report .\migration-report.json \
            --verify

        Rollback (compensating deletes; prefer a PostgreSQL restore point):
          dotnet run --project backend/tools/Northstar.DataMigration -- \
            --target "$env:NORTHSTAR_TARGET_CONNECTION" \
            --report .\migration-report.json \
            --confirm-rollback \
            --rollback

        Safety flags:
          --include-linked-agent-users --confirm-linked-agent-users
          --confirm-admin-conflict quarantine
          --confirm-backup
          --confirm-rollback

        S3 access is read-only (HEAD and temporary GET for SHA-256 only). The utility
        never uploads, copies, overwrites, deletes, or modifies S3 objects.

        Connection strings may be supplied through NORTHSTAR_SOURCE_CONNECTION /
        NORTHSTAR_SOURCE_DB and NORTHSTAR_TARGET_CONNECTION. The utility prints no
        passwords, password hashes, tokens, or connection strings.
        """;

    private static string ReadValue(string[] args, ref int index, string key, string? inlineValue)
    {
        if (inlineValue is not null)
        {
            return inlineValue;
        }

        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{key} requires a value.");
        }

        index++;
        return args[index];
    }

    private static string BuildReadOnlySqliteConnection(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        var builder = source.Contains('=', StringComparison.Ordinal)
            ? new SqliteConnectionStringBuilder(source)
            : new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(source) };

        builder.Mode = SqliteOpenMode.ReadOnly;
        builder.Pooling = false;
        return builder.ToString();
    }
}
