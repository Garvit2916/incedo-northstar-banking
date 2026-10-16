using Northstar.Domain.Common;

namespace Northstar.Domain.Entities;

/// <summary>
/// Immutable record of a single application status change, forming the audit
/// trail shown to customers and reviewers.
/// </summary>
public class ApplicationStatusHistory : AuditableEntity
{
    public Guid ApplicationId { get; set; }

    public Application Application { get; set; } = null!;

    public ApplicationStatus? FromStatus { get; set; }

    public ApplicationStatus ToStatus { get; set; }

    /// <summary>Null for system-driven transitions.</summary>
    public Guid? ChangedByUserId { get; set; }

    public string? Reason { get; set; }

    public DateTimeOffset ChangedAtUtc { get; set; }
}
