using Microsoft.Extensions.Logging;
using Northstar.Application.Abstractions;
using Northstar.Application.Ai;

namespace Northstar.Infrastructure.Ai;

/// <summary>
/// Wraps a live provider and falls back to the deterministic provider on any
/// failure (timeout, transport error, or schema violation). This guarantees the
/// support workspace remains usable when the AI provider is unavailable, and the
/// provenance recorded with each result always reflects which path produced it.
/// </summary>
public sealed class ResilientAiProvider : IAiProvider
{
    private readonly IAiProvider _primary;
    private readonly IAiProvider _fallback;
    private readonly ILogger<ResilientAiProvider> _logger;

    public ResilientAiProvider(IAiProvider primary, IAiProvider fallback, ILogger<ResilientAiProvider> logger)
    {
        _primary = primary;
        _fallback = fallback;
        _logger = logger;
    }

    public string Name => _primary.Name;

    public bool IsLiveInference => _primary.IsLiveInference;

    public Task<AiCallResult<AiTicketSummary>> SummarizeAsync(AiTicketContext context, CancellationToken ct) =>
        GuardAsync(() => _primary.SummarizeAsync(context, ct), () => _fallback.SummarizeAsync(context, ct),
            "summarize", ct);

    public Task<AiCallResult<AiSuggestedResponse>> SuggestResponseAsync(
        AiTicketContext context,
        IReadOnlyList<string> guidance,
        CancellationToken ct) =>
        GuardAsync(() => _primary.SuggestResponseAsync(context, guidance, ct),
            () => _fallback.SuggestResponseAsync(context, guidance, ct), "suggest-response", ct);

    public Task<AiCallResult<AiSentiment>> AnalyzeSentimentAsync(AiTicketContext context, CancellationToken ct) =>
        GuardAsync(() => _primary.AnalyzeSentimentAsync(context, ct),
            () => _fallback.AnalyzeSentimentAsync(context, ct), "sentiment", ct);

    private async Task<T> GuardAsync<T>(
        Func<Task<T>> primary,
        Func<Task<T>> fallback,
        string operation,
        CancellationToken ct)
    {
        try
        {
            return await primary();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A client-side cancellation is not an AI failure; surface it.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "AI provider {Provider} failed during {Operation}; using deterministic fallback.",
                _primary.Name, operation);
            return await fallback();
        }
    }
}
