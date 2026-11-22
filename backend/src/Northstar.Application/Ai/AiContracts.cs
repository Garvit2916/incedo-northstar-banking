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
    AiApplicationContext? Application,
    string Description = "");

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
    IReadOnlyList<string> Signals,
    IReadOnlyList<string> FrustrationIndicators,
    IReadOnlyList<string> UrgencyIndicators);

/// <summary>Minimised, non-sensitive query built from a support ticket.</summary>
public sealed record KnowledgeRetrievalQuery(
    string TicketCategory,
    string Subject,
    string Description,
    IReadOnlyList<string> ConversationBody,
    string? ApplicableProduct);

/// <summary>A scored knowledge article retrieved for a ticket.</summary>
public sealed record KnowledgeHit(
    Guid ArticleId,
    string ArticleCode,
    string Title,
    string Category,
    string ApplicableProduct,
    string IssueType,
    string Priority,
    IReadOnlyList<string> ResolutionSteps,
    string CustomerExplanation,
    string AgentGuidance,
    string EscalationCriteria,
    double Score,
    string MatchReason);

public static class NextBestActionTypes
{
    public const string Reply = "reply";
    public const string RequestDocuments = "request-documents";
    public const string VerifyIdentity = "verify-identity";
    public const string ReviewApplication = "review-application";
    public const string Escalate = "escalate";
    public const string Resolve = "resolve";
    public const string FollowUp = "follow-up";
    public const string Clarify = "clarify";
}

/// <summary>
/// Deterministic next-best action. Every recommendation carries the rule that
/// produced it so agents can see exactly why it was suggested.
/// </summary>
public sealed record NextBestAction(
    string Action,
    string Reason,
    string Evidence,
    string Priority,
    bool RequiresHumanApproval,
    string ExpectedOutcome,
    string ActionType)
{
    /// <summary>A short label suitable for the support workspace action card.</summary>
    public string Title { get; init; } = Action;

    /// <summary>Agent-facing explanation, distinct from the evidence string.</summary>
    public string Explanation { get; init; } = Reason;

    /// <summary>Approved knowledge articles supporting this recommendation.</summary>
    public IReadOnlyList<KnowledgeHit> Sources { get; init; } = Array.Empty<KnowledgeHit>();

    /// <summary>Safe, human-confirmation-only demo workflow suggested by the action.</summary>
    public string? SuggestedWorkflow { get; init; }

    /// <summary>Retrieval confidence, never an automated decision score.</summary>
    public double? Confidence { get; init; }
}
