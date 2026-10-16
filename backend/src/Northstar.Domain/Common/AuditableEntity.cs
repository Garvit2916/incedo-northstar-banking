namespace Northstar.Domain.Common;

/// <summary>
/// Base type for all persisted domain entities. Identifiers are client-generated
/// GUIDs so that aggregates can be constructed without a database round trip.
/// Timestamp and concurrency values are maintained centrally by the persistence
/// layer so entities themselves stay free of infrastructure concerns.
/// </summary>
public abstract class AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>
    /// Optimistic concurrency token. Incremented by the persistence layer on every
    /// update; EF Core adds it to the WHERE clause of UPDATE statements.
    /// </summary>
    public int Version { get; set; }
}
