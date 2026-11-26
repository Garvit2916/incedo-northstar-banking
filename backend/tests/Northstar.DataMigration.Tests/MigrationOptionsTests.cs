namespace Northstar.DataMigration.Tests;

public class MigrationOptionsTests
{
    [Fact]
    public void DefaultsToDryRun()
    {
        var options = MigrationOptions.Parse(new[]
        {
            "--source", "some.db",
            "--target", "Host=localhost;Database=test"
        });

        Assert.Equal(MigrationMode.DryRun, options.Mode);
        Assert.Contains("Mode=ReadOnly", options.SourceConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(options.Validate());
    }

    [Fact]
    public void ApplyRequiresSafetyConfirmations()
    {
        var options = MigrationOptions.Parse(new[]
        {
            "--source", "some.db",
            "--target", "Host=localhost;Database=test",
            "--apply"
        });

        var errors = options.Validate();
        Assert.Contains(errors, error => error.Contains("--confirm-backup", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("--s3-bucket", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsInlineValuesAndConflictFlag()
    {
        var options = MigrationOptions.Parse(new[]
        {
            "--source=some.db",
            "--target=Host=localhost;Database=test",
            "--s3-bucket=kyc",
            "--confirm-backup",
            "--confirm-admin-conflict=quarantine",
            "--apply"
        });

        Assert.Empty(options.Validate());
        Assert.True(options.ConfirmAdminConflictQuarantine);
        Assert.True(options.ConfirmBackup);
        Assert.Equal(MigrationMode.Apply, options.Mode);
    }

    [Fact]
    public void S3WriteOptionsAreRejected()
    {
        var args = new[]
        {
            "--source", "some.db",
            "--target", "Host=localhost;Database=test"
        };

        Assert.Throws<ArgumentException>(() => MigrationOptions.Parse(args.Append("--copy-missing-blobs").ToArray()));
        Assert.Throws<ArgumentException>(() => MigrationOptions.Parse(args.Append("--local-blob-root").Append("C:\\blobs").ToArray()));
    }

    [Fact]
    public void VerifyAndRollbackRequireAnExistingReport()
    {
        var missingReport = Path.Combine(Path.GetTempPath(), $"northstar-missing-{Guid.NewGuid():N}.json");
        var verify = MigrationOptions.Parse(new[]
        {
            "--target", "Host=localhost;Database=test",
            "--report", missingReport,
            "--verify"
        });
        var rollback = MigrationOptions.Parse(new[]
        {
            "--target", "Host=localhost;Database=test",
            "--report", missingReport,
            "--confirm-rollback",
            "--rollback"
        });

        Assert.Contains(verify.Validate(), error => error.Contains("existing JSON report", StringComparison.Ordinal));
        Assert.Contains(rollback.Validate(), error => error.Contains("existing JSON report", StringComparison.Ordinal));
    }

    [Fact]
    public void OptionalAgentInclusionNeedsSecondConfirmation()
    {
        var options = MigrationOptions.Parse(new[]
        {
            "--source", "some.db",
            "--target", "Host=localhost;Database=test",
            "--include-linked-agent-users"
        });

        Assert.Contains(options.Validate(), error => error.Contains("--confirm-linked-agent-users", StringComparison.Ordinal));
    }
}
