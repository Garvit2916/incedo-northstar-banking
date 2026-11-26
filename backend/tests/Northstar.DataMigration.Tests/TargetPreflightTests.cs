using Microsoft.EntityFrameworkCore;

namespace Northstar.DataMigration.Tests;

public class TargetPreflightTests
{
    [Fact]
    public async Task ApprovedQuarantinePlanHasNoBlockers()
    {
        await using var sourceDatabase = new SqliteTestDatabase();
        await using var source = sourceDatabase.CreateContext();
        await SeedDataFactory.SeedSourceAsync(source);
        var snapshot = await SourceSnapshotReader.ReadAsync(source, includeOptionalLinkedAgentUsers: false);

        await using var targetDatabase = new SqliteTestDatabase();
        await using var target = targetDatabase.CreateContext();
        await SeedDataFactory.SeedTargetRolesAsync(target);
        await SeedDataFactory.SeedAppliedPostgresMigrationAsync(target);
        await SeedDataFactory.SeedConflictAdminAsync(target);

        var options = CreateOptions(confirmQuarantine: true);
        var result = await TargetPreflight.RunAsync(target, snapshot, options);

        Assert.Empty(result.Summary.Blockers);
        Assert.True(result.Summary.AppliedMigrationFound);
        Assert.True(result.Summary.ConflictAdminIdentityCollision);
        Assert.Equal("quarantine", result.Summary.ConflictPlan.Action);
        Assert.Equal(4, result.RoleIds.Count);
    }

    [Fact]
    public async Task QuarantineIsBlockedWhenConflictAdminHasRelatedRows()
    {
        await using var sourceDatabase = new SqliteTestDatabase();
        await using var source = sourceDatabase.CreateContext();
        await SeedDataFactory.SeedSourceAsync(source);
        var snapshot = await SourceSnapshotReader.ReadAsync(source, includeOptionalLinkedAgentUsers: false);

        await using var targetDatabase = new SqliteTestDatabase();
        await using var target = targetDatabase.CreateContext();
        await SeedDataFactory.SeedTargetRolesAsync(target);
        await SeedDataFactory.SeedAppliedPostgresMigrationAsync(target);
        await SeedDataFactory.SeedConflictAdminAsync(target);
        await SeedDataFactory.AddConflictProfileAsync(target);

        var result = await TargetPreflight.RunAsync(target, snapshot, CreateOptions(confirmQuarantine: true));

        Assert.Contains(result.Summary.Blockers, blocker => blocker.Contains("related row(s)", StringComparison.Ordinal));
        Assert.Equal("blocked", result.Summary.ConflictPlan.Action);
        Assert.True(await target.CustomerProfiles.AnyAsync());
    }

    [Fact]
    public async Task QuarantineRequiresExplicitConfirmation()
    {
        await using var sourceDatabase = new SqliteTestDatabase();
        await using var source = sourceDatabase.CreateContext();
        await SeedDataFactory.SeedSourceAsync(source);
        var snapshot = await SourceSnapshotReader.ReadAsync(source, includeOptionalLinkedAgentUsers: false);

        await using var targetDatabase = new SqliteTestDatabase();
        await using var target = targetDatabase.CreateContext();
        await SeedDataFactory.SeedTargetRolesAsync(target);
        await SeedDataFactory.SeedAppliedPostgresMigrationAsync(target);
        await SeedDataFactory.SeedConflictAdminAsync(target);

        var result = await TargetPreflight.RunAsync(target, snapshot, CreateOptions(confirmQuarantine: false));

        Assert.Contains(result.Summary.Blockers, blocker => blocker.Contains("--confirm-admin-conflict", StringComparison.Ordinal));
        Assert.Equal("blocked", result.Summary.ConflictPlan.Action);
    }

    private static MigrationOptions CreateOptions(bool confirmQuarantine)
    {
        var args = new List<string>
        {
            "--source", "source.db",
            "--target", "Host=localhost;Database=test"
        };

        if (confirmQuarantine)
        {
            args.AddRange(new[] { "--confirm-admin-conflict", "quarantine" });
        }

        return MigrationOptions.Parse(args.ToArray());
    }
}
