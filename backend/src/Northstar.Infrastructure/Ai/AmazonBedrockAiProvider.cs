using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Options;
using Northstar.Application.Abstractions;
using Northstar.Application.Ai;

namespace Northstar.Infrastructure.Ai;

public sealed class AmazonBedrockAiProvider : IAiProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AiProviderOptions _options;

    public AmazonBedrockAiProvider(IOptions<AiProviderOptions> options)
    {
        _options = options.Value;
    }

    public string Name => "bedrock";

    public bool IsLiveInference => true;

    public async Task<AiCallResult<AiTicketSummary>> SummarizeAsync(AiTicketContext context, CancellationToken ct)
    {
        var prompt = AiPrompts.SummarizeUserPrompt(context, _options.MaxInputCharacters);
        var sw = Stopwatch.StartNew();
        var value = await CompleteAsync<AiTicketSummary>(prompt, ct);
        sw.Stop();
        return new AiCallResult<AiTicketSummary>(
            value, Name, _options.BedrockModelId, true, null, sw.ElapsedMilliseconds);
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
        return new AiCallResult<AiSuggestedResponse>(
            value, Name, _options.BedrockModelId, true, null, sw.ElapsedMilliseconds);
    }

    public async Task<AiCallResult<AiSentiment>> AnalyzeSentimentAsync(AiTicketContext context, CancellationToken ct)
    {
        var prompt = AiPrompts.SentimentUserPrompt(context, _options.MaxInputCharacters);
        var sw = Stopwatch.StartNew();
        var value = await CompleteAsync<AiSentiment>(prompt, ct);
        sw.Stop();
        return new AiCallResult<AiSentiment>(
            value, Name, _options.BedrockModelId, true, value.Confidence, sw.ElapsedMilliseconds);
    }

    private async Task<T> CompleteAsync<T>(string userPrompt, CancellationToken ct)
    {
        var request = new ConverseRequest
        {
            ModelId = _options.BedrockModelId,
            System = new List<SystemContentBlock>
            {
                new() { Text = AiPrompts.SystemPrompt() }
            },
            Messages = new List<Message>
            {
                new()
                {
                    Role = ConversationRole.User,
                    Content = new List<ContentBlock> { new() { Text = userPrompt } }
                }
            },
            InferenceConfig = new InferenceConfiguration
            {
                Temperature = (float)_options.Temperature,
                MaxTokens = _options.MaxTokens
            }
        };

        if (!string.IsNullOrWhiteSpace(_options.GuardrailIdentifier) &&
            !string.IsNullOrWhiteSpace(_options.GuardrailVersion))
        {
            request.GuardrailConfig = new GuardrailConfiguration
            {
                GuardrailIdentifier = _options.GuardrailIdentifier,
                GuardrailVersion = _options.GuardrailVersion
            };
        }

        using var client = CreateClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        var response = await client.ConverseAsync(request, timeout.Token);
        var content = string.Join(
            string.Empty,
            response.Output?.Message?.Content?
                .Where(block => !string.IsNullOrEmpty(block.Text))
                .Select(block => block.Text) ?? Enumerable.Empty<string>());

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("The Bedrock provider returned no completion content.");
        }

        return JsonSerializer.Deserialize<T>(content, Json)
               ?? throw new InvalidOperationException("The Bedrock response did not match the expected schema.");
    }

    private AmazonBedrockRuntimeClient CreateClient()
    {
        if (string.IsNullOrWhiteSpace(_options.Region))
        {
            return new AmazonBedrockRuntimeClient();
        }

        var config = new AmazonBedrockRuntimeConfig
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(_options.Region)
        };
        return new AmazonBedrockRuntimeClient(config);
    }
}
