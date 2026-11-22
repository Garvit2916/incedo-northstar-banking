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
    int MessageCount,
    SentimentLabel? LatestSentiment,
    DateTimeOffset? SentimentAnalyzedAtUtc);

public sealed record TicketOverviewResponse(
    int OpenTickets,
    int UnassignedTickets,
    int UrgentTickets,
    int EscalatedTickets,
    int AwaitingCustomer,
    int ResolvedToday,
    int AssignedToMe,
    int NegativeSentiment,
    int UrgentEscalationSentiment,
    int AnalyzedSentiment);

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
    SentimentLabel? LatestSentiment,
    DateTimeOffset? SentimentAnalyzedAtUtc,
    IReadOnlyList<TicketMessageResponse> Messages);

/// <summary>
/// Customer-safe ticket summary. Sentiment is an internal triage signal and is
/// deliberately omitted, as are agent internal notes.
/// </summary>
public sealed record CustomerTicketSummaryResponse(
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

/// <summary>Customer-safe ticket detail with internal notes and sentiment omitted.</summary>
public sealed record CustomerTicketDetailResponse(
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

/// <summary>
/// Customer-submitted ticket. Ownership is derived from the authenticated caller;
/// the request deliberately has no customer id that could be spoofed.
/// </summary>
public sealed record CreateCustomerTicketRequest(
    string Subject,
    string Description,
    TicketCategory Category,
    TicketPriority Priority,
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
