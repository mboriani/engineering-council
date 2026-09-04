using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// The first consolidation layer: takes the raw findings emitted by all
/// specialized analyzers and produces a consolidated set — de-duplicated,
/// severity-reconciled, and merge-annotated. It does NOT talk to an LLM in
/// Milestone 003; the contract is shaped so an LLM-based reconciler can replace
/// the rule-based one later without changing callers.
/// </summary>
public interface IFindingMerger
{
    Task<IReadOnlyList<Finding>> MergeAsync(
        IReadOnlyList<Finding> rawFindings,
        CancellationToken cancellationToken = default);
}
