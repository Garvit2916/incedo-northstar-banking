using Microsoft.EntityFrameworkCore;
using Northstar.Domain;
using Northstar.Domain.Entities;
using Northstar.Infrastructure.Identity;
using Northstar.Infrastructure.Persistence;

namespace Northstar.DataMigration;

public sealed class TargetPreflightResult
{
    public required PreflightSummary Summary { get; init; }
    public required IReadOnlyDictionary<string, Guid> RoleIds { get; init; }
}

public static class TargetPreflight
{
    public static async Task<TargetPreflightResult> RunAsync(
        AppDbContext target,
        SourceSnapshot source,
        MigrationOptions options,
        CancellationToken cancellationToken = default)
    {
        var blockers = new List<string>();
        var warnings = new List<string>(options.Warnings);

        var appliedMigrations = await target.Database.GetAppliedMigrationsAsync(cancellationToken);
        var migrationApplied = appliedMigrations.Contains(MigrationScope.PostgresMigrationId, StringComparer.Ordinal);
        if (!migrationApplied)
        {
            blockers.Add($"Target is missing migration {MigrationScope.PostgresMigrationId}.");
        }

        var roleIds = await ResolveRolesAsync(target, blockers, cancellationToken);
        var excludedIds = source.Manifest.IdSetsByTable();

        var idCollisions = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        await AddIdCollisionsAsync<ApplicationUser>(target, MigrationTables.Users, excludedIds, idCollisions, cancellationToken);
        await AddIdCollisionsAsync<Product>(target, MigrationTables.Products, excludedIds, idCollisions, cancellationToken);
        await AddIdCollisionsAsync<CustomerProfile>(target, MigrationTables.CustomerProfiles, excludedIds, idCollisions, cancellationToken);
        await AddIdCollisionsAsync<Domain.Entities.Application>(target, MigrationTables.Applications, excludedIds, idCollisions, cancellationToken);
        await AddIdCollisionsAsync<ApplicationStatusHistory>(target, MigrationTables.ApplicationStatusHistory, excludedIds, idCollisions, cancellationToken);
        await AddIdCollisionsAsync<KycDocument>(target, MigrationTables.KycDocuments, excludedIds, idCollisions, cancellationToken);
        await AddIdCollisionsAsync<Ticket>(target, MigrationTables.Tickets, excludedIds, idCollisions, cancellationToken);
        await AddIdCollisionsAsync<TicketAssignment>(target, MigrationTables.TicketAssignments, excludedIds, idCollisions, cancellationToken);
        await AddIdCollisionsAsync<TicketMessage>(target, MigrationTables.TicketMessages, excludedIds, idCollisions, cancellationToken);
        await AddIdCollisionsAsync<AiAssistanceRecord>(target, MigrationTables.AiAssistanceRecords, excludedIds, idCollisions, cancellationToken);
        await AddIdCollisionsAsync<Notification>(target, MigrationTables.Notifications, excludedIds, idCollisions, cancellationToken);
        await AddIdCollisionsAsync<AuditEvent>(target, MigrationTables.AuditEvents, excludedIds, idCollisions, cancellationToken);

        var userIds = source.Manifest.UserIds.Select(Guid.Parse).ToHashSet();
        var roleRowsForUsers = await target.UserRoles
            .AsNoTracking()
            .CountAsync(r => userIds.Contains(r.UserId), cancellationToken);
        if (roleRowsForUsers > 0)
        {
            idCollisions[MigrationTables.UserRoles] = new List<string> { $"{roleRowsForUsers} row(s) reference migrated user IDs" };
        }

        foreach (var (table, collisions) in idCollisions)
        {
            if (collisions.Count > 0)
            {
                blockers.Add($"Target ID collision in {table}: {collisions.Count} row(s).");
            }
        }

        var uniqueKeyCheck = await CheckUniqueKeysAsync(target, source, warnings, cancellationToken);
        var uniqueKeyCollisions = uniqueKeyCheck.Collisions;
        var conflictAdminIdentityCollision = uniqueKeyCheck.ConflictAdminIdentityCollision;
        foreach (var (key, count) in uniqueKeyCollisions)
        {
            if (count > 0)
            {
                blockers.Add($"Target unique-key collision ({key}): {count} row(s).");
            }
        }

        var countsBefore = await TargetStateSnapshot.CountsAsync(target, cancellationToken);
        var fingerprintsBefore = await TargetStateSnapshot.FingerprintsAsync(
            target,
            excludedIds,
            MigrationScope.ConflictAdminUserId,
            migrationAuditEventId: null,
            cancellationToken);

        var conflictPlan = await BuildConflictPlanAsync(
            target,
            options,
            blockers,
            warnings,
            cancellationToken);

        var summary = new PreflightSummary
        {
            AppliedMigrationFound = migrationApplied,
            ConflictAdminIdentityCollision = conflictAdminIdentityCollision,
            TargetRoleIds = roleIds.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            IdCollisions = idCollisions,
            UniqueKeyCollisions = uniqueKeyCollisions,
            TargetCountsBefore = countsBefore,
            UnrelatedFingerprintsBefore = fingerprintsBefore,
            ConflictPlan = conflictPlan,
            Blockers = blockers,
            Warnings = warnings
        };

        return new TargetPreflightResult
        {
            Summary = summary,
            RoleIds = roleIds
        };
    }

