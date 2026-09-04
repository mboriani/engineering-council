namespace EngineeringCouncil.Infrastructure.Providers;

/// <summary>
/// Configuration for <see cref="OpenAILLMProvider"/>. Reads naturally from
/// environment variables so a key is never committed:
/// <c>OPENAI_API_KEY</c>, <c>OPENAI_MODEL</c>, <c>OPENAI_ENDPOINT</c>.
/// A custom endpoint also enables OpenAI-compatible servers (e.g. Ollama).
/// </summary>
public sealed class OpenAIProviderOptions
{
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>Optional base endpoint for OpenAI-compatible servers.</summary>
    public string? Endpoint { get; set; }

    public static OpenAIProviderOptions FromEnvironment() => new()
    {
        ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
        Model = Environment.GetEnvironmentVariable("OPENAI_MODEL") is { Length: > 0 } m ? m : "gpt-4o-mini",
        Endpoint = Environment.GetEnvironmentVariable("OPENAI_ENDPOINT")
    };
}
