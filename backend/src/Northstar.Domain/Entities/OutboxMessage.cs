using Northstar.Domain.Common;

namespace Northstar.Domain.Entities;

/// <summary>
/// Transactional outbox row. Notification side effects (email) are persisted in
/// the same transaction as the business change that produced them, so a failure
/// to deliver a message can never roll back or corrupt a valid decision. A
/// background processor drains this table at-least-once.
/// </summary>
public class OutboxMessage : AuditableEntity
{
    /// <summary>Logical event type, e.g. "application.approved".</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Serialised JSON payload describing the side effect.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Stable key used to guarantee idempotent delivery across retries so the
    /// same notification is never sent twice.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public DateTimeOffset? NextAttemptAtUtc { get; set; }
}