    private static async Task<IReadOnlyDictionary<string, Guid>> ResolveRolesAsync(
        AppDbContext target,
        List<string> blockers,
        CancellationToken cancellationToken)
    {
        var roles = await target.Roles.AsNoTracking().ToListAsync(cancellationToken);
        var result = new Dictionary<string, Guid>(StringComparer.Ordinal);

        foreach (var roleName in Roles.All)
        {
            var matches = roles
                .Where(r => string.Equals(
                    MigrationScope.CanonicalizeRoleName(r.NormalizedName ?? r.Name),
                    roleName,
                    StringComparison.Ordinal))
                .ToList();

            if (matches.Count != 1)
            {
                blockers.Add($"Expected exactly one target role '{roleName}', found {matches.Count}.");
                continue;
            }

            result[roleName] = matches[0].Id;
        }

        return result;
    }

    private static async Task AddIdCollisionsAsync<TEntity>(
        AppDbContext target,
        string table,
        IReadOnlyDictionary<string, HashSet<Guid>> excludedIdsByTable,
        Dictionary<string, List<string>> collisions,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var ids = excludedIdsByTable.GetValueOrDefault(table) ?? new HashSet<Guid>();
        if (ids.Count == 0)
        {
            return;
        }

        var found = await target.Set<TEntity>()
            .AsNoTracking()
            .Where(e => ids.Contains(EF.Property<Guid>(e, "Id")))
            .Select(e => EF.Property<Guid>(e, "Id"))
            .ToListAsync(cancellationToken);

        if (found.Count > 0)
        {
            collisions[table] = found.Select(id => id.ToString().ToUpperInvariant()).ToList();
        }
    }

    private static async Task<(Dictionary<string, long> Collisions, bool ConflictAdminIdentityCollision)> CheckUniqueKeysAsync(
        AppDbContext target,
        SourceSnapshot source,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);

