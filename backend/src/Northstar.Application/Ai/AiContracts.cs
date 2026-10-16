using Northstar.Domain;

namespace Northstar.Application.Ai;

/// <summary>
/// Minimised, server-built view of a ticket handed to an AI provider. Only the
/// fields required to reason about the request are included; identity tokens,
/// credentials, and unrelated customer data are never part of the context.
/// </summary>
public sealed record AiTicketContext(
    string TicketReference,
    string Subject,
    string Category,
    string Priority,
    string Status,
    IReadOnlyList<AiConversationMessage> Conversation,
    AiApplicationContext? Application);

public sealed record AiConversationMessage(string Author, string Body);

/// <summary>
/// Application status context, included only when the acting agent is authorised
/// to see the related application.
/// </summary>
public sealed record AiApplicationContext(
    string Reference,
    string ProductName,
    string Status,
    IReadOnlyList<string> DocumentReviewStates,
    bool HasOutstandingDocuments);

/// <summary>
/// Envelope describing where a result came from. <see cref="IsLiveInference"/>
/// is false whenever the deterministic development fallback produced the output,
/// so the UI never implies live model inference occurred.
/// </summary>
public sealed record AiCallResult<T>(
    T Value,
    string Provider,
    string? Model,
    bool IsLiveInference,
    double? Confidence,
    long LatencyMs);

public sealed record AiTicketSummary(
    string MainIssue,
    IReadOnlyList<string> KeyContext,
    IReadOnlyList<string> ActionsTaken,
    IReadOnlyList<string> OutstandingItems,
    string CurrentState,
    IReadOnlyList<string> Uncertainties);

public sealed record AiSuggestedResponse(
    string Subject,
    string Body,
    bool RequiresHumanReview,
    IReadOnlyList<string> Notes);

public sealed record AiSentiment(
    SentimentLabel Label,
    string Explanation,
    double? Confidence,
    bool EscalationRecommended,
    IReadOnlyList<string> Signals);

/// <summary>
/// Deterministic next-best action. Every recommendation carries the rule that
/// produced it so agents can see exactly why it was suggested.
/// </summary>
public sealed record NextBestAction(
    string Action,
    string Reason,
    string Evidence,
    string Priority,
    bool RequiresHumanApproval);
