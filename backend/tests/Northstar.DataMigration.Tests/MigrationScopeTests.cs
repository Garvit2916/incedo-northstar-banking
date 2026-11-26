using Northstar.Domain;

namespace Northstar.DataMigration.Tests;

public class MigrationScopeTests
{
    [Fact]
    public void RoleNamesAreCanonicalized()
    {
        Assert.Equal(Roles.Administrator, MigrationScope.CanonicalizeRoleName("administrator"));
        Assert.Equal(Roles.Customer, MigrationScope.CanonicalizeRoleName(" CUSTOMER "));
        Assert.Equal(Roles.SupportAgent, MigrationScope.CanonicalizeRoleName("supportagent"));
        Assert.Equal(Roles.Reviewer, MigrationScope.CanonicalizeRoleName("Reviewer"));
        Assert.Null(MigrationScope.CanonicalizeRoleName("Unknown"));
    }

    [Fact]
    public void ApprovedScopeIncludesGarvitButNotOptionalAgents()
    {
        Assert.Equal(5, MigrationScope.ApprovedUserIds.Count);
        Assert.Contains(MigrationScope.RequiredLinkedUserId, MigrationScope.ApprovedUserIds);
        Assert.DoesNotContain(MigrationScope.OptionalLinkedAgentUserIds.First(), MigrationScope.ApprovedUserIds);

        var expanded = MigrationScope.GetAllowedUserIds(includeOptionalLinkedAgentUsers: true);
        Assert.Equal(11, expanded.Count);
        Assert.Contains(MigrationScope.OptionalLinkedAgentUserIds.First(), expanded);
    }

    [Fact]
    public void ExpectedRolesAreDeclaredForAllFourOriginals()
    {
        foreach (var userId in MigrationScope.OriginalUserIds)
        {
            Assert.True(MigrationScope.ExpectedRolesByUserId.ContainsKey(userId));
        }

        Assert.Equal("Administrator", MigrationScope.ExpectedRolesByUserId[MigrationScope.AdministratorUserId].Single());
        Assert.Equal("Customer", MigrationScope.ExpectedRolesByUserId[MigrationScope.CustomerUserId].Single());
    }
}
