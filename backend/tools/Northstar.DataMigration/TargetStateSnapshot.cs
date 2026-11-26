using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Northstar.Domain.Entities;
using Northstar.Infrastructure.Identity;
using Northstar.Infrastructure.Persistence;

namespace Northstar.DataMigration;

/// <summary>
/// Captures counts and ID-only fingerprints so verification can prove that rows
/// outside the migration manifest were not added, removed, or re-keyed.
/// </summary>
public static class TargetStateSnapshot
{
    public static async Task<Dictionary<string, long>> CountsAsync(
        AppDbContext target,
        CancellationToken cancellationToken = default) => new(StringComparer.Ordinal)
    {
        [MigrationTables.Users] = await target.Users.CountAsync(cancellationToken),
        [MigrationTables.UserRoles] = await target.UserRoles.CountAsync(cancellationToken),
        [MigrationTables.Products] = await target.Products.CountAsync(cancellationToken),
        [MigrationTables.CustomerProfiles] = await target.CustomerProfiles.CountAsync(cancellationToken),
        [MigrationTables.Applications] = await target.Applications.CountAsync(cancellationToken),
        [MigrationTables.ApplicationStatusHistory] = await target.ApplicationStatusHistory.CountAsync(cancellationToken),
        [MigrationTables.KycDocuments] = await target.KycDocuments.CountAsync(cancellationToken),
        [MigrationTables.Tickets] = await target.Tickets.CountAsync(cancellationToken),
        [MigrationTables.TicketAssignments] = await target.TicketAssignments.CountAsync(cancellationToken),
        [MigrationTables.TicketMessages] = await target.TicketMessages.CountAsync(cancellationToken),
        [MigrationTables.AiAssistanceRecords] = await target.AiAssistanceRecords.CountAsync(cancellationToken),
        [MigrationTables.Notifications] = await target.Notifications.CountAsync(cancellationToken),
        [MigrationTables.AuditEvents] = await target.AuditEvents.CountAsync(cancellationToken),
        [MigrationTables.RefreshTokens] = await target.RefreshTokens.CountAsync(cancellationToken)
    };

    public static async Task<Dictionary<string, string>> FingerprintsAsync(
        AppDbContext target,
        IReadOnlyDictionary<string, HashSet<Guid>> excludedIdsByTable,
        Guid conflictUserId,
        Guid? migrationAuditEventId,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        result[MigrationTables.Users] = await FingerprintAsync(
            target.Users.Select(u => u.Id),
            Exclusions(excludedIdsByTable, MigrationTables.Users, conflictUserId),
            cancellationToken);

        var userRoleRows = await target.UserRoles
            .AsNoTracking()
            .Select(r => new { r.UserId, r.RoleId })
            .ToListAsync(cancellationToken);
        var migratedUserIds = Exclusions(excludedIdsByTable, MigrationTables.Users);
        result[MigrationTables.UserRoles] = HashOrdered(
            userRoleRows
                .Where(r => !migratedUserIds.Contains(r.UserId))
                .Select(r => $"{r.UserId:N}|{r.RoleId:N}"));

        result[MigrationTables.Products] = await FingerprintAsync(
            target.Products.Select(e => e.Id),
            Exclusions(excludedIdsByTable, MigrationTables.Products),
            cancellationToken);
        result[MigrationTables.CustomerProfiles] = await FingerprintAsync(
            target.CustomerProfiles.Select(e => e.Id),
            Exclusions(excludedIdsByTable, MigrationTables.CustomerProfiles),
            cancellationToken);
        result[MigrationTables.Applications] = await FingerprintAsync(
            target.Applications.Select(e => e.Id),
            Exclusions(excludedIdsByTable, MigrationTables.Applications),
            cancellationToken);
        result[MigrationTables.ApplicationStatusHistory] = await FingerprintAsync(
            target.ApplicationStatusHistory.Select(e => e.Id),
            Exclusions(excludedIdsByTable, MigrationTables.ApplicationStatusHistory),
            cancellationToken);
        result[MigrationTables.KycDocuments] = await FingerprintAsync(
            target.KycDocuments.Select(e => e.Id),
            Exclusions(excludedIdsByTable, MigrationTables.KycDocuments),
            cancellationToken);
        result[MigrationTables.Tickets] = await FingerprintAsync(
            target.Tickets.Select(e => e.Id),
            Exclusions(excludedIdsByTable, MigrationTables.Tickets),
            cancellationToken);
        result[MigrationTables.TicketAssignments] = await FingerprintAsync(
            target.TicketAssignments.Select(e => e.Id),
            Exclusions(excludedIdsByTable, MigrationTables.TicketAssignments),
            cancellationToken);
        result[MigrationTables.TicketMessages] = await FingerprintAsync(
            target.TicketMessages.Select(e => e.Id),
            Exclusions(excludedIdsByTable, MigrationTables.TicketMessages),
            cancellationToken);
        result[MigrationTables.AiAssistanceRecords] = await FingerprintAsync(
            target.AiAssistanceRecords.Select(e => e.Id),
            Exclusions(excludedIdsByTable, MigrationTables.AiAssistanceRecords),
            cancellationToken);
        result[MigrationTables.Notifications] = await FingerprintAsync(
            target.Notifications.Select(e => e.Id),
            Exclusions(excludedIdsByTable, MigrationTables.Notifications),
            cancellationToken);

        var auditExclusions = Exclusions(excludedIdsByTable, MigrationTables.AuditEvents);
        if (migrationAuditEventId.HasValue)
        {
            auditExclusions.Add(migrationAuditEventId.Value);
        }

        result[MigrationTables.AuditEvents] = await FingerprintAsync(
            target.AuditEvents.Select(e => e.Id),
            auditExclusions,
            cancellationToken);
        result[MigrationTables.RefreshTokens] = await FingerprintAsync(
            target.RefreshTokens.Select(e => e.Id),
            Exclusions(excludedIdsByTable, MigrationTables.RefreshTokens),
            cancellationToken);

        return result;
    }

    private static HashSet<Guid> Exclusions(
        IReadOnlyDictionary<string, HashSet<Guid>> excludedIdsByTable,
        string table,
        Guid? additional = null)
    {
        var result = new HashSet<Guid>(excludedIdsByTable.GetValueOrDefault(table) ?? new HashSet<Guid>());
        if (additional.HasValue)
        {
            result.Add(additional.Value);
        }

        return result;
    }

    private static async Task<string> FingerprintAsync(
        IQueryable<Guid> ids,
        HashSet<Guid> excluded,
        CancellationToken cancellationToken)
    {
        var rows = await ids.ToListAsync(cancellationToken);
        return HashOrdered(rows.Where(id => !excluded.Contains(id)).Select(id => id.ToString("N")));
    }

    private static string HashOrdered(IEnumerable<string> values)
    {
        var ordered = values.OrderBy(value => value, StringComparer.Ordinal).ToList();
        var builder = new StringBuilder();
        foreach (var value in ordered)
        {
            builder.Append(value).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
