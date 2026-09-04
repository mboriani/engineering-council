namespace EngineeringCouncil.Core.Domain;

/// <summary>One provider execution for one acquisition step (success or failure).</summary>
public sealed record ProviderExecutionRecord
{
    public string RunId { get; init; } = string.Empty;
    public string StepId { get; init; } = string.Empty;
    public required string ProviderName { get; init; }
    public string ProviderId { get; init; } = string.Empty;
    public string ProviderVersion { get; init; } = string.Empty;
    public EvidenceProviderType ProviderType { get; init; } = EvidenceProviderType.LLM;

    // ── Acquisition step context (Milestone 008) ──────────────────────────────
    public EvidenceAcquisitionScope Scope { get; init; } = EvidenceAcquisitionScope.Repository;
    public FindingCategory? RequestedDiscipline { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string ContextSelectionStrategy { get; init; } = string.Empty;
    public int ContextFileCount { get; init; }
    public int ContextCharacterCount { get; init; }

    /// <summary>
    /// Files in scope for context selection for this step — the repository file
    /// total, since the selector ranks every scanned file before applying limits.
    /// Identical across steps in a run; a run-level "considered" figure.
    /// </summary>
    public int ContextFilesConsidered { get; init; }

    /// <summary>
    /// Deterministic identity of the effective context sent to the provider
    /// (Milestone 012.2). Two executions are comparable only when they share the
    /// same non-empty fingerprint. See <c>ContextFingerprint</c>.
    /// </summary>
    public string ContextFingerprint { get; init; } = string.Empty;

    public int EvidenceCount { get; init; }
    public int ObservationsProduced { get; init; }

    // ── Structured execution telemetry (Milestone 012.1) ─────────────────────
    // Provider-neutral. The configured model is carried by <see cref="ProviderVersion"/>.

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

    public bool Success { get; init; }
    public TimeSpan Duration { get; init; }
    public int? TokensUsed { get; init; }
    public decimal? CostEstimate { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset ExecutedAt { get; init; } = DateTimeOffset.UtcNow;

    // ── Derived token-efficiency telemetry (Milestone 015.2D) ─────────────────
    // Computed from the authoritative raw fields above (see TokenEfficiencyMetrics).
    // "Activity" accounting only — NOT cost, NOT billable tokens, NOT unique context,
    // and NOT a redefinition of TokensUsed (M12.1). Unknown stays null.

    /// <summary>SumKnown(InputTokens, CacheCreationInputTokens, CacheReadInputTokens) — observed input-side context token activity.</summary>
    public int? ContextTokenActivity { get; init; }

    /// <summary>SumKnown(InputTokens, CacheCreationInputTokens) — input-side activity not reported as cache reads.</summary>
    public int? FreshContextTokens { get; init; }

    /// <summary>CacheReadInputTokens / ContextTokenActivity — cache-reuse fraction; NOT a cost-savings percentage.</summary>
    public double? CacheReuseRatio { get; init; }
}

/// <summary>Per-provider aggregate across all acquisition steps in a run.</summary>
public sealed record ProviderExecutionSummary
{
    public required string ProviderName { get; init; }
    public int Executions { get; init; }
    public int Failures { get; init; }
    public int EvidenceCount { get; init; }
    public int ObservationsProduced { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public int? TotalTokens { get; init; }
    public decimal? TotalCost { get; init; }
}

/// <summary>
/// Run-level report of the multi-provider evidence collection. Written to
/// <c>provider-execution.json</c>. No voting/consensus — just what ran and how.
/// </summary>
public sealed record ProviderExecutionReport
{
    public IReadOnlyList<string> ProvidersExecuted { get; init; } = [];
    public int TotalExecutions { get; init; }
    public int Failures { get; init; }
    public int SuccessfulExecutionCount { get; init; }
    public int EvidenceCount { get; init; }
    public int ObservationsProduced { get; init; }
    public int ContextFilesSelected { get; init; }

    // ── Run-level provider metrics (Milestone 012.1) ─────────────────────────
    // Token totals aggregate KNOWN values only; KnownTokenExecutionCount makes
    // them interpretable. Unavailable usage is never reported as zero.

    /// <summary>Providers with at least one successful AND one failed execution.</summary>
    public int PartialProviderCount { get; init; }

    /// <summary>Sum of known input tokens across executions; null when none are known.</summary>
    public int? TotalInputTokens { get; init; }

    /// <summary>Sum of known output tokens across executions; null when none are known.</summary>
    public int? TotalOutputTokens { get; init; }

    /// <summary>Sum of known total-token values across executions; null when none are known.</summary>
    public int? TotalTokens { get; init; }

    /// <summary>Executions whose total-token usage was known (contributed to TotalTokens).</summary>
    public int KnownTokenExecutionCount { get; init; }

    /// <summary>
    /// Sum of known cache-read input tokens across executions; null when none are
    /// known (Milestone 015.2C). Aggregated independently of
    /// <see cref="TotalTokens"/>; never affects the M12.1 total or
    /// <see cref="KnownTokenExecutionCount"/>.
    /// </summary>
    public int? TotalCacheReadInputTokens { get; init; }

    /// <summary>
    /// Sum of known cache-creation input tokens across executions; null when none
    /// are known (Milestone 015.2C). Aggregated independently; never affects the
    /// M12.1 total or <see cref="KnownTokenExecutionCount"/>.
    /// </summary>
    public int? TotalCacheCreationInputTokens { get; init; }

    // ── Run-level derived token-efficiency telemetry (Milestone 015.2D) ───────
    // Aggregated from the authoritative raw fields with SumKnown. "Activity"
    // accounting only — NOT cost/billable/unique-context and NOT a redefinition of
    // TotalTokens or KnownTokenExecutionCount.

    /// <summary>Sum of per-execution input-side context-token activity; null when none known.</summary>
    public int? TotalContextTokenActivity { get; init; }

    /// <summary>Sum of per-execution fresh context activity; null when none known.</summary>
    public int? TotalFreshContextTokens { get; init; }

    /// <summary>
    /// Run-level cache reuse ratio = TotalCacheReadInputTokens / TotalContextTokenActivity —
    /// computed from AGGREGATE authoritative values, never an average of per-execution
    /// ratios. Null when the denominator is unavailable or zero. NOT a cost-savings
    /// percentage.
    /// </summary>
    public double? CacheReuseRatio { get; init; }

    /// <summary>Total retry attempts across executions (0 = none).</summary>
    public int TotalRetries { get; init; }

    /// <summary>Total structured-response repair attempts (platform allows at most one per execution).</summary>
    public int TotalRepairAttempts { get; init; }

    /// <summary>Executions whose one repair attempt produced a valid response.</summary>
    public int SuccessfulRepairs { get; init; }

    /// <summary>Executions whose failure category is Timeout.</summary>
    public int TimeoutCount { get; init; }

    public TimeSpan TotalDuration { get; init; }
    public decimal? TotalCost { get; init; }

    /// <summary>The acquisition plan that was executed.</summary>
    public EvidenceAcquisitionPlan? Plan { get; init; }

    public IReadOnlyList<ProviderExecutionSummary> ByProvider { get; init; } = [];
    public IReadOnlyList<ProviderExecutionRecord> Records { get; init; } = [];
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;

    public static ProviderExecutionReport FromRecords(
        IReadOnlyList<ProviderExecutionRecord> records,
        EvidenceAcquisitionPlan? plan = null)
    {
        var byProvider = records
            .GroupBy(r => r.ProviderName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ProviderExecutionSummary
            {
                ProviderName = g.Key,
                Executions = g.Count(),
                Failures = g.Count(r => !r.Success),
                EvidenceCount = g.Sum(r => r.EvidenceCount),
                ObservationsProduced = g.Sum(r => r.ObservationsProduced),
                TotalDuration = g.Aggregate(TimeSpan.Zero, (acc, r) => acc + r.Duration),
                TotalTokens = g.Any(r => r.TokensUsed.HasValue) ? g.Sum(r => r.TokensUsed ?? 0) : null,
                TotalCost = g.Any(r => r.CostEstimate.HasValue) ? g.Sum(r => r.CostEstimate ?? 0m) : null
            })
            .OrderBy(s => s.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var knownTokens = records.Where(r => r.TokensUsed.HasValue).Select(r => r.TokensUsed!.Value).ToList();
        var totalCacheRead = SumKnown(records.Select(r => r.CacheReadInputTokens));
        var totalContextActivity = SumKnown(records.Select(r =>
            TokenEfficiencyMetrics.ContextTokenActivity(r.InputTokens, r.CacheCreationInputTokens, r.CacheReadInputTokens)));

        return new ProviderExecutionReport
        {
            ProvidersExecuted = byProvider.Select(s => s.ProviderName).ToList(),
            TotalExecutions = records.Count,
            Failures = records.Count(r => !r.Success),
            SuccessfulExecutionCount = records.Count(r => r.Success),
            EvidenceCount = records.Sum(r => r.EvidenceCount),
            ObservationsProduced = records.Sum(r => r.ObservationsProduced),
            ContextFilesSelected = records.Sum(r => r.ContextFileCount),
            PartialProviderCount = byProvider.Count(s => s.Failures > 0 && s.Failures < s.Executions),
            TotalInputTokens = SumKnown(records.Select(r => r.InputTokens)),
            TotalOutputTokens = SumKnown(records.Select(r => r.OutputTokens)),
            TotalTokens = knownTokens.Count == 0 ? null : knownTokens.Sum(),
            KnownTokenExecutionCount = knownTokens.Count,
            TotalCacheReadInputTokens = totalCacheRead,
            TotalCacheCreationInputTokens = SumKnown(records.Select(r => r.CacheCreationInputTokens)),
            TotalContextTokenActivity = totalContextActivity,
            TotalFreshContextTokens = SumKnown(records.Select(r =>
                TokenEfficiencyMetrics.FreshContextTokens(r.InputTokens, r.CacheCreationInputTokens))),
            CacheReuseRatio = TokenEfficiencyMetrics.CacheReuseRatio(totalCacheRead, totalContextActivity),
            TotalRetries = records.Sum(r => r.RetryCount),
            TotalRepairAttempts = records.Sum(r => r.RepairAttemptCount),
            SuccessfulRepairs = records.Count(r => r.RepairSucceeded == true),
            TimeoutCount = records.Count(r => string.Equals(r.ErrorCategory, "Timeout", StringComparison.Ordinal)),
            TotalDuration = records.Aggregate(TimeSpan.Zero, (acc, r) => acc + r.Duration),
            TotalCost = records.Any(r => r.CostEstimate.HasValue) ? records.Sum(r => r.CostEstimate ?? 0m) : null,
            Plan = plan,
            ByProvider = byProvider,
            Records = records
        };
    }

    /// <summary>Sums only KNOWN nullable values; null when there are none known (never treats unknown as zero).</summary>
    private static int? SumKnown(IEnumerable<int?> values)
    {
        var known = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return known.Count == 0 ? null : known.Sum();
    }
}
