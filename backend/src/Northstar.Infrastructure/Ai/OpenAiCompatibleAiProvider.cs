using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Northstar.Application.Abstractions;
using Northstar.Application.Ai;

namespace Northstar.Infrastructure.Ai;

/// <summary>
/// Live provider for any OpenAI-compatible chat completions endpoint (Azure
/// OpenAI, OpenAI, Amazon Bedrock via a compatible gateway, or a self-hosted
/// model server). The API key is held only on the server; the browser never has
/// access to it.
///
/// Every response is constrained to a JSON schema and parsed into a strongly
/// typed contract. Malformed or non-conforming output raises an error so the
/// resilient wrapper can fall back to the deterministic provider.
/// </summary>
public sealed class OpenAiCompatibleAiProvider : IAiProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;
    private readonly AiProviderOptions _options;

    public OpenAiCompatibleAiProvider(HttpClient http, IOptions<AiProviderOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public string Name => _options.Provider;

    public bool IsLiveInference => true;

    public async Task<AiCallResult<AiTicketSummary>> SummarizeAsync(AiTicketContext context, CancellationToken ct)
    {
        var prompt = AiPrompts.SummarizeUserPrompt(context, _options.MaxInputCharacters);
        var sw = Stopwatch.StartNew();
        var value = await CompleteAsync<AiTicketSummary>(prompt, ct);
        sw.Stop();
        return new AiCallResult<AiTicketSummary>(value, Name, _options.Model, true, null, sw.ElapsedMilliseconds);
    }

    public async Task<AiCallResult<AiSuggestedResponse>> SuggestResponseAsync(
        AiTicketContext context,
        IReadOnlyList<string> guidance,
        CancellationToken ct)
    {
        var prompt = AiPrompts.SuggestResponseUserPrompt(context, guidance, _options.MaxInputCharacters);
        var sw = Stopwatch.StartNew();
        var value = await CompleteAsync<AiSuggestedResponse>(prompt, ct);
        sw.Stop();
        return new AiCallResult<AiSuggestedResponse>(value, Name, _options.Model, true, null, sw.ElapsedMilliseconds);
    }

    public async Task<AiCallResult<AiSentiment>> AnalyzeSentimentAsync(AiTicketContext context, CancellationToken ct)
    {
        var prompt = AiPrompts.SentimentUserPrompt(context, _options.MaxInputCharacters);
        var sw = Stopwatch.StartNew();
        var value = await CompleteAsync<AiSentiment>(prompt, ct);
        sw.Stop();
        return new AiCallResult<AiSentiment>(value, Name, _options.Model, true, value.Confidence, sw.ElapsedMilliseconds);
    }

    private async Task<T> CompleteAsync<T>(string userPrompt, CancellationToken ct)
    {
        var request = new ChatRequest(
            _options.Model,
            new[]
            {
                new ChatMessage("system", AiPrompts.SystemPrompt()),
                new ChatMessage("user", userPrompt)
            },
            _options.Temperature,
            new ResponseFormat("json_object"));

        using var response = await _http.PostAsJsonAsync(BuildRequestUri(), request, Json, ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ChatResponse>(Json, ct)
                      ?? throw new InvalidOperationException("The AI provider returned an empty response.");

        var content = payload.Choices.FirstOrDefault()?.Message?.Content
                      ?? throw new InvalidOperationException("The AI provider returned no completion content.");

        return JsonSerializer.Deserialize<T>(content, Json)
               ?? throw new InvalidOperationException("The AI response did not match the expected schema.");
    }

    private string BuildRequestUri()
    {
        var baseUrl = (_options.BaseUrl ?? string.Empty).TrimEnd('/');
        var uri = $"{baseUrl}/chat/completions";

        // Azure OpenAI requires an api-version query parameter and a different
        // path shape; if the base URL points at an Azure resource we append the
        // default version unless one is already present.
        if (baseUrl.Contains("openai.azure.com", StringComparison.OrdinalIgnoreCase) &&
            !uri.Contains("api-version", StringComparison.OrdinalIgnoreCase))
        {
            uri += "?api-version=2024-06-01";
        }

        return uri;
    }

    private sealed record ChatRequest(
        string Model,
        ChatMessage[] Messages,
        double Temperature,
        ResponseFormat Response_Format);

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ResponseFormat(string Type);

    private sealed record ChatResponse(ChatChoice[] Choices);

    private sealed record ChatChoice(ChatMessageOut? Message);

    private sealed record ChatMessageOut(string? Content);
}
