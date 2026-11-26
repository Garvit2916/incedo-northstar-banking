using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Northstar.DataMigration;

public static class MigrationTables
{
    public const string Users = "AspNetUsers";
    public const string UserRoles = "AspNetUserRoles";
    public const string Products = "Products";
    public const string CustomerProfiles = "CustomerProfiles";
    public const string Applications = "Applications";
    public const string ApplicationStatusHistory = "ApplicationStatusHistory";
    public const string KycDocuments = "KycDocuments";
    public const string Tickets = "Tickets";
    public const string TicketAssignments = "TicketAssignments";
    public const string TicketMessages = "TicketMessages";
    public const string AiAssistanceRecords = "AiAssistanceRecords";
    public const string Notifications = "Notifications";
    public const string AuditEvents = "AuditEvents";
    public const string RefreshTokens = "RefreshTokens";

    public static readonly string[] Migrated =
    {
        Users,
        UserRoles,
        Products,
        CustomerProfiles,
        Applications,
        ApplicationStatusHistory,
        KycDocuments,
        Tickets,
        TicketAssignments,
        TicketMessages,
        AiAssistanceRecords,
        Notifications,
        AuditEvents
    };

    public static readonly string[] All =
    {
        Users,
        UserRoles,
        Products,
        CustomerProfiles,
        Applications,
        ApplicationStatusHistory,
        KycDocuments,
        Tickets,
        TicketAssignments,
        TicketMessages,
        AiAssistanceRecords,
        Notifications,
        AuditEvents,
        RefreshTokens
    };
}

public sealed class UserRoleMapping
{
    public Guid UserId { get; init; }
    public string RoleName { get; init; } = string.Empty;
}

public sealed class MigrationManifest
{
    public string SourceDatabase { get; init; } = string.Empty;
    public Dictionary<string, int> Counts { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> ContentHashes { get; init; } = new(StringComparer.Ordinal);
    public List<string> UserIds { get; init; } = new();
    public List<UserRoleMapping> UserRoleMappings { get; init; } = new();
    public List<string> ProductIds { get; init; } = new();
    public List<string> CustomerProfileIds { get; init; } = new();
    public List<string> ApplicationIds { get; init; } = new();
    public List<string> ApplicationStatusHistoryIds { get; init; } = new();
    public List<string> KycDocumentIds { get; init; } = new();
    public List<string> TicketIds { get; init; } = new();
    public List<string> TicketAssignmentIds { get; init; } = new();
    public List<string> TicketMessageIds { get; init; } = new();
    public List<string> AiAssistanceRecordIds { get; init; } = new();
    public List<string> NotificationIds { get; init; } = new();
    public List<string> AuditEventIds { get; init; } = new();
    public List<string> ExcludedSoftUserReferenceIds { get; init; } = new();
    public int SourceRefreshTokenCount { get; init; }
    public string Hash { get; set; } = string.Empty;

    public static MigrationManifest Finalize(MigrationManifest manifest)
    {
        foreach (var list in manifest.AllIdLists())
        {
            list.Sort(StringComparer.OrdinalIgnoreCase);
        }

        manifest.UserRoleMappings.Sort((left, right) =>
        {
            var byUser = string.CompareOrdinal(left.UserId.ToString(), right.UserId.ToString());
            return byUser != 0 ? byUser : StringComparer.Ordinal.Compare(left.RoleName, right.RoleName);
        });

        manifest.Hash = ComputeHash(manifest);
        return manifest;
    }

    public IReadOnlyDictionary<string, HashSet<Guid>> IdSetsByTable()
    {
        var result = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal)
        {
            [MigrationTables.Users] = ToGuidSet(UserIds),
            [MigrationTables.Products] = ToGuidSet(ProductIds),
            [MigrationTables.CustomerProfiles] = ToGuidSet(CustomerProfileIds),
            [MigrationTables.Applications] = ToGuidSet(ApplicationIds),
            [MigrationTables.ApplicationStatusHistory] = ToGuidSet(ApplicationStatusHistoryIds),
            [MigrationTables.KycDocuments] = ToGuidSet(KycDocumentIds),
            [MigrationTables.Tickets] = ToGuidSet(TicketIds),
            [MigrationTables.TicketAssignments] = ToGuidSet(TicketAssignmentIds),
            [MigrationTables.TicketMessages] = ToGuidSet(TicketMessageIds),
            [MigrationTables.AiAssistanceRecords] = ToGuidSet(AiAssistanceRecordIds),
            [MigrationTables.Notifications] = ToGuidSet(NotificationIds),
            [MigrationTables.AuditEvents] = ToGuidSet(AuditEventIds),
            [MigrationTables.RefreshTokens] = new HashSet<Guid>()
        };

        return result;
    }

