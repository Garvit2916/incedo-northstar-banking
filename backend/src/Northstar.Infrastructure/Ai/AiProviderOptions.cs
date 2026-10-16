namespace Northstar.Infrastructure.Ai;

public sealed class AiProviderOptions
{
    public const string SectionName = "Ai";

    /// <summary>
    /// "deterministic" (default, no external calls), "openai-compatible", or
    /// "azure-openai". Anything other than "deterministic" requires an API key.
    /// </summary>
    public string Provider { get; set; } = "deterministic";

    public string? BaseUrl { get; set; }

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gpt-4o-mini";

    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>Hard cap on the serialised context sent to the model.</summary>
    public int MaxInputCharacters { get; set; } = 12000;

    public double Temperature { get; set; } = 0.2;
}
