using Microsoft.EntityFrameworkCore;
using Northstar.Domain.Entities;
using Northstar.Infrastructure.Identity;

namespace Northstar.DataMigration;

public static class RollbackExecutor
{
    public static async Task ExecuteAsync(
        MigrationAppDbContext target,
        MigrationReport report,
        CancellationToken cancellationToken = default)
    {
        var ids = report.Manifest.IdSetsByTable();

        var auditIds = new HashSet<Guid>(ids[MigrationTables.AuditEvents]);
        if (report.Apply.MigrationAuditEventId != Guid.Empty)
        {
            auditIds.Add(report.Apply.MigrationAuditEventId);
        }

        await DeleteByIdAsync<AuditEvent>(target, MigrationTables.AuditEvents, auditIds, cancellationToken);
        await DeleteByIdAsync<Notification>(target, MigrationTables.Notifications, ids, cancellationToken);
        await DeleteByIdAsync<AiAssistanceRecord>(target, MigrationTables.AiAssistanceRecords, ids, cancellationToken);
        await DeleteByIdAsync<TicketMessage>(target, MigrationTables.TicketMessages, ids, cancellationToken);
        await DeleteByIdAsync<TicketAssignment>(target, MigrationTables.TicketAssignments, ids, cancellationToken);
        await DeleteByIdAsync<Ticket>(target, MigrationTables.Tickets, ids, cancellationToken);
        await DeleteByIdAsync<KycDocument>(target, MigrationTables.KycDocuments, ids, cancellationToken);
        await DeleteByIdAsync<ApplicationStatusHistory>(target, MigrationTables.ApplicationStatusHistory, ids, cancellationToken);
        await DeleteByIdAsync<Domain.Entities.Application>(target, MigrationTables.Applications, ids, cancellationToken);
        await DeleteByIdAsync<CustomerProfile>(target, MigrationTables.CustomerProfiles, ids, cancellationToken);
        await DeleteByIdAsync<Product>(target, MigrationTables.Products, ids, cancellationToken);

        var userIds = ids[MigrationTables.Users];
        var userRoles = await target.UserRoles
            .AsNoTracking()
            .Where(r => userIds.Contains(r.UserId))
            .ToListAsync(cancellationToken);
        target.UserRoles.RemoveRange(userRoles);
        await target.SaveChangesAsync(cancellationToken);

        await DeleteByIdAsync<ApplicationUser>(
            target,
            MigrationTables.Users,
            userIds,
            cancellationToken);

        if (report.Preflight.ConflictPlan.Action == "quarantine")
        {
            await ConflictResolver.RevertAsync(target, report.Preflight.ConflictPlan, cancellationToken);
        }
    }

    private static async Task DeleteByIdAsync<TEntity>(
        MigrationAppDbContext target,
        string table,
        IReadOnlyDictionary<string, HashSet<Guid>> idsByTable,
        CancellationToken cancellationToken)
        where TEntity : class =>
        await DeleteByIdAsync<TEntity>(target, table, idsByTable.GetValueOrDefault(table) ?? new HashSet<Guid>(), cancellationToken);

    private static async Task DeleteByIdAsync<TEntity>(
        MigrationAppDbContext target,
        string table,
        HashSet<Guid> ids,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        if (ids.Count == 0)
        {
            return;
        }

        var rows = await target.Set<TEntity>()
            .AsNoTracking()
            .Where(e => ids.Contains(EF.Property<Guid>(e, "Id")))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return;
        }

        target.Set<TEntity>().RemoveRange(rows);
        await target.SaveChangesAsync(cancellationToken);
    }
}
