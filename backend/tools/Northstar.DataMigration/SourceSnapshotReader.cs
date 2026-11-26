using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Northstar.Domain.Entities;
using Northstar.Infrastructure.Identity;
using Northstar.Infrastructure.Persistence;

namespace Northstar.DataMigration;

public sealed class SourceSnapshot
{
    public required List<ApplicationUser> Users { get; init; }
    public required List<UserRoleMapping> UserRoles { get; init; }
    public required List<Product> Products { get; init; }
    public required List<CustomerProfile> CustomerProfiles { get; init; }
    public required List<Domain.Entities.Application> Applications { get; init; }
    public required List<ApplicationStatusHistory> ApplicationStatusHistory { get; init; }
    public required List<KycDocument> KycDocuments { get; init; }
    public required List<Ticket> Tickets { get; init; }
    public required List<TicketAssignment> TicketAssignments { get; init; }
    public required List<TicketMessage> TicketMessages { get; init; }
    public required List<AiAssistanceRecord> AiAssistanceRecords { get; init; }
    public required List<Notification> Notifications { get; init; }
    public required List<AuditEvent> AuditEvents { get; init; }
    public required MigrationManifest Manifest { get; init; }
}

public static class SourceSnapshotReader
{
    public static async Task<SourceSnapshot> ReadAsync(
        AppDbContext source,
        bool includeOptionalLinkedAgentUsers,
        CancellationToken cancellationToken = default)
    {
        await ValidateSourceIntegrityAsync(source, cancellationToken);

        var users = await source.Users.AsNoTracking().ToListAsync(cancellationToken);
        var roles = await source.Roles.AsNoTracking().ToListAsync(cancellationToken);
        var userRoles = await source.UserRoles.AsNoTracking().ToListAsync(cancellationToken);
        var products = await source.Products.AsNoTracking().ToListAsync(cancellationToken);
        var profiles = await source.CustomerProfiles.AsNoTracking().ToListAsync(cancellationToken);
        var applications = await source.Applications.AsNoTracking().ToListAsync(cancellationToken);
        var statuses = await source.ApplicationStatusHistory.AsNoTracking().ToListAsync(cancellationToken);
        var kycDocuments = await source.KycDocuments.AsNoTracking().ToListAsync(cancellationToken);
        var tickets = await source.Tickets.AsNoTracking().ToListAsync(cancellationToken);
        var assignments = await source.TicketAssignments.AsNoTracking().ToListAsync(cancellationToken);
        var messages = await source.TicketMessages.AsNoTracking().ToListAsync(cancellationToken);
        var aiRecords = await source.AiAssistanceRecords.AsNoTracking().ToListAsync(cancellationToken);
        var notifications = await source.Notifications.AsNoTracking().ToListAsync(cancellationToken);
        var auditEvents = await source.AuditEvents.AsNoTracking().ToListAsync(cancellationToken);
        var refreshTokenCount = await source.RefreshTokens.CountAsync(cancellationToken);

        ValidateSourceUniqueness(users, applications, tickets);
        ValidateSeedUsers(users, includeOptionalLinkedAgentUsers);

        var roleNameById = new Dictionary<Guid, string>();
        foreach (var role in roles)
        {
            var canonical = MigrationScope.CanonicalizeRoleName(role.NormalizedName ?? role.Name);
            if (canonical is not null)
            {
                roleNameById[role.Id] = canonical;
            }
        }

        ValidateExpectedRoles(users, userRoles, roleNameById, includeOptionalLinkedAgentUsers);

        var allowedUsers = MigrationScope.GetAllowedUserIds(includeOptionalLinkedAgentUsers);
        var selectedUsers = new HashSet<Guid>(MigrationScope.ApprovedUserIds);
        if (includeOptionalLinkedAgentUsers)
        {
            selectedUsers.UnionWith(MigrationScope.OptionalLinkedAgentUserIds);
        }

        var selectedProfiles = new HashSet<Guid>();
        var selectedApplications = new HashSet<Guid>();
        var selectedProducts = new HashSet<Guid>();
        var selectedStatuses = new HashSet<Guid>();
        var selectedKyc = new HashSet<Guid>();
        var selectedTickets = new HashSet<Guid>();
        var selectedAssignments = new HashSet<Guid>();
        var selectedMessages = new HashSet<Guid>();
        var selectedAiRecords = new HashSet<Guid>();
        var selectedNotifications = new HashSet<Guid>();
        var selectedAudits = new HashSet<Guid>();

        while (true)
        {
            var before = Signature(
                selectedUsers, selectedProfiles, selectedApplications, selectedProducts,
                selectedStatuses, selectedKyc, selectedTickets, selectedAssignments,
                selectedMessages, selectedAiRecords, selectedNotifications, selectedAudits);

            var profileIds = profilesSource(profiles, selectedUsers, selectedProfiles);
            var apps = applicationsSource(applications, profileIds, selectedUsers, selectedApplications);
            profileIds.UnionWith(applications
                .Where(a => apps.Contains(a.Id))
                .Select(a => a.CustomerProfileId));
            selectedUsers.UnionWith(profilesSourceRows(profileIds, profiles).Select(p => p.UserId));

            var productIds = applications
                .Where(a => apps.Contains(a.Id))
                .Select(a => a.ProductId)
                .ToHashSet();

            var statusIds = statuses
                .Where(s => apps.Contains(s.ApplicationId))
                .Select(s => s.Id)
                .ToHashSet();

            var kycIds = kycDocuments
                .Where(k => apps.Contains(k.ApplicationId) || profileIds.Contains(k.CustomerProfileId))
                .Select(k => k.Id)
                .ToHashSet();

            var ticketIds = tickets
                .Where(t =>
                    (t.CustomerUserId.HasValue && selectedUsers.Contains(t.CustomerUserId.Value)) ||
                    (t.AssignedAgentId.HasValue && selectedUsers.Contains(t.AssignedAgentId.Value)) ||
                    (t.RelatedApplicationId.HasValue && apps.Contains(t.RelatedApplicationId.Value)))
                .Select(t => t.Id)
                .ToHashSet();

            var assignmentIds = assignments
                .Where(a => ticketIds.Contains(a.TicketId))
                .Select(a => a.Id)
                .ToHashSet();

            var messageIds = messages
                .Where(m => ticketIds.Contains(m.TicketId))
                .Select(m => m.Id)
                .ToHashSet();

            var aiIds = aiRecords
                .Where(a => ticketIds.Contains(a.TicketId))
                .Select(a => a.Id)
                .ToHashSet();

            var notificationIds = notifications
                .Where(n =>
                    selectedUsers.Contains(n.UserId) ||
                    (n.ApplicationId.HasValue && apps.Contains(n.ApplicationId.Value)))
                .Select(n => n.Id)
                .ToHashSet();

            selectedUsers.UnionWith(notifications
                .Where(n => notificationIds.Contains(n.Id))
                .Select(n => n.UserId));

            var entityIds = BuildEntityIdSet(
                selectedUsers, profileIds, apps, productIds, statusIds, kycIds, ticketIds,
                assignmentIds, messageIds, aiIds, notificationIds);

            var auditIds = auditEvents
                .Where(a =>
                    (a.ActorUserId.HasValue && selectedUsers.Contains(a.ActorUserId.Value)) ||
                    (a.EntityId is not null && entityIds.Contains(a.EntityId)))
                .Select(a => a.Id)
                .ToHashSet();

            selectedProfiles = profileIds;
            selectedApplications = apps;
            selectedProducts = productIds;
            selectedStatuses = statusIds;
            selectedKyc = kycIds;
            selectedTickets = ticketIds;
            selectedAssignments = assignmentIds;
            selectedMessages = messageIds;
            selectedAiRecords = aiIds;
            selectedNotifications = notificationIds;
            selectedAudits = auditIds;

            if (Signature(
                    selectedUsers, selectedProfiles, selectedApplications, selectedProducts,
                    selectedStatuses, selectedKyc, selectedTickets, selectedAssignments,
                    selectedMessages, selectedAiRecords, selectedNotifications, selectedAudits) == before)
            {
                break;
            }
        }

        var disallowedUsers = selectedUsers.Except(allowedUsers).ToList();
        if (disallowedUsers.Count > 0)
        {
            throw new InvalidOperationException(
                "Migration closure requires unapproved user IDs: " +
                string.Join(", ", disallowedUsers.Select(id => id.ToString().ToUpperInvariant())));
        }

        var roleMappings = userRoles
            .Where(r => selectedUsers.Contains(r.UserId))
            .Select(r =>
            {
                if (!roleNameById.TryGetValue(r.RoleId, out var roleName))
                {
                    throw new InvalidOperationException(
                        $"Source user {r.UserId} has an unrecognised role ID {r.RoleId}.");
                }

                return new UserRoleMapping { UserId = r.UserId, RoleName = roleName };
            })
            .ToList();

        var excludedSoftReferences = BuildUnresolvedSoftReferences(
            applications.Where(a => selectedApplications.Contains(a.Id)),
            tickets.Where(t => selectedTickets.Contains(t.Id)),
            assignments.Where(a => selectedAssignments.Contains(a.Id)),
            messages.Where(m => selectedMessages.Contains(m.Id)),
            aiRecords.Where(a => selectedAiRecords.Contains(a.Id)),
            statuses.Where(s => selectedStatuses.Contains(s.Id)),
            kycDocuments.Where(k => selectedKyc.Contains(k.Id)),
            auditEvents.Where(a => selectedAudits.Contains(a.Id)),
            selectedUsers);

        var selectedUserRows = users.Where(u => selectedUsers.Contains(u.Id)).ToList();
        var selectedProductRows = products.Where(p => selectedProducts.Contains(p.Id)).ToList();
        var selectedProfileRows = profiles.Where(p => selectedProfiles.Contains(p.Id)).ToList();
        var selectedApplicationRows = applications.Where(a => selectedApplications.Contains(a.Id)).ToList();
        var selectedStatusRows = statuses.Where(s => selectedStatuses.Contains(s.Id)).ToList();
        var selectedKycRows = kycDocuments.Where(k => selectedKyc.Contains(k.Id)).ToList();
        var selectedTicketRows = tickets.Where(t => selectedTickets.Contains(t.Id)).ToList();
        var selectedAssignmentRows = assignments.Where(a => selectedAssignments.Contains(a.Id)).ToList();
        var selectedMessageRows = messages.Where(m => selectedMessages.Contains(m.Id)).ToList();
        var selectedAiRows = aiRecords.Where(a => selectedAiRecords.Contains(a.Id)).ToList();
        var selectedNotificationRows = notifications.Where(n => selectedNotifications.Contains(n.Id)).ToList();
        var selectedAuditRows = auditEvents.Where(a => selectedAudits.Contains(a.Id)).ToList();

        var contentHashes = ContentFingerprint.ComputeSource(source, new SourceSnapshot
        {
            Users = selectedUserRows,
            UserRoles = roleMappings,
            Products = selectedProductRows,
            CustomerProfiles = selectedProfileRows,
            Applications = selectedApplicationRows,
            ApplicationStatusHistory = selectedStatusRows,
            KycDocuments = selectedKycRows,
            Tickets = selectedTicketRows,
            TicketAssignments = selectedAssignmentRows,
            TicketMessages = selectedMessageRows,
            AiAssistanceRecords = selectedAiRows,
            Notifications = selectedNotificationRows,
            AuditEvents = selectedAuditRows,
            Manifest = new MigrationManifest()
        });

        var manifest = MigrationManifest.Finalize(new MigrationManifest
        {
            SourceDatabase = "sqlite",
            ContentHashes = contentHashes,
            SourceRefreshTokenCount = refreshTokenCount,
            UserIds = selectedUsers.OrderBy(id => id).Select(id => id.ToString().ToUpperInvariant()).ToList(),
            UserRoleMappings = roleMappings,
            ProductIds = selectedProducts.OrderBy(id => id).Select(id => id.ToString().ToUpperInvariant()).ToList(),
            CustomerProfileIds = selectedProfiles.OrderBy(id => id).Select(id => id.ToString().ToUpperInvariant()).ToList(),
            ApplicationIds = selectedApplications.OrderBy(id => id).Select(id => id.ToString().ToUpperInvariant()).ToList(),
            ApplicationStatusHistoryIds = selectedStatuses.OrderBy(id => id).Select(id => id.ToString().ToUpperInvariant()).ToList(),
            KycDocumentIds = selectedKyc.OrderBy(id => id).Select(id => id.ToString().ToUpperInvariant()).ToList(),
            TicketIds = selectedTickets.OrderBy(id => id).Select(id => id.ToString().ToUpperInvariant()).ToList(),
            TicketAssignmentIds = selectedAssignments.OrderBy(id => id).Select(id => id.ToString().ToUpperInvariant()).ToList(),
            TicketMessageIds = selectedMessages.OrderBy(id => id).Select(id => id.ToString().ToUpperInvariant()).ToList(),
            AiAssistanceRecordIds = selectedAiRecords.OrderBy(id => id).Select(id => id.ToString().ToUpperInvariant()).ToList(),
            NotificationIds = selectedNotifications.OrderBy(id => id).Select(id => id.ToString().ToUpperInvariant()).ToList(),
            AuditEventIds = selectedAudits.OrderBy(id => id).Select(id => id.ToString().ToUpperInvariant()).ToList(),
            ExcludedSoftUserReferenceIds = excludedSoftReferences
                .OrderBy(id => id)
                .Select(id => id.ToString().ToUpperInvariant())
                .ToList(),
            Counts = BuildCounts(
                selectedUsers.Count,
                roleMappings.Count,
                selectedProducts.Count,
                selectedProfiles.Count,
                selectedApplications.Count,
                selectedStatuses.Count,
                selectedKyc.Count,
                selectedTickets.Count,
                selectedAssignments.Count,
                selectedMessages.Count,
                selectedAiRecords.Count,
                selectedNotifications.Count,
                selectedAudits.Count)
        });

        return new SourceSnapshot
        {
            Users = selectedUserRows,
            UserRoles = roleMappings,
            Products = selectedProductRows,
            CustomerProfiles = selectedProfileRows,
            Applications = selectedApplicationRows,
            ApplicationStatusHistory = selectedStatusRows,
            KycDocuments = selectedKycRows,
            Tickets = selectedTicketRows,
            TicketAssignments = selectedAssignmentRows,
            TicketMessages = selectedMessageRows,
            AiAssistanceRecords = selectedAiRows,
            Notifications = selectedNotificationRows,
            AuditEvents = selectedAuditRows,
            Manifest = manifest
        };
    }

