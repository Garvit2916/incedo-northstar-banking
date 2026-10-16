using Northstar.Domain.Common;

namespace Northstar.Domain.Entities;

/// <summary>
/// Operational record of an AI assistance request. By design this stores only a
/// hash of the input and the validated output - never the raw prompt or the full
/// ticket text - to minimise retention of potentially sensitive content while
/// still supporting the agent review trail required by the assessment.
/// </summary>
public class AiAssistanceRecord : AuditableEntity
{
    public Guid TicketId { get; set; }

    public AiAssistKind Kind { get; set; }

    /// <summary>Provider identifier, e.g. "azure-openai" or "fallback-deterministic".</summary>
    public string Provider { get; set; } = string.Empty;

    public string? Model { get; set; }

    /// <summary>False when the deterministic development fallback produced the output.</summary>
    public bool IsLiveInference { get; set; }

    /// <summary>SHA-256 of the canonicalised input, for correlation without retention.</summary>
    public string InputHash { get; set; } = string.Empty;

    /// <summary>Validated JSON output. Draft content requiring human review.</summary>
    public string OutputJson { get; set; } = string.Empty;

    public double? Confidence { get; set; }

    public long LatencyMs { get; set; }

    public Guid CreatedByUserId { get; set; }
}