    private static string ComputeHash(MigrationManifest manifest)
    {
        var builder = new StringBuilder();
        builder.Append(manifest.SourceDatabase).Append('\n');
        builder.Append(manifest.SourceRefreshTokenCount).Append('\n');

        foreach (var table in MigrationTables.Migrated)
        {
            builder.Append(table).Append('=').Append(manifest.Counts.GetValueOrDefault(table, 0)).Append('\n');
        }

        foreach (var (table, hash) in manifest.ContentHashes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append("content.").Append(table).Append('=').Append(hash).Append('\n');
        }

        foreach (var list in manifest.AllIdLists())
        {
            foreach (var id in list.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append(id).Append('\n');
            }
        }

        foreach (var mapping in manifest.UserRoleMappings
                     .OrderBy(x => x.UserId)
                     .ThenBy(x => x.RoleName, StringComparer.Ordinal))
        {
            builder.Append(mapping.UserId).Append(':').Append(mapping.RoleName).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private IEnumerable<List<string>> AllIdLists()
    {
        yield return UserIds;
        yield return ProductIds;
        yield return CustomerProfileIds;
        yield return ApplicationIds;
        yield return ApplicationStatusHistoryIds;
        yield return KycDocumentIds;
        yield return TicketIds;
        yield return TicketAssignmentIds;
        yield return TicketMessageIds;
        yield return AiAssistanceRecordIds;
        yield return NotificationIds;
        yield return AuditEventIds;
        yield return ExcludedSoftUserReferenceIds;
    }

    private static HashSet<Guid> ToGuidSet(IEnumerable<string> ids) =>
        ids.Select(Guid.Parse).ToHashSet();
}

public sealed class ConflictPlan
{
    public Guid UserId { get; init; }
    public string Action { get; init; } = "none";
    public string? OriginalEmail { get; init; }
    public string? OriginalUserName { get; init; }
    public string QuarantinedEmail { get; init; } = string.Empty;
    public string QuarantinedUserName { get; init; } = string.Empty;
    public Dictionary<string, long> RelatedRecordCounts { get; init; } = new(StringComparer.Ordinal);
}

public sealed class PreflightSummary
{
    public bool AppliedMigrationFound { get; init; }
    public bool ConflictAdminIdentityCollision { get; init; }
    public Dictionary<string, Guid> TargetRoleIds { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<string>> IdCollisions { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> UniqueKeyCollisions { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> TargetCountsBefore { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> UnrelatedFingerprintsBefore { get; init; } = new(StringComparer.Ordinal);
    public ConflictPlan ConflictPlan { get; init; } = new();
    public List<string> Blockers { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}

public sealed class BlobMismatch
{
    public string StorageKey { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Expected { get; init; } = string.Empty;
    public string Actual { get; init; } = string.Empty;
}

public sealed class BlobVerificationSummary
{
    public bool Enabled { get; init; }
    public string Bucket { get; init; } = string.Empty;
    public int CheckedCount { get; init; }
    public int ChecksumVerifiedCount { get; set; }
    public int MissingCount { get; set; }
    public int MismatchCount { get; set; }
    public List<string> MissingKeys { get; init; } = new();
    public List<BlobMismatch> Mismatches { get; init; } = new();
    public List<string> Blockers { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    public string Warning { get; init; } = string.Empty;
}

public sealed class ApplySummary
{
    public bool Applied { get; init; }
    public Guid MigrationAuditEventId { get; init; }
    public DateTimeOffset CompletedAtUtc { get; init; }
}

public sealed class MigrationReport
{
    public string Status { get; set; } = "created";
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public string Mode { get; set; } = string.Empty;
    public MigrationManifest Manifest { get; set; } = new();
    public PreflightSummary Preflight { get; set; } = new();
    public BlobVerificationSummary Blobs { get; set; } = new();
    public ApplySummary Apply { get; set; } = new();
    public Dictionary<string, string> Verification { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> RollbackVerification { get; set; } = new(StringComparer.Ordinal);
    public List<string> Errors { get; set; } = new();
}

public static class ReportSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(MigrationReport report) => JsonSerializer.Serialize(report, Options);

    public static MigrationReport Deserialize(string json) =>
        JsonSerializer.Deserialize<MigrationReport>(json, Options)
        ?? throw new InvalidOperationException("Migration report JSON was empty.");
}
