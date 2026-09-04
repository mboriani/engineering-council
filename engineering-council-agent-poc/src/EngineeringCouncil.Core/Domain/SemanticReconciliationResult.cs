namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// The outcome of a targeted semantic review (Milestone 014.4): whether the
/// already-reconciled provider observations behind an ambiguous consolidated
/// finding describe the SAME underlying engineering issue. Never a score,
/// probability, ranking, or vote.
/// </summary>
public enum SemanticReconciliationDecision
{
    /// <summary>The observations describe the same underlying issue, just labeled differently.</summary>
    SameIssue,

    /// <summary>
    /// The observations appear to describe DIFFERENT issues that were reconciled
    /// together. Advisory only in Milestone 014.4 — recorded as provenance; the
    /// finding is NEVER split. See ADR-025 / MILESTONE-014.4 for why.
    /// </summary>
    DifferentIssues,

    /// <summary>The available evidence was insufficient to decide (including any reviewer failure/timeout/malformed output).</summary>
    Inconclusive
}

/// <summary>
/// The result of a targeted semantic review, attached as ADDITIVE provenance on a
/// consolidated <see cref="Finding"/> (Milestone 014.4). It never changes the
/// finding's severity, confidence, recommendation, or reconciliation grouping — the
/// deterministic reconciliation output is authoritative regardless of this result.
/// </summary>
public sealed record SemanticReconciliationResult
{
    public required SemanticReconciliationDecision Decision { get; init; }

    /// <summary>Short rationale, when available. Never chain-of-thought; never raw model output.</summary>
    public string Reason { get; init; } = string.Empty;
}
