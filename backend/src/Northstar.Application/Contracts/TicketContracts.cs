using Northstar.Domain;

namespace Northstar.Application.Contracts;

public sealed record TicketMessageResponse(
    Guid Id,
    Guid? AuthorUserId,
    MessageAuthorType AuthorType,
    string Body,
    bool IsInternalNote,
    DateTimeOffset CreatedAtUtc);

public sealed record TicketSummaryResponse(
    Guid Id,
    string Reference,
    string Subject,
    TicketCategory Category,
    TicketPriority Priority,
    TicketStatus Status,
    Guid? CustomerUserId,
    Guid? AssignedAgentId,
    Guid? RelatedApplicationId,
    DateTimeOffset? SlaDueAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int MessageCount);

public sealed record TicketDetailResponse(
    Guid Id,
    string Reference,
    string Subject,
    string Description,
    TicketCategory Category,
    TicketPriority Priority,
    TicketStatus Status,
    Guid? CustomerUserId,
    Guid? AssignedAgentId,
    Guid? RelatedApplicationId,
    DateTimeOffset? SlaDueAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<TicketMessageResponse> Messages);

public sealed record CreateTicketRequest(
    string Subject,
    string Description,
    TicketCategory Category,
    TicketPriority Priority,
    Guid? CustomerUserId,
    Guid? RelatedApplicationId);

public sealed record AddTicketMessageRequest(string Body, bool IsInternalNote);

public sealed record ChangeTicketStatusRequest(TicketStatus Status);

public sealed record AssignTicketRequest(Guid AgentUserId);

public sealed record TicketQuery(
    TicketStatus? Status = null,
    TicketCategory? Category = null,
    TicketPriority? Priority = null,
    Guid? AssignedAgentId = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20);
