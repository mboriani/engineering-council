using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// Produces the council-level executive <see cref="CouncilSummary"/> over a run's
/// consolidated findings. Rule-based in Milestone 003 (no LLM); the contract is
/// ready for an LLM-authored summary later.
/// </summary>
public interface ICouncilSummaryGenerator
{
    Task<CouncilSummary> GenerateAsync(
        AnalysisRun run,
        IReadOnlyList<Finding> consolidatedFindings,
        CancellationToken cancellationToken = default);
}
