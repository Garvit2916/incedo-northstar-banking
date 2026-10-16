using System.Text.Json;
using Northstar.Application.Abstractions;
using Northstar.Domain.Entities;

namespace Northstar.Infrastructure.Auditing;

/// <summary>
/// Persists append-only audit events. Metadata is serialised from small,
/// non-sensitive objects; callers must never pass secrets, tokens, document
/// contents, or full personal records.
/// </summary>
public sealed class AuditWriter : IAuditWriter
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public AuditWriter(IApplicationDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task RecordAsync(
        string action,
        string? entityType = null,
        string? entityId = null,
        object? metadata = null,
        bool succeeded = true,
        CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var role = _currentUser.IsInRole(Domain.Roles.Administrator) ? Domain.Roles.Administrator
            : _currentUser.IsInRole(Domain.Roles.Reviewer) ? Domain.Roles.Reviewer
            : _currentUser.IsInRole(Domain.Roles.SupportAgent) ? Domain.Roles.SupportAgent
            : _currentUser.IsInRole(Domain.Roles.Customer) ? Domain.Roles.Customer
            : null;

        _db.AuditEvents.Add(new AuditEvent
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            ActorUserId = _currentUser.UserId,
            ActorRole = role,
            IpAddress = _currentUser.IpAddress,
            UserAgent = Truncate(_currentUser.UserAgent, 400),
            CorrelationId = _currentUser.CorrelationId,
            MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata),
            Succeeded = succeeded,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        await _db.SaveChangesAsync(ct);
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
