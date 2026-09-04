namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// Engineering evidence gathered by an <c>IEvidenceProvider</c> for one analyzer.
/// It is the RAW input an analyzer reasons over to produce <see cref="Finding"/>s
/// — it is deliberately NOT a finding. A provider never creates findings; it only
/// returns evidence.
/// </summary>
public sealed record Evidence
{
    /// <summary>Unique id for this evidence item; referenced by observations for traceability.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>Name of the provider that produced this evidence (e.g. "Claude", "Mock").</summary>
    public required string ProviderName { get; init; }

    /// <summary>Stable provider identifier (slug), e.g. "claude", "mock".</summary>
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>Provider/backend version (e.g. model id or tool version).</summary>
    public string ProviderVersion { get; init; } = string.Empty;

    public EvidenceProviderType ProviderType { get; init; } = EvidenceProviderType.LLM;

    /// <summary>The raw provider response the analyzer will interpret (e.g. model text, tool output).</summary>
    public string RawResponse { get; init; } = string.Empty;

    /// <summary>Provider-reported confidence in the evidence (0..1). Advisory only.</summary>
    public double Confidence { get; init; }

    /// <summary>How long the provider itself reported taking to produce the evidence.</summary>
    public TimeSpan ExecutionTime { get; init; }

    /// <summary>Wall-clock duration the executor measured for this provider (incl. failures/timeouts).</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>True when the provider produced evidence; false on failure/timeout.</summary>
    public bool Success { get; init; } = true;

    /// <summary>Populated when <see cref="Success"/> is false.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Tokens consumed, when the provider is an LLM that reports usage.</summary>
    public int? TokensUsed { get; init; }

    /// <summary>Optional monetary cost estimate for the call.</summary>
    public decimal? CostEstimate { get; init; }

    // ── Structured execution telemetry (Milestone 012.1) ─────────────────────
    // Provider-neutral; unavailable values stay null rather than being invented.
    // The configured model is carried by <see cref="ProviderVersion"/>.

    /// <summary>Input tokens reported by the provider (null when unknown).</summary>
    public int? InputTokens { get; init; }

    /// <summary>Output tokens reported by the provider (null when unknown).</summary>
    public int? OutputTokens { get; init; }

    /// <summary>
    /// Input tokens served from the provider's cache (null when unknown). Separately
    /// observable, additive operational telemetry (Milestone 015.2C) — never added
    /// into <see cref="TokensUsed"/>, whose semantics are unchanged.
    /// </summary>
    public int? CacheReadInputTokens { get; init; }

    /// <summary>
    /// Input tokens the provider newly wrote to its cache (null when unknown).
    /// Separately observable, additive operational telemetry (Milestone 015.2C) —
    /// never added into <see cref="TokensUsed"/>.
    /// </summary>
    public int? CacheCreationInputTokens { get; init; }

    /// <summary>
    /// Total retries actually made for this step: provider-internal transient retries
    /// (from the final attempt) plus the bounded acquisition-level retries (Milestone
    /// 015.3B). 0 = the first request succeeded.
    /// </summary>
    public int RetryCount { get; init; }

    /// <summary>
    /// Total acquisition attempts for this logical step (Milestone 015.3B). 1 = no
    /// step-level retry; retries are bounded by <c>Evidence:Execution:MaxAttempts</c>
    /// (default 2).
    /// </summary>
    public int AttemptCount { get; init; } = 1;

    /// <summary>
    /// True only when this step failed on its FINAL allowed attempt with a retry-eligible
    /// (Timeout) failure — the step wanted to retry but had no attempt left. Never true for
    /// a success or for failures the retry policy never retries.
    /// </summary>
    public bool RetryExhausted { get; init; }

    /// <summary>Structured-response repair attempts (platform allows at most one).</summary>
    public int RepairAttemptCount { get; init; }

    /// <summary>True when the one repair attempt produced a valid response; null when none was attempted.</summary>
    public bool? RepairSucceeded { get; init; }

    /// <summary>Provider-reported finish reason (safe string; null when unavailable).</summary>
    public string? FinishReason { get; init; }

    /// <summary>True only when the provider reported the output was cut off by the token limit.</summary>
    public bool ResponseTruncated { get; init; }

    /// <summary>Provider-neutral failure category (e.g. "Timeout", "SchemaValidation"); null on success.</summary>
    public string? ErrorCategory { get; init; }

    // ── Acquisition provenance (Milestone 008) ────────────────────────────────
    public EvidenceAcquisitionScope AcquisitionScope { get; init; } = EvidenceAcquisitionScope.Repository;

    /// <summary>Discipline the request targeted; null for repository-wide acquisition.</summary>
    public FindingCategory? RequestedDiscipline { get; init; }

    /// <summary>Correlates this evidence to its acquisition step, context selection and telemetry.</summary>
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>The acquisition step that produced this evidence.</summary>
    public string AcquisitionStepId { get; init; } = string.Empty;

    public int ContextFileCount { get; init; }
    public int ContextCharacterCount { get; init; }
    public string ContextSelectionStrategy { get; init; } = string.Empty;

    /// <summary>
    /// Deterministic identity of the effective context this evidence was produced
    /// against (Milestone 012.2). See <c>ContextFingerprint</c>.
    /// </summary>
    public string ContextFingerprint { get; init; } = string.Empty;

    /// <summary>Free-form provider metadata (model id, discipline, tool version, …).</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