        var userNames = source.Users
            .Where(u => u.NormalizedUserName is not null)
            .Select(u => u.NormalizedUserName!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var emails = source.Users
            .Where(u => u.NormalizedEmail is not null)
            .Select(u => u.NormalizedEmail!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var conflictingUsers = await target.Users
            .AsNoTracking()
            .Where(u =>
                (u.NormalizedUserName != null && userNames.Contains(u.NormalizedUserName)) ||
                (u.NormalizedEmail != null && emails.Contains(u.NormalizedEmail)))
            .Select(u => new { u.Id, u.Email, u.NormalizedEmail, u.UserName, u.NormalizedUserName })
            .ToListAsync(cancellationToken);

        var conflictId = MigrationScope.ConflictAdminUserId;
        var conflictUser = conflictingUsers.FirstOrDefault(u => u.Id == conflictId);
        var conflictAdminIdentityCollision = conflictUser is not null && IsAdminIdentity(
            conflictUser.Id,
            conflictUser.Email,
            conflictUser.NormalizedEmail,
            conflictUser.UserName,
            conflictUser.NormalizedUserName);
        var unexpectedUserConflicts = conflictingUsers
            .Where(u => u.Id != conflictId || !conflictAdminIdentityCollision)
            .ToList();

        result["AspNetUsers.NormalizedUserNameOrEmail"] = unexpectedUserConflicts.Count;

        var productCodes = source.Products.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);
        result["Products.Code"] = await target.Products
            .CountAsync(p => productCodes.Contains(p.Code), cancellationToken);

        var applicationReferences = source.Applications.Select(a => a.Reference).ToHashSet(StringComparer.Ordinal);
        result["Applications.Reference"] = await target.Applications
            .CountAsync(a => applicationReferences.Contains(a.Reference), cancellationToken);

        var ticketReferences = source.Tickets.Select(t => t.Reference).ToHashSet(StringComparer.Ordinal);
        result["Tickets.Reference"] = await target.Tickets
            .CountAsync(t => ticketReferences.Contains(t.Reference), cancellationToken);

        var profileUserIds = source.CustomerProfiles.Select(p => p.UserId).ToHashSet();
        result["CustomerProfiles.UserId"] = await target.CustomerProfiles
            .CountAsync(p => profileUserIds.Contains(p.UserId), cancellationToken);

        if (conflictAdminIdentityCollision)
        {
            warnings.Add(
                $"Known conflict admin {conflictId.ToString().ToUpperInvariant()} occupies a migrated identity; " +
                "it is handled by the conflict plan, not as an unexpected unique-key collision.");
        }

        return (result, conflictAdminIdentityCollision);
    }

    private static async Task<ConflictPlan> BuildConflictPlanAsync(
        AppDbContext target,
        MigrationOptions options,
        List<string> blockers,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var conflictUser = await target.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == MigrationScope.ConflictAdminUserId, cancellationToken);

        if (conflictUser is null)
        {
            return new ConflictPlan { Action = "none" };
        }

        var blocksAdminIdentity = IsAdminIdentity(
            conflictUser.Id,
            conflictUser.Email,
            conflictUser.NormalizedEmail,
            conflictUser.UserName,
            conflictUser.NormalizedUserName);

        var relatedCounts = await CountConflictReferencesAsync(target, cancellationToken);

        if (!blocksAdminIdentity)
        {
            warnings.Add("The conflict admin account exists but does not occupy the administrator email/username.");
            return new ConflictPlan
            {
                UserId = conflictUser.Id,
                Action = "none",
                OriginalEmail = conflictUser.Email,
                OriginalUserName = conflictUser.UserName,
                RelatedRecordCounts = relatedCounts
            };
        }

        var totalRelated = relatedCounts.Values.Sum();
        if (totalRelated > 0)
        {
            blockers.Add(
                $"Conflict admin {MigrationScope.ConflictAdminUserId.ToString().ToUpperInvariant()} has {totalRelated} related row(s); quarantine requires a separate decision.");
        }

        if (!options.ConfirmAdminConflictQuarantine)
        {
            blockers.Add(
                "Administrator email/username conflict requires --confirm-admin-conflict quarantine.");
        }

        var quarantineIdentity =
            $"conflict-{MigrationScope.ConflictAdminUserId.ToString().ToLowerInvariant()}@northstar.invalid";

