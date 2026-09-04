namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// Configuration for targeted semantic reconciliation (Milestone 014.4).
/// **Disabled by default** — offline/default Council behavior stays byte-for-byte
/// identical to Milestone 014.3. When enabled, ONLY consolidated findings whose
/// deterministic Council assessment flags an <c>ObservationType</c> disagreement are
/// ever reviewed (see <c>SemanticReconciliationBuilder.IsCandidate</c>).
/// </summary>
public sealed class SemanticReconciliationOptions
{
    public const string SectionName = "Council:SemanticReconciliation";

    public bool Enabled { get; set; }

    /// <summary>Model id used by the real (LLM-backed) reviewer, when enabled. Never hardcoded.</summary>
    public string Model { get; set; } = string.Empty;

    public int MaxOutputTokens { get; set; } = 300;

    public int TimeoutSeconds { get; set; } = 60;

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds <= 0 ? 60 : TimeoutSeconds);
}
