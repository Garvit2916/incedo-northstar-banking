using Northstar.Domain.Common;

namespace Northstar.Domain.Entities;

/// <summary>
/// Append-only audit record for security-sensitive and business-critical
/// actions. Metadata must never contain secrets, tokens, or full document
/// contents.
/// </summary>
public class AuditEvent : AuditableEntity
{
    public Guid? ActorUserId { get; set; }

    public string? ActorRole { get; set; }

    /// <summary>Machine-readable action name, e.g. "application.approved".</summary>
    public string Action { get; set; } = string.Empty;

    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? CorrelationId { get; set; }

    /// <summary>Small, non-sensitive JSON object with additional context.</summary>
    public string? MetadataJson { get; set; }

    public bool Succeeded { get; set; } = true;
}
