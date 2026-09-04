namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// Deterministic, rule-based scoring of engineering health and risk from the
/// finding severity distribution. No LLM. Thresholds are intentionally explicit
/// and documented (see ADR-007) so scores are reproducible and explainable.
///
/// Health (worst matching rule wins, checked top-down):
///   Critical        : ≥1 Critical, or ≥5 High
///   NeedsAttention  : ≥1 High, or ≥8 Medium
///   Fair            : ≥1 Medium, or ≥8 Low
///   Good            : ≥1 Low (below the Fair thresholds)
///   Excellent       : no findings above Info
///
/// Risk (worst matching rule wins, checked top-down):
///   Critical  : ≥1 Critical
///   High      : ≥1 High, or ≥5 Medium
///   Moderate  : ≥1 Medium, or ≥8 Low
///   Low       : otherwise
/// </summary>
public static class HealthRiskScorer
{
    public static EngineeringHealth ScoreHealth(EngineeringMetrics m)
    {
        if (m.Critical >= 1 || m.High >= 5) return EngineeringHealth.Critical;
        if (m.High >= 1 || m.Medium >= 8) return EngineeringHealth.NeedsAttention;
        if (m.Medium >= 1 || m.Low >= 8) return EngineeringHealth.Fair;
        if (m.Low >= 1) return EngineeringHealth.Good;
        return EngineeringHealth.Excellent;
    }

    public static EngineeringRisk ScoreRisk(EngineeringMetrics m)
    {
        if (m.Critical >= 1) return EngineeringRisk.Critical;
        if (m.High >= 1 || m.Medium >= 5) return EngineeringRisk.High;
        if (m.Medium >= 1 || m.Low >= 8) return EngineeringRisk.Moderate;
        return EngineeringRisk.Low;
    }
}
