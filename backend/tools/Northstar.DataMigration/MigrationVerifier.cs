using Microsoft.EntityFrameworkCore;
using Northstar.Domain.Entities;
using Northstar.Infrastructure.Identity;
using Northstar.Infrastructure.Persistence;

namespace Northstar.DataMigration;

public static class MigrationVerifier
{
    public static async Task<Dictionary<string, string>> VerifyAsync(
        AppDbContext target,
        MigrationReport report,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var manifest = report.Manifest;
        var excludedIds = manifest.IdSetsByTable();

        result["report.status"] = report.Status is "applied" or "verified" or "verification-failed"
            ? "ok"
            : $"unexpected report status '{report.Status}'";
        result["manifest.hash"] = string.IsNullOrWhiteSpace(manifest.Hash) ? "manifest hash is missing" : "ok";

        await AddMissingIdsAsync<ApplicationUser>(target, result, MigrationTables.Users, excludedIds, cancellationToken);
        await AddMissingIdsAsync<Product>(target, result, MigrationTables.Products, excludedIds, cancellationToken);
        await AddMissingIdsAsync<CustomerProfile>(target, result, MigrationTables.CustomerProfiles, excludedIds, cancellationToken);
        await AddMissingIdsAsync<Domain.Entities.Application>(target, result, MigrationTables.Applications, excludedIds, cancellationToken);
        await AddMissingIdsAsync<ApplicationStatusHistory>(target, result, MigrationTables.ApplicationStatusHistory, excludedIds, cancellationToken);
        await AddMissingIdsAsync<KycDocument>(target, result, MigrationTables.KycDocuments, excludedIds, cancellationToken);
        await AddMissingIdsAsync<Ticket>(target, result, MigrationTables.Tickets, excludedIds, cancellationToken);
        await AddMissingIdsAsync<TicketAssignment>(target, result, MigrationTables.TicketAssignments, excludedIds, cancellationToken);
        await AddMissingIdsAsync<TicketMessage>(target, result, MigrationTables.TicketMessages, excludedIds, cancellationToken);
        await AddMissingIdsAsync<AiAssistanceRecord>(target, result, MigrationTables.AiAssistanceRecords, excludedIds, cancellationToken);
        await AddMissingIdsAsync<Notification>(target, result, MigrationTables.Notifications, excludedIds, cancellationToken);
        await AddMissingIdsAsync<AuditEvent>(target, result, MigrationTables.AuditEvents, excludedIds, cancellationToken);

        await AddContentCheckAsync<ApplicationUser>(target, result, MigrationTables.Users, excludedIds, manifest, cancellationToken);
        await AddContentCheckAsync<Product>(target, result, MigrationTables.Products, excludedIds, manifest, cancellationToken);
        await AddContentCheckAsync<CustomerProfile>(target, result, MigrationTables.CustomerProfiles, excludedIds, manifest, cancellationToken);
        await AddContentCheckAsync<Domain.Entities.Application>(target, result, MigrationTables.Applications, excludedIds, manifest, cancellationToken);
        await AddContentCheckAsync<ApplicationStatusHistory>(target, result, MigrationTables.ApplicationStatusHistory, excludedIds, manifest, cancellationToken);
        await AddContentCheckAsync<KycDocument>(target, result, MigrationTables.KycDocuments, excludedIds, manifest, cancellationToken);
        await AddContentCheckAsync<Ticket>(target, result, MigrationTables.Tickets, excludedIds, manifest, cancellationToken);
        await AddContentCheckAsync<TicketAssignment>(target, result, MigrationTables.TicketAssignments, excludedIds, manifest, cancellationToken);
        await AddContentCheckAsync<TicketMessage>(target, result, MigrationTables.TicketMessages, excludedIds, manifest, cancellationToken);
        await AddContentCheckAsync<AiAssistanceRecord>(target, result, MigrationTables.AiAssistanceRecords, excludedIds, manifest, cancellationToken);
        await AddContentCheckAsync<Notification>(target, result, MigrationTables.Notifications, excludedIds, manifest, cancellationToken);
        await AddContentCheckAsync<AuditEvent>(target, result, MigrationTables.AuditEvents, excludedIds, manifest, cancellationToken);

        var roleRows = await target.UserRoles
            .AsNoTracking()
            .Select(r => new { r.UserId, r.RoleId })
            .ToListAsync(cancellationToken);
        var roleMismatchCount = 0;
        foreach (var mapping in manifest.UserRoleMappings)
        {
            if (!report.Preflight.TargetRoleIds.TryGetValue(mapping.RoleName, out var targetRoleId) ||
                !roleRows.Any(r => r.UserId == mapping.UserId && r.RoleId == targetRoleId))
            {
                roleMismatchCount++;
            }
        }

        result["roles"] = roleMismatchCount == 0
            ? "ok"
            : $"{roleMismatchCount} migrated user-role mapping(s) did not match target role IDs";

        var countsAfter = await TargetStateSnapshot.CountsAsync(target, cancellationToken);
        foreach (var table in MigrationTables.Migrated)
        {
            var before = report.Preflight.TargetCountsBefore.GetValueOrDefault(table, 0);
            var migrated = manifest.Counts.GetValueOrDefault(table, 0);
            var migrationAuditEvent = table == MigrationTables.AuditEvents ? 1 : 0;
            var expected = before + migrated + migrationAuditEvent;
            var actual = countsAfter.GetValueOrDefault(table, 0);
            result[$"count.{table}"] = expected == actual ? "ok" : $"expected {expected}, found {actual}";
        }

        var refreshBefore = report.Preflight.TargetCountsBefore.GetValueOrDefault(MigrationTables.RefreshTokens, 0);
        var refreshAfter = countsAfter.GetValueOrDefault(MigrationTables.RefreshTokens, 0);
        result[$"count.{MigrationTables.RefreshTokens}"] =
            refreshBefore == refreshAfter ? "ok" : $"expected unchanged {refreshBefore}, found {refreshAfter}";

        var fingerprintsAfter = await TargetStateSnapshot.FingerprintsAsync(
            target,
            excludedIds,
            MigrationScope.ConflictAdminUserId,
            report.Apply.MigrationAuditEventId,
            cancellationToken);
        var fingerprintMismatches = report.Preflight.UnrelatedFingerprintsBefore
            .Where(pair => !string.Equals(
                pair.Value,
                fingerprintsAfter.GetValueOrDefault(pair.Key),
                StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ToList();
        result["unrelated.fingerprints"] = fingerprintMismatches.Count == 0
            ? "ok"
            : "changed tables: " + string.Join(", ", fingerprintMismatches);

        var migrationAudit = await target.AuditEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == report.Apply.MigrationAuditEventId, cancellationToken);
        result["migration.auditEvent"] = migrationAudit is not null &&
                                         migrationAudit.Action == MigrationScope.MigrationAction &&
                                         migrationAudit.EntityId == manifest.Hash
            ? "ok"
            : "migration audit event missing or inconsistent";

        if (report.Preflight.ConflictPlan.Action == "quarantine")
        {
            var conflictUser = await target.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(u => u.Id == report.Preflight.ConflictPlan.UserId, cancellationToken);
            result["conflict.quarantined"] = conflictUser is not null &&
                                            string.Equals(
                                                conflictUser.NormalizedUserName,
                                                report.Preflight.ConflictPlan.QuarantinedUserName.ToUpperInvariant(),
                                                StringComparison.Ordinal) &&
                                            string.Equals(
                                                conflictUser.NormalizedEmail,
                                                report.Preflight.ConflictPlan.QuarantinedEmail.ToUpperInvariant(),
                                                StringComparison.Ordinal)
                ? "ok"
                : "conflict admin quarantine values were not preserved";
        }

        return result;
    }

    public static async Task<Dictionary<string, string>> VerifyRollbackAsync(
        AppDbContext target,
        MigrationReport report,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var ids = report.Manifest.IdSetsByTable();

        await AddUnexpectedIdsAsync<ApplicationUser>(target, result, MigrationTables.Users, ids, cancellationToken);
        await AddUnexpectedIdsAsync<Product>(target, result, MigrationTables.Products, ids, cancellationToken);
        await AddUnexpectedIdsAsync<CustomerProfile>(target, result, MigrationTables.CustomerProfiles, ids, cancellationToken);
        await AddUnexpectedIdsAsync<Domain.Entities.Application>(target, result, MigrationTables.Applications, ids, cancellationToken);
        await AddUnexpectedIdsAsync<ApplicationStatusHistory>(target, result, MigrationTables.ApplicationStatusHistory, ids, cancellationToken);
        await AddUnexpectedIdsAsync<KycDocument>(target, result, MigrationTables.KycDocuments, ids, cancellationToken);
        await AddUnexpectedIdsAsync<Ticket>(target, result, MigrationTables.Tickets, ids, cancellationToken);
        await AddUnexpectedIdsAsync<TicketAssignment>(target, result, MigrationTables.TicketAssignments, ids, cancellationToken);
        await AddUnexpectedIdsAsync<TicketMessage>(target, result, MigrationTables.TicketMessages, ids, cancellationToken);
        await AddUnexpectedIdsAsync<AiAssistanceRecord>(target, result, MigrationTables.AiAssistanceRecords, ids, cancellationToken);
        await AddUnexpectedIdsAsync<Notification>(target, result, MigrationTables.Notifications, ids, cancellationToken);
        await AddUnexpectedIdsAsync<AuditEvent>(target, result, MigrationTables.AuditEvents, ids, cancellationToken);

        var userIds = ids[MigrationTables.Users];
        var roleRowsRemaining = await target.UserRoles
            .AsNoTracking()
            .CountAsync(role => userIds.Contains(role.UserId), cancellationToken);
        result[$"rollback.ids.{MigrationTables.UserRoles}"] = roleRowsRemaining == 0
            ? "ok"
            : $"{roleRowsRemaining} migrated user-role row(s) still present";

        var migrationAudit = await target.AuditEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(audit => audit.Id == report.Apply.MigrationAuditEventId, cancellationToken);
        result["rollback.migration.auditEvent"] = migrationAudit is null
            ? "ok"
            : "migration audit event still present";

        var refreshBefore = report.Preflight.TargetCountsBefore.GetValueOrDefault(MigrationTables.RefreshTokens, 0);
        var refreshAfter = await target.RefreshTokens.CountAsync(cancellationToken);
        result[$"rollback.count.{MigrationTables.RefreshTokens}"] =
            refreshBefore == refreshAfter ? "ok" : $"expected unchanged {refreshBefore}, found {refreshAfter}";

        if (report.Preflight.ConflictPlan.Action == "quarantine")
        {
            var plan = report.Preflight.ConflictPlan;
            var conflictUser = await target.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(user => user.Id == plan.UserId, cancellationToken);
            result["rollback.conflict.restored"] = conflictUser is not null &&
                                                   string.Equals(conflictUser.Email, plan.OriginalEmail, StringComparison.Ordinal) &&
                                                   string.Equals(conflictUser.UserName, plan.OriginalUserName, StringComparison.Ordinal) &&
                                                   string.Equals(
                                                       conflictUser.NormalizedEmail,
                                                       plan.OriginalEmail?.ToUpperInvariant(),
                                                       StringComparison.Ordinal) &&
                                                   string.Equals(
                                                       conflictUser.NormalizedUserName,
                                                       plan.OriginalUserName?.ToUpperInvariant(),
                                                       StringComparison.Ordinal)
                ? "ok"
                : "conflict admin identity was not restored exactly";
        }

        return result;
    }

    private static async Task AddContentCheckAsync<TEntity>(
        AppDbContext target,
        Dictionary<string, string> result,
        string table,
        IReadOnlyDictionary<string, HashSet<Guid>> idsByTable,
        MigrationManifest manifest,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var ids = idsByTable.GetValueOrDefault(table) ?? new HashSet<Guid>();
        if (!manifest.ContentHashes.TryGetValue(table, out var expected))
        {
            result[$"rows.content.{table}"] = "source content hash is missing";
            return;
        }

        var rows = await target.Set<TEntity>()
            .AsNoTracking()
            .Where(entity => ids.Contains(EF.Property<Guid>(entity, "Id")))
            .ToListAsync(cancellationToken);

        var actual = ContentFingerprint.Compute(target, rows);
        result[$"rows.content.{table}"] = string.Equals(expected, actual, StringComparison.Ordinal)
            ? "ok"
            : "migrated row content differs from source";
    }

    private static async Task AddMissingIdsAsync<TEntity>(
        AppDbContext target,
        Dictionary<string, string> result,
        string table,
        IReadOnlyDictionary<string, HashSet<Guid>> excludedIdsByTable,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var ids = excludedIdsByTable.GetValueOrDefault(table) ?? new HashSet<Guid>();
        var found = await target.Set<TEntity>()
            .AsNoTracking()
            .Where(e => ids.Contains(EF.Property<Guid>(e, "Id")))
            .Select(e => EF.Property<Guid>(e, "Id"))
            .ToListAsync(cancellationToken);

        var missing = ids.Except(found).ToList();
        result[$"ids.{table}"] = missing.Count == 0
            ? "ok"
            : $"{missing.Count} expected ID(s) missing";
    }

    private static async Task AddUnexpectedIdsAsync<TEntity>(
        AppDbContext target,
        Dictionary<string, string> result,
        string table,
        IReadOnlyDictionary<string, HashSet<Guid>> idsByTable,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var ids = idsByTable.GetValueOrDefault(table) ?? new HashSet<Guid>();
        if (ids.Count == 0)
        {
            result[$"rollback.ids.{table}"] = "ok";
            return;
        }

        var found = await target.Set<TEntity>()
            .AsNoTracking()
            .Where(entity => ids.Contains(EF.Property<Guid>(entity, "Id")))
            .Select(entity => EF.Property<Guid>(entity, "Id"))
            .ToListAsync(cancellationToken);

        result[$"rollback.ids.{table}"] = found.Count == 0
            ? "ok"
            : $"{found.Count} migrated row(s) still present";
    }
}
