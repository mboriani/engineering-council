using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// Deterministic multi-source reconciliation (Milestone 010). Takes the raw
/// analyzer findings (which may describe the same engineering issue from different
/// evidence sources — Claude, Codex, SARIF, …) and the observations that backed
/// them, and produces ONE consolidated finding collection plus traceability.
///
/// It runs BEFORE the Engineering Review Package builder. It is purely rule-based:
/// no LLM arbitration, no voting, no provider weights, no councils. It prefers
/// false negatives (leaving equivalent findings separate) to incorrect merges.
/// The same inputs in the same repository state produce the same result.
/// </summary>
public interface IFindingReconciler
{
    ReconciliationResult Reconcile(
        IReadOnlyList<Finding> rawFindings,
        IReadOnlyList<EngineeringObservation> observations,
        CancellationToken cancellationToken = default);
}
