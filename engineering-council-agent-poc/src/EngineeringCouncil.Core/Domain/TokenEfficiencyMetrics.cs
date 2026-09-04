namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// Provider-neutral DERIVED token-efficiency metrics computed from already-captured
/// AUTHORITATIVE raw telemetry (Milestone 015.2D). They are "activity" accounting,
/// NOT cost, NOT billable tokens, NOT unique context size, NOT model context-window
/// occupancy, and NOT <see cref="Evidence.TotalTokens"/> semantics. They answer
/// "where is the agent spending context/token activity?" without claiming monetary
/// value.
///
/// Semantics (all SumKnown — unknown stays null, never 0):
/// - <see cref="ContextTokenActivity"/> = SumKnown(InputTokens, CacheCreationInputTokens, CacheReadInputTokens)
///   → observed INPUT-side context token activity reported by the runtime.
/// - <see cref="FreshContextTokens"/> = SumKnown(InputTokens, CacheCreationInputTokens)
///   → input-side activity not reported as cache reads.
/// - <see cref="CacheReuseRatio"/> = CacheReadInputTokens / ContextTokenActivity
///   → the fraction of observed input-side activity served as cache reads. This is
///   NOT a cost-savings percentage and NOT "how much of the repository was cached".
///
/// Any provider that later exposes equivalent cache telemetry feeds these same
/// calculations automatically; nothing here is Claude-specific.
/// </summary>
public static class TokenEfficiencyMetrics
{
    private static int? SumKnown(params int?[] values)
        => values.Any(v => v.HasValue) ? values.Sum(v => v ?? 0) : null;

    /// <summary>SumKnown(InputTokens, CacheCreationInputTokens, CacheReadInputTokens).</summary>
    public static int? ContextTokenActivity(int? inputTokens, int? cacheCreationInputTokens, int? cacheReadInputTokens)
        => SumKnown(inputTokens, cacheCreationInputTokens, cacheReadInputTokens);

    /// <summary>SumKnown(InputTokens, CacheCreationInputTokens).</summary>
    public static int? FreshContextTokens(int? inputTokens, int? cacheCreationInputTokens)
        => SumKnown(inputTokens, cacheCreationInputTokens);

    /// <summary>
    /// CacheReadInputTokens / ContextTokenActivity. Null when cache-read is unknown,
    /// when activity is unknown, or when activity is zero (no meaningful denominator).
    /// Never turns unknown into 0.
    /// </summary>
    public static double? CacheReuseRatio(int? cacheReadInputTokens, int? contextTokenActivity)
        => cacheReadInputTokens.HasValue && contextTokenActivity is > 0
            ? (double)cacheReadInputTokens.Value / contextTokenActivity.Value
            : null;
}