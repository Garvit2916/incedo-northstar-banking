using Microsoft.EntityFrameworkCore;

namespace Northstar.DataMigration.Tests;

public class SourceSnapshotReaderTests
{
    [Fact]
    public async Task ComputesApprovedClosureWithoutRefreshTokensOrOptionalAgents()
    {
        await using var database = new SqliteTestDatabase();
        await using var context = database.CreateContext();
        var seed = await SeedDataFactory.SeedSourceAsync(context);

        var snapshot = await SourceSnapshotReader.ReadAsync(context, includeOptionalLinkedAgentUsers: false);
        var manifest = snapshot.Manifest;

        Assert.Equal(5, manifest.Counts[MigrationTables.Users]);
        Assert.Equal(5, manifest.Counts[MigrationTables.UserRoles]);
        Assert.Equal(2, manifest.Counts[MigrationTables.CustomerProfiles]);
        Assert.Equal(1, manifest.Counts[MigrationTables.Products]);
        Assert.Equal(2, manifest.Counts[MigrationTables.Applications]);
        Assert.Equal(2, manifest.Counts[MigrationTables.ApplicationStatusHistory]);
        Assert.Equal(1, manifest.Counts[MigrationTables.KycDocuments]);
        Assert.Equal(1, manifest.Counts[MigrationTables.Tickets]);
        Assert.Equal(1, manifest.Counts[MigrationTables.TicketAssignments]);
        Assert.Equal(1, manifest.Counts[MigrationTables.TicketMessages]);
        Assert.Equal(1, manifest.Counts[MigrationTables.AiAssistanceRecords]);
        Assert.Equal(1, manifest.Counts[MigrationTables.Notifications]);
        Assert.Equal(2, manifest.Counts[MigrationTables.AuditEvents]);
        Assert.Equal(0, manifest.Counts[MigrationTables.RefreshTokens]);
        Assert.Equal(1, manifest.SourceRefreshTokenCount);

        Assert.Contains(MigrationScope.RequiredLinkedUserId.ToString().ToUpperInvariant(), manifest.UserIds);
        Assert.DoesNotContain(seed.Agent2UserId.ToString().ToUpperInvariant(), manifest.UserIds);
        Assert.Contains(seed.Agent2UserId.ToString().ToUpperInvariant(), manifest.ExcludedSoftUserReferenceIds);

        Assert.DoesNotContain(seed.Agent2UserId, snapshot.Users.Select(user => user.Id));
    }

    [Fact]
    public async Task OptionalAgentUsersCanBeIncludedWhenExplicitlyRequested()
    {
        await using var database = new SqliteTestDatabase();
        await using var context = database.CreateContext();
        var seed = await SeedDataFactory.SeedSourceAsync(context);

        var snapshot = await SourceSnapshotReader.ReadAsync(context, includeOptionalLinkedAgentUsers: true);

        Assert.Equal(11, snapshot.Manifest.Counts[MigrationTables.Users]);
        Assert.Equal(17, snapshot.Manifest.Counts[MigrationTables.UserRoles]);
        Assert.Contains(seed.Agent2UserId.ToString().ToUpperInvariant(), snapshot.Manifest.UserIds);
        Assert.DoesNotContain(seed.Agent2UserId.ToString().ToUpperInvariant(), snapshot.Manifest.ExcludedSoftUserReferenceIds);
    }
}
