using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Northstar.Domain.Entities;

namespace Northstar.DataMigration;

public static class MigrationWriter
{
    public static async Task<Guid> ApplyAsync(
        MigrationAppDbContext target,
        SourceSnapshot source,
        IReadOnlyDictionary<string, Guid> targetRoleIds,
        CancellationToken cancellationToken = default)
    {
        target.Users.AddRange(source.Users);
        await target.SaveChangesAsync(cancellationToken);

        var userRoles = source.UserRoles
            .Select(mapping =>
            {
                if (!targetRoleIds.TryGetValue(mapping.RoleName, out var roleId))
                {
                    throw new InvalidOperationException($"Target role '{mapping.RoleName}' was not resolved.");
                }

                return new IdentityUserRole<Guid>
                {
                    UserId = mapping.UserId,
                    RoleId = roleId
                };
            })
            .ToList();
        target.UserRoles.AddRange(userRoles);
        await target.SaveChangesAsync(cancellationToken);

        target.Products.AddRange(source.Products);
        await target.SaveChangesAsync(cancellationToken);

        target.CustomerProfiles.AddRange(source.CustomerProfiles);
        await target.SaveChangesAsync(cancellationToken);

        target.Applications.AddRange(source.Applications);
        await target.SaveChangesAsync(cancellationToken);

        target.ApplicationStatusHistory.AddRange(source.ApplicationStatusHistory);
        await target.SaveChangesAsync(cancellationToken);

        target.KycDocuments.AddRange(source.KycDocuments);
        await target.SaveChangesAsync(cancellationToken);

        target.Tickets.AddRange(source.Tickets);
        await target.SaveChangesAsync(cancellationToken);

        target.TicketAssignments.AddRange(source.TicketAssignments);
        await target.SaveChangesAsync(cancellationToken);

        target.TicketMessages.AddRange(source.TicketMessages);
        await target.SaveChangesAsync(cancellationToken);

        target.AiAssistanceRecords.AddRange(source.AiAssistanceRecords);
        await target.SaveChangesAsync(cancellationToken);

        target.Notifications.AddRange(source.Notifications);
        await target.SaveChangesAsync(cancellationToken);

        target.AuditEvents.AddRange(source.AuditEvents);
        await target.SaveChangesAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var migrationAudit = new AuditEvent
        {
            Id = Guid.NewGuid(),
            ActorUserId = null,
            ActorRole = null,
            Action = MigrationScope.MigrationAction,
            EntityType = "Migration",
            EntityId = source.Manifest.Hash,
            IpAddress = null,
            UserAgent = null,
            CorrelationId = null,
            MetadataJson = JsonSerializer.Serialize(new
            {
                manifestHash = source.Manifest.Hash,
                counts = source.Manifest.Counts,
                refreshTokensMigrated = 0
            }),
            Succeeded = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Version = 0
        };

        target.AuditEvents.Add(migrationAudit);
        await target.SaveChangesAsync(cancellationToken);

        return migrationAudit.Id;
    }
}
