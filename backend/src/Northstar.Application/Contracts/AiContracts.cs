using Northstar.Domain;

namespace Northstar.Application.Contracts;

/// <summary>
/// Provenance for an AI-generated result. Exposed to the UI so generated content
/// is always labelled with its source and flagged when it is a development
/// fallback rather than live model inference.
/// </summary>
public sealed record AiProvenanceResponse(
    string Provider,
    string? Model,
    bool IsLiveInference,
    bool RequiresHumanReview,
    string Disclaimer);

public sealed record AiSummaryResponse(
    string MainIssue,
    IReadOnlyList<string> KeyContext,
    IReadOnlyList<string> ActionsTaken,
    IReadOnlyList<string> OutstandingItems,
    string CurrentState,
    IReadOnlyList<string> Uncertainties,
    AiProvenanceResponse Provenance);

public sealed record AiSuggestedResponseResponse(
    string Subject,
    string Body,
    IReadOnlyList<string> Notes,
    AiProvenanceResponse Provenance);

public sealed record AiSentimentResponse(
    SentimentLabel Label,
    string Explanation,
    double? Confidence,
    bool EscalationRecommended,
    IReadOnlyList<string> Signals,
    AiProvenanceResponse Provenance);

public sealed record NextBestActionResponse(
    string Action,
    string Reason,
    string Evidence,
    string Priority,
    bool RequiresHumanApproval);

public sealed record NextBestActionsResponse(
    IReadOnlyList<NextBestActionResponse> Actions,
    AiProvenanceResponse Provenance);
