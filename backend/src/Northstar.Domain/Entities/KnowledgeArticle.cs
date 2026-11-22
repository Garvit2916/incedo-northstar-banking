using System.Text.Json;

namespace Northstar.Domain.Entities;

/// <summary>
/// A demos-only support knowledge article. Articles are structured so the
/// deterministic retrieval service can score them by ticket category, product,
/// tags and keywords, and so the support workspace can show agents the exact
/// guidance behind a recommendation.
///
/// Collections are persisted as delimited/JSON strings to keep the EF Core model
/// portable across the SQLite and SQL Server providers used by this project.
/// </summary>
public class KnowledgeArticle : Common.AuditableEntity
{
    public string ArticleCode { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string TagsCsv { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string ApplicableProduct { get; set; } = string.Empty;

    public string IssueType { get; set; } = string.Empty;

    public string Priority { get; set; } = "Normal";

    public string KeywordsCsv { get; set; } = string.Empty;

    public string ResolutionStepsJson { get; set; } = "[]";

    public string CustomerExplanation { get; set; } = string.Empty;

    public string AgentGuidance { get; set; } = string.Empty;

    public string EscalationCriteria { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public IReadOnlyList<string> Tags => SplitList(TagsCsv);

    public IReadOnlyList<string> Keywords => SplitList(KeywordsCsv);

    public IReadOnlyList<string> ResolutionSteps
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ResolutionStepsJson))
            {
                return Array.Empty<string>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<string>>(ResolutionStepsJson) ?? new List<string>();
            }
            catch (JsonException)
            {
                return Array.Empty<string>();
            }
        }
    }

    private static IReadOnlyList<string> SplitList(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
