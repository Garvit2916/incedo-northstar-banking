namespace Northstar.Application.Contracts;

public sealed record AssignRolesRequest(IReadOnlyList<string> Roles);

public sealed record SetUserActiveRequest(bool IsActive);

public sealed record AuditEventResponse(
    Guid Id,
    Guid? ActorUserId,
    string? ActorRole,
    string Action,
    string? EntityType,
    string? EntityId,
    string? IpAddress,
    string? CorrelationId,
    bool Succeeded,
    DateTimeOffset CreatedAtUtc);

public sealed record DashboardStatsResponse(
    int ApplicationsDraft,
    int ApplicationsSubmitted,
    int ApplicationsUnderReview,
    int ApplicationsApproved,
    int ApplicationsRejected,
    int OpenTickets,
    int UnreadNotifications);
