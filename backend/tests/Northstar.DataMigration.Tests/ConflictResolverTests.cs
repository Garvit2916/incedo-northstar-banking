namespace Northstar.DataMigration.Tests;

public class ConflictResolverTests
{
    [Fact]
    public void QuarantineRequiresApprovalAndNoRelatedRows()
    {
        var plan = new ConflictPlan
        {
            Action = "quarantine",
            UserId = MigrationScope.ConflictAdminUserId,
            RelatedRecordCounts = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["AuditEvents.ActorUserId"] = 0
            }
        };

        Assert.False(ConflictResolver.CanQuarantine(plan, confirmed: false));
        Assert.True(ConflictResolver.CanQuarantine(plan, confirmed: true));

        plan.RelatedRecordCounts["AuditEvents.ActorUserId"] = 1;
        Assert.False(ConflictResolver.CanQuarantine(plan, confirmed: true));
    }

    [Fact]
    public void BlockedPlansCannotBeApplied()
    {
        var plan = new ConflictPlan { Action = "blocked" };
        Assert.False(ConflictResolver.CanQuarantine(plan, confirmed: true));
    }
}