    private static async Task ValidateSourceIntegrityAsync(AppDbContext source, CancellationToken ct)
    {
        var checks = await source.Database
            .SqlQueryRaw<string>("SELECT \"quick_check\" AS \"Value\" FROM pragma_quick_check")
            .ToListAsync(ct);

        if (checks.Any(check => !string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("SQLite quick_check failed.");
        }

        var foreignKeyViolations = await source.Database
            .SqlQueryRaw<long>("SELECT COUNT(*) AS \"Value\" FROM pragma_foreign_key_check")
            .SingleAsync(ct);

        if (foreignKeyViolations != 0)
        {
            throw new InvalidOperationException(
                $"SQLite foreign_key_check reported {foreignKeyViolations} violation(s).");
        }
    }

    private static void ValidateSourceUniqueness(
        List<ApplicationUser> users,
        List<Domain.Entities.Application> applications,
        List<Ticket> tickets)
    {
        if (users.Where(u => u.NormalizedUserName is not null)
            .GroupBy(u => u.NormalizedUserName, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("Source contains duplicate normalized user names.");
        }

        if (applications.GroupBy(a => a.Reference, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("Source contains duplicate application references.");
        }

        if (tickets.GroupBy(t => t.Reference, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("Source contains duplicate ticket references.");
        }
    }

    private static void ValidateSeedUsers(
        List<ApplicationUser> users,
        bool includeOptionalLinkedAgentUsers)
    {
        var required = new HashSet<Guid>(MigrationScope.ApprovedUserIds);
        if (includeOptionalLinkedAgentUsers)
        {
            required.UnionWith(MigrationScope.OptionalLinkedAgentUserIds);
        }

        var missing = required.Except(users.Select(u => u.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Required source users are missing: " +
                string.Join(", ", missing.Select(id => id.ToString().ToUpperInvariant())));
        }
    }

    private static void ValidateExpectedRoles(
        List<ApplicationUser> users,
        List<IdentityUserRole<Guid>> userRoles,
        Dictionary<Guid, string> roleNameById,
        bool includeOptionalLinkedAgentUsers)
    {
        var expected = new Dictionary<Guid, IReadOnlySet<string>>(MigrationScope.ExpectedRolesByUserId);
        if (includeOptionalLinkedAgentUsers)
        {
            foreach (var id in MigrationScope.OptionalLinkedAgentUserIds)
            {
                expected[id] = new HashSet<string> { "Customer", "SupportAgent" };
            }
        }

        foreach (var (userId, expectedRoles) in expected)
        {
            if (!users.Any(u => u.Id == userId))
            {
                continue;
            }

            var actualRoles = userRoles
                .Where(r => r.UserId == userId && roleNameById.TryGetValue(r.RoleId, out var name) && name is not null)
                .Select(r => roleNameById[r.RoleId])
                .ToHashSet(StringComparer.Ordinal);

            if (!expectedRoles.IsSubsetOf(actualRoles))
            {
                throw new InvalidOperationException(
                    $"Source user {userId.ToString().ToUpperInvariant()} does not have the expected role(s).");
            }
        }
    }

    private static HashSet<Guid> profilesSource(
        List<CustomerProfile> profiles,
        HashSet<Guid> selectedUsers,
        HashSet<Guid> currentProfiles)
    {
        var result = new HashSet<Guid>(currentProfiles);
        result.UnionWith(profiles.Where(p => selectedUsers.Contains(p.UserId)).Select(p => p.Id));
        return result;
    }

    private static List<CustomerProfile> profilesSourceRows(HashSet<Guid> profileIds, List<CustomerProfile> profiles) =>
        profiles.Where(p => profileIds.Contains(p.Id)).ToList();

    private static HashSet<Guid> applicationsSource(
        List<Domain.Entities.Application> applications,
        HashSet<Guid> profiles,
        HashSet<Guid> selectedUsers,
        HashSet<Guid> currentApplications)
    {
        var result = new HashSet<Guid>(currentApplications);
        result.UnionWith(applications
            .Where(a =>
                profiles.Contains(a.CustomerProfileId) ||
                (a.AssignedReviewerId.HasValue && selectedUsers.Contains(a.AssignedReviewerId.Value)))
            .Select(a => a.Id));
        return result;
    }

    private static HashSet<string> BuildEntityIdSet(params HashSet<Guid>[] idSets)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var idSet in idSets)
        {
            foreach (var id in idSet)
            {
                result.Add(id.ToString());
                result.Add(id.ToString().ToUpperInvariant());
                result.Add(id.ToString().ToLowerInvariant());
            }
        }

        return result;
    }

    private static HashSet<Guid> BuildUnresolvedSoftReferences(
        IEnumerable<Domain.Entities.Application> applications,
        IEnumerable<Ticket> tickets,
        IEnumerable<TicketAssignment> assignments,
        IEnumerable<TicketMessage> messages,
        IEnumerable<AiAssistanceRecord> aiRecords,
        IEnumerable<ApplicationStatusHistory> statuses,
        IEnumerable<KycDocument> kycDocuments,
        IEnumerable<AuditEvent> auditEvents,
        HashSet<Guid> selectedUsers)
    {
        var references = new HashSet<Guid>();

        void Add(Guid? id)
        {
            if (id.HasValue && !selectedUsers.Contains(id.Value))
            {
                references.Add(id.Value);
            }
        }

        foreach (var application in applications)
        {
            Add(application.AssignedReviewerId);
        }

        foreach (var ticket in tickets)
        {
            Add(ticket.CustomerUserId);
            Add(ticket.AssignedAgentId);
        }

        foreach (var assignment in assignments)
        {
            Add(assignment.AgentUserId);
            Add(assignment.AssignedByUserId);
        }

        foreach (var message in messages)
        {
            Add(message.AuthorUserId);
        }

        foreach (var record in aiRecords)
        {
            Add(record.CreatedByUserId);
        }

        foreach (var status in statuses)
        {
            Add(status.ChangedByUserId);
        }

        foreach (var document in kycDocuments)
        {
            Add(document.ReviewedByUserId);
        }

        foreach (var audit in auditEvents)
        {
            Add(audit.ActorUserId);
        }

        references.ExceptWith(MigrationScope.ConflictAdminUserId == Guid.Empty
            ? Array.Empty<Guid>()
            : new[] { MigrationScope.ConflictAdminUserId });

        return references;
    }

    private static Dictionary<string, int> BuildCounts(
        int users,
        int userRoles,
        int products,
        int profiles,
        int applications,
        int statuses,
        int kyc,
        int tickets,
        int assignments,
        int messages,
        int aiRecords,
        int notifications,
        int audits) => new(StringComparer.Ordinal)
    {
        [MigrationTables.Users] = users,
        [MigrationTables.UserRoles] = userRoles,
        [MigrationTables.Products] = products,
        [MigrationTables.CustomerProfiles] = profiles,
        [MigrationTables.Applications] = applications,
        [MigrationTables.ApplicationStatusHistory] = statuses,
        [MigrationTables.KycDocuments] = kyc,
        [MigrationTables.Tickets] = tickets,
        [MigrationTables.TicketAssignments] = assignments,
        [MigrationTables.TicketMessages] = messages,
        [MigrationTables.AiAssistanceRecords] = aiRecords,
        [MigrationTables.Notifications] = notifications,
        [MigrationTables.AuditEvents] = audits,
        [MigrationTables.RefreshTokens] = 0
    };

    private static string Signature(params HashSet<Guid>[] sets) =>
        string.Join("|", sets.Select(set => set.Count));
}
