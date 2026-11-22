namespace Northstar.Infrastructure.Ai;

public sealed class AiProviderOptions
{
    public const string SectionName = "Ai";

    /// <summary>
    /// "deterministic" (default, no external calls), "bedrock",
    /// "openai-compatible", or "azure-openai". Bedrock uses the AWS default
    /// credential chain; the other live providers require an API key.
    /// </summary>
    public string Provider { get; set; } = "deterministic";

    public string? BaseUrl { get; set; }

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gpt-4o-mini";

    public string BedrockModelId { get; set; } = "anthropic.claude-3-5-sonnet-20241022-v2:0";

    public string? Region { get; set; }

    public string? GuardrailIdentifier { get; set; }

    public string? GuardrailVersion { get; set; }

    public int MaxTokens { get; set; } = 1200;

    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>Hard cap on the serialised context sent to the model.</summary>
    public int MaxInputCharacters { get; set; } = 12000;

    public double Temperature { get; set; } = 0.2;
}
