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
    AiProvenanceResponse Provenance)
{
    public IReadOnlyList<KnowledgeReferenceResponse> Sources { get; init; } =
        Array.Empty<KnowledgeReferenceResponse>();
}

public sealed record AiSuggestedResponseResponse(
    string Subject,
    string Body,
    IReadOnlyList<string> Notes,
    AiProvenanceResponse Provenance)
{
    public IReadOnlyList<KnowledgeReferenceResponse> Sources { get; init; } =
        Array.Empty<KnowledgeReferenceResponse>();
}

public sealed record AiSentimentResponse(
    SentimentLabel Label,
    string Explanation,
    double? Confidence,
    bool EscalationRecommended,
    IReadOnlyList<string> Signals,
    IReadOnlyList<string> FrustrationIndicators,
    IReadOnlyList<string> UrgencyIndicators,
    AiProvenanceResponse Provenance);

public sealed record NextBestActionResponse(
    string Action,
    string Reason,
    string Evidence,
    string Priority,
    bool RequiresHumanApproval,
    string ExpectedOutcome,
    string ActionType)
{
    public string Title { get; init; } = Action;

    public string Explanation { get; init; } = Reason;

    public IReadOnlyList<KnowledgeReferenceResponse> Sources { get; init; } =
        Array.Empty<KnowledgeReferenceResponse>();

    public string? SuggestedWorkflow { get; init; }

    public double? Confidence { get; init; }
}

public sealed record NextBestActionsResponse(
    IReadOnlyList<NextBestActionResponse> Actions,
    AiProvenanceResponse Provenance)
{
    public IssueClassificationResponse? Classification { get; init; }
}
