namespace EngineeringCouncil.Core.Abstractions;

/// <summary>What happens when an explicitly selected provider cannot execute.</summary>
public enum ProviderFailureMode
{
    /// <summary>Default: continue with the other providers and mark the failed execution.</summary>
    Continue = 0,

    /// <summary>Strict: fail the whole run when any selected provider cannot execute.</summary>
    FailRun = 1
}

/// <summary>
/// Configuration shared by the real LLM evidence providers (Milestone 011). Bound
/// from <c>Evidence:Claude</c> / <c>Evidence:OpenAI</c>. External providers are
/// DISABLED by default — nothing leaves the machine unless explicitly enabled.
///
/// The API key itself is NEVER stored in configuration or artifacts: only the NAME
/// of the environment variable that holds it is configurable, and the value is read
/// from the environment at resolution time.
/// </summary>
public abstract class LlmProviderOptions
{
    /// <summary>Provider display name (also the acquisition provider name).</summary>
    public abstract string ProviderName { get; }

    /// <summary>Disabled by default: real providers send selected repository content externally.</summary>
    public bool Enabled { get; set; }

    /// <summary>Name of the environment variable holding the API key (never the key itself).</summary>
    public string ApiKeyEnvironmentVariable { get; set; } = string.Empty;

    /// <summary>Model id to use. Always configurable; never hardcoded per discipline.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Optional base endpoint override (self-hosted / compatible gateways).</summary>
    public string? Endpoint { get; set; }

    public int MaxOutputTokens { get; set; } = 8000;

    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Maximum retries for TRANSIENT failures only (rate limit, timeout, 5xx).</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Allow at most one structured-response repair attempt when validation fails.</summary>
    public bool EnableStructuredRepair { get; set; } = true;

    /// <summary>Resolves the API key from the configured environment variable (never logged).</summary>
    public string? ResolveApiKey()
        => string.IsNullOrWhiteSpace(ApiKeyEnvironmentVariable)
            ? null
            : Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);

    /// <summary>True when the provider is enabled, configured, and has a usable key.</summary>
    public bool IsUsable => Enabled
        && !string.IsNullOrWhiteSpace(Model)
        && !string.IsNullOrWhiteSpace(ResolveApiKey());

    /// <summary>A secret-safe explanation of why the provider cannot run, or null when it can.</summary>
    public string? UnavailableReason
    {
        get
        {
            if (!Enabled) return $"{ProviderName} is disabled (set Evidence:{ProviderName}:Enabled = true, or select it explicitly).";
            if (string.IsNullOrWhiteSpace(ResolveApiKey()))
                return $"{ProviderName} requires the environment variable {ApiKeyEnvironmentVariable}.";
            return string.IsNullOrWhiteSpace(Model)
                ? $"{ProviderName} has no configured model (set Evidence:{ProviderName}:Model)."
                : null;
        }
    }

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds <= 0 ? 120 : TimeoutSeconds);
}

/// <summary>Configuration for the real Anthropic Claude evidence provider.</summary>
public sealed class ClaudeProviderOptions : LlmProviderOptions
{
    public const string SectionName = "Evidence:Claude";

    public override string ProviderName => "Claude";

    public ClaudeProviderOptions()
    {
        ApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY";
        Model = "claude-sonnet-5";   // configurable via Evidence:Claude:Model
    }
}

/// <summary>
/// Configuration for the real OpenAI evidence provider. A code-focused model is
/// simply the configured <see cref="LlmProviderOptions.Model"/> of this provider.
/// The "Codex" name is NOT an alias for this provider (Milestone 013.4): "Codex"
/// now selects the real agentic Codex CLI provider.
/// </summary>
public sealed class OpenAiProviderOptions : LlmProviderOptions
{
    public const string SectionName = "Evidence:OpenAI";

    public override string ProviderName => "OpenAI";

    public OpenAiProviderOptions()
    {
        ApiKeyEnvironmentVariable = "OPENAI_API_KEY";
        Model = "gpt-4o-mini";      // configurable via Evidence:OpenAI:Model
    }
}
