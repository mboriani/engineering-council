using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>Run configuration the planner and executor need.</summary>
public sealed record AnalysisRunConfiguration
{
    public required string RunId { get; init; }

    /// <summary>Optional per-provider scope overrides (from configuration).</summary>
    public IReadOnlyDictionary<string, EvidenceAcquisitionScope> ProviderScopeOverrides { get; init; }
        = new Dictionary<string, EvidenceAcquisitionScope>(StringComparer.OrdinalIgnoreCase);

    public int ContextMaxFiles { get; init; } = 200;
    public int ContextMaxCharacters { get; init; } = 500_000;
}

/// <summary>
/// Produces an explicit, deterministic <see cref="EvidenceAcquisitionPlan"/> from
/// provider metadata + configuration: repository-scoped sources get one step;
/// discipline-scoped sources get one step per supported, selected discipline.
/// Never branches on concrete provider names; the same inputs always yield the
/// same ordered plan.
/// </summary>
public interface IEvidenceAcquisitionPlanner
{
    EvidenceAcquisitionPlan CreatePlan(
        AnalysisRunConfiguration configuration,
        RepositorySnapshot repository,
        IReadOnlyCollection<IEvidenceProvider> providers,
        IReadOnlyCollection<FindingCategory> disciplines);
}
