using Microsoft.EntityFrameworkCore;
using Northstar.Infrastructure.Persistence;

namespace Northstar.DataMigration;

public static class ConflictResolver
{
    public static bool CanQuarantine(ConflictPlan plan, bool confirmed) =>
        plan.Action == "quarantine" &&
        confirmed &&
        plan.RelatedRecordCounts.Values.All(count => count == 0);

    public static async Task QuarantineAsync(
        AppDbContext target,
        ConflictPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (!CanQuarantine(plan, confirmed: true))
        {
            throw new InvalidOperationException("Administrator conflict quarantine was not approved.");
        }

        var user = await target.Users
            .SingleAsync(u => u.Id == plan.UserId, cancellationToken);

        user.Email = plan.QuarantinedEmail;
        user.NormalizedEmail = plan.QuarantinedEmail.ToUpperInvariant();
        user.UserName = plan.QuarantinedUserName;
        user.NormalizedUserName = plan.QuarantinedUserName.ToUpperInvariant();
        user.ConcurrencyStamp = Guid.NewGuid().ToString();

        await target.SaveChangesAsync(cancellationToken);
    }

    public static async Task RevertAsync(
        AppDbContext target,
        ConflictPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (plan.Action != "quarantine")
        {
            return;
        }

        var user = await target.Users
            .SingleOrDefaultAsync(u => u.Id == plan.UserId, cancellationToken);

        if (user is null)
        {
            return;
        }

        user.Email = plan.OriginalEmail;
        user.NormalizedEmail = plan.OriginalEmail?.ToUpperInvariant();
        user.UserName = plan.OriginalUserName;
        user.NormalizedUserName = plan.OriginalUserName?.ToUpperInvariant();
        user.ConcurrencyStamp = Guid.NewGuid().ToString();

        await target.SaveChangesAsync(cancellationToken);
    }
}
