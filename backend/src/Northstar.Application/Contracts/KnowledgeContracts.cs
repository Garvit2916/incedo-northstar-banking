namespace Northstar.Application.Contracts;

/// <summary>Full article view for the support knowledge-base workspace.</summary>
public sealed record KnowledgeArticleResponse(
    Guid Id,
    string ArticleCode,
    string Title,
    string Category,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Keywords,
    string Content,
    string ApplicableProduct,
    string IssueType,
    string Priority,
    IReadOnlyList<string> ResolutionSteps,
    string CustomerExplanation,
    string AgentGuidance,
    string EscalationCriteria,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Compact reference attached to AI results and next-best actions.</summary>
public sealed record KnowledgeReferenceResponse(
    Guid Id,
    string ArticleCode,
    string Title,
    string Category,
    double Score,
    string MatchReason);

public sealed record KnowledgeQuery(
    string? Search = null,
    string? Category = null,
    string? Product = null,
    int Page = 1,
    int PageSize = 20);

/// <summary>Assistive ticket classification shown with grounded AI results.</summary>
public sealed record IssueClassificationResponse(
    string SuggestedCategory,
    string IssueType,
    double Confidence,
    IReadOnlyList<string> Signals,
    string Explanation);
