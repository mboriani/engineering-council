namespace EngineeringCouncil.Infrastructure.Llm;

/// <summary>Categorized, provider-neutral failure reasons. Never carries secrets.</summary>
public enum LlmErrorCategory
{
    Unknown = 0,
    Authentication = 1,
    RateLimit = 2,
    Timeout = 3,
    Network = 4,
    InvalidRequest = 5,
    UnsupportedModel = 6,
    ServerError = 7,
    ContextTooLarge = 8,
    Cancelled = 9,
    SchemaValidation = 10,
    Configuration = 11,

    /// <summary>
    /// An external process (e.g. the OpenCode agentic runtime) failed to start or
    /// exited with a non-zero code. Never retried — the process result is what it is.
    /// </summary>
    ProcessError = 12
}

/// <summary>
/// A categorized provider failure. <see cref="IsTransient"/> drives the retry policy:
/// only rate limits, timeouts, network blips and eligible server errors are retried —
/// never authentication, invalid requests, unsupported models or schema failures.
/// </summary>
public sealed class LlmProviderException : Exception
{
    public LlmProviderException(
        LlmErrorCategory category, string message, Exception? inner = null,
        int? retryCount = null, int? repairAttemptCount = null, bool? repairSucceeded = null,
        bool? responseTruncated = null)
        : base(message, inner)
    {
        Category = category;
        RetryCount = retryCount;
        RepairAttemptCount = repairAttemptCount;
        RepairSucceeded = repairSucceeded;
        ResponseTruncated = responseTruncated;
    }

    public LlmErrorCategory Category { get; }

    /// <summary>Actual retries attempted before this failure (null when unknown).</summary>
    public int? RetryCount { get; }

    /// <summary>Repair attempts made before this failure (null when unknown).</summary>
    public int? RepairAttemptCount { get; }

    /// <summary>Whether the repair attempt produced a valid response (null when none attempted).</summary>
    public bool? RepairSucceeded { get; }

    /// <summary>True only when the provider reported the output was cut off by the token limit.</summary>
    public bool? ResponseTruncated { get; }

    public bool IsTransient => Category is LlmErrorCategory.RateLimit
        or LlmErrorCategory.Timeout or LlmErrorCategory.Network or LlmErrorCategory.ServerError;
}

/// <summary>One provider-neutral completion request (already prompt-built).</summary>
public sealed record LlmCompletionRequest
{
    public required string SystemInstructions { get; init; }
    public required string UserContent { get; init; }
    public required string Model { get; init; }
    public int MaxOutputTokens { get; init; } = 8000;
}

/// <summary>
/// One provider-neutral completion response. Usage/ids/finish reason are OPTIONAL —
/// unavailable information stays null rather than being invented.
/// </summary>
public sealed record LlmCompletionResponse
{
    public required string Text { get; init; }
    public string? ResponseId { get; init; }
    public string? RequestId { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public int? TotalTokens => InputTokens is null && OutputTokens is null ? null : (InputTokens ?? 0) + (OutputTokens ?? 0);
    public string? FinishReason { get; init; }

    /// <summary>True when the provider reported the output was cut off by the token limit.</summary>
    public bool Truncated { get; init; }
}

/// <summary>
/// Provider-specific network/SDK boundary. Everything HTTP lives behind this seam so
/// unit tests use fakes and never touch the network, credentials, or paid models.
/// </summary>
public interface ILlmChatClient
{
    /// <summary>Stable provider id for telemetry (e.g. "claude", "openai").</summary>
    string ProviderId { get; }

    Task<LlmCompletionResponse> CompleteAsync(LlmCompletionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Anthropic Claude transport.</summary>
public interface IClaudeClient : ILlmChatClient;

/// <summary>OpenAI transport (a code-focused model is just this provider's configured model).</summary>
public interface IOpenAiClient : ILlmChatClient;