        return new ConflictPlan
        {
            UserId = conflictUser.Id,
            Action = totalRelated == 0 && options.ConfirmAdminConflictQuarantine
                ? "quarantine"
                : "blocked",
            OriginalEmail = conflictUser.Email,
            OriginalUserName = conflictUser.UserName,
            QuarantinedEmail = quarantineIdentity,
            QuarantinedUserName = quarantineIdentity,
            RelatedRecordCounts = relatedCounts
        };
    }

    private static bool IsAdminIdentity(
        Guid userId,
        string? email,
        string? normalizedEmail,
        string? userName,
        string? normalizedUserName)
    {
        if (userId != MigrationScope.ConflictAdminUserId)
        {
            return false;
        }

        var adminIdentity = MigrationScope.AdminEmail;
        return new[] { email, normalizedEmail, userName, normalizedUserName }
            .Any(value => string.Equals(value, adminIdentity, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<Dictionary<string, long>> CountConflictReferencesAsync(
        AppDbContext target,
        CancellationToken cancellationToken)
    {
        var id = MigrationScope.ConflictAdminUserId;
        var result = new Dictionary<string, long>(StringComparer.Ordinal);

        result["AspNetUserRoles"] = await CountAsync(
            target, $"SELECT COUNT(*) AS \"Value\" FROM \"AspNetUserRoles\" WHERE \"UserId\" = {id}", cancellationToken);
        result["AspNetUserClaims"] = await CountAsync(
            target, $"SELECT COUNT(*) AS \"Value\" FROM \"AspNetUserClaims\" WHERE \"UserId\" = {id}", cancellationToken);
        result["AspNetUserLogins"] = await CountAsync(
            target, $"SELECT COUNT(*) AS \"Value\" FROM \"AspNetUserLogins\" WHERE \"UserId\" = {id}", cancellationToken);
        result["AspNetUserTokens"] = await CountAsync(
            target, $"SELECT COUNT(*) AS \"Value\" FROM \"AspNetUserTokens\" WHERE \"UserId\" = {id}", cancellationToken);
        result["CustomerProfiles"] = await CountAsync(
            target, $"SELECT COUNT(*) AS \"Value\" FROM \"CustomerProfiles\" WHERE \"UserId\" = {id}", cancellationToken);
        result["Notifications"] = await CountAsync(
            target, $"SELECT COUNT(*) AS \"Value\" FROM \"Notifications\" WHERE \"UserId\" = {id}", cancellationToken);
        result["Applications.AssignedReviewerId"] = await CountAsync(
            target, $"SELECT COUNT(*) AS \"Value\" FROM \"Applications\" WHERE \"AssignedReviewerId\" = {id}", cancellationToken);
        result["Tickets"] = await CountAsync(
            target,
            $"SELECT COUNT(*) AS \"Value\" FROM \"Tickets\" WHERE \"CustomerUserId\" = {id} OR \"AssignedAgentId\" = {id}",
            cancellationToken);
        result["TicketAssignments"] = await CountAsync(
            target,
            $"SELECT COUNT(*) AS \"Value\" FROM \"TicketAssignments\" WHERE \"AgentUserId\" = {id} OR \"AssignedByUserId\" = {id}",
            cancellationToken);
        result["TicketMessages"] = await CountAsync(
            target, $"SELECT COUNT(*) AS \"Value\" FROM \"TicketMessages\" WHERE \"AuthorUserId\" = {id}", cancellationToken);
        result["KycDocuments"] = await CountAsync(
            target, $"SELECT COUNT(*) AS \"Value\" FROM \"KycDocuments\" WHERE \"ReviewedByUserId\" = {id}", cancellationToken);
        result["AiAssistanceRecords"] = await CountAsync(
            target, $"SELECT COUNT(*) AS \"Value\" FROM \"AiAssistanceRecords\" WHERE \"CreatedByUserId\" = {id}", cancellationToken);
        result["ApplicationStatusHistory"] = await CountAsync(
            target, $"SELECT COUNT(*) AS \"Value\" FROM \"ApplicationStatusHistory\" WHERE \"ChangedByUserId\" = {id}", cancellationToken);
        result["AuditEvents.ActorUserId"] = await CountAsync(
            target, $"SELECT COUNT(*) AS \"Value\" FROM \"AuditEvents\" WHERE \"ActorUserId\" = {id}", cancellationToken);

        var idText = id.ToString();
        result["AuditEvents.EntityId"] = await CountAsync(
            target,
            $"SELECT COUNT(*) AS \"Value\" FROM \"AuditEvents\" WHERE \"EntityId\" IS NOT NULL AND LOWER(\"EntityId\") = LOWER({idText})",
            cancellationToken);
        result["RefreshTokens"] = await CountAsync(
            target, $"SELECT COUNT(*) AS \"Value\" FROM \"RefreshTokens\" WHERE \"UserId\" = {id}", cancellationToken);

        return result;
    }

    private static async Task<long> CountAsync(
        AppDbContext target,
        FormattableString sql,
        CancellationToken cancellationToken)
    {
        var arguments = sql.GetArguments();
        var statement = sql.Format;
        for (var index = 0; index < arguments.Length; index++)
        {
            statement = statement.Replace($"{{{index}}}", $"@p{index}", StringComparison.Ordinal);
        }

        return await target.Database
            .SqlQueryRaw<long>(statement, arguments.Select(argument => argument!).ToArray())
            .SingleAsync(cancellationToken);
    }
}
