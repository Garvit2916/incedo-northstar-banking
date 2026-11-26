using Microsoft.EntityFrameworkCore;
using Northstar.Domain;

namespace Northstar.DataMigration.Tests;

public class MigrationWriterTests
{
    [Fact]
    public async Task WritesSnapshotWithOriginalTimestampsAndTargetRoleIds()
    {
        await using var sourceDatabase = new SqliteTestDatabase();
        await using var source = sourceDatabase.CreateContext();
        var seed = await SeedDataFactory.SeedSourceAsync(source);
        var snapshot = await SourceSnapshotReader.ReadAsync(source, includeOptionalLinkedAgentUsers: false);

        await using var targetDatabase = new SqliteTestDatabase();
        await using var target = targetDatabase.CreateContext();
        var targetRoles = await SeedDataFactory.SeedTargetRolesAsync(target);

        var auditEventId = await MigrationWriter.ApplyAsync(target, snapshot, targetRoles);

        Assert.NotEqual(Guid.Empty, auditEventId);
        Assert.Equal(5, await target.Users.CountAsync());

        var adminRole = await target.UserRoles.SingleAsync(
            role => role.UserId == MigrationScope.AdministratorUserId);
        Assert.Equal(targetRoles[Roles.Administrator], adminRole.RoleId);
        Assert.NotEqual(seed.SourceRoleIds[Roles.Administrator], adminRole.RoleId);

        var product = await target.Products.SingleAsync();
        Assert.Equal(seed.FixedTime, product.CreatedAtUtc);
        Assert.Equal(seed.FixedTime, product.UpdatedAtUtc);
        Assert.Equal(1, product.Version);

        var application = await target.Applications.SingleAsync(item => item.Id == seed.ApplicationId);
        Assert.Equal(seed.FixedTime, application.UpdatedAtUtc);
        Assert.Equal(ApplicationStatus.UnderReview, application.Status);

        Assert.Equal(0, await target.RefreshTokens.CountAsync());
        Assert.Equal(
            MigrationScope.MigrationAction,
            await target.AuditEvents
                .Where(audit => audit.Id == auditEventId)
                .Select(audit => audit.Action)
                .SingleAsync());
    }
}
