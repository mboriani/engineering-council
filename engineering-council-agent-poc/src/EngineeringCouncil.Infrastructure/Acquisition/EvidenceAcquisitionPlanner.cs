using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Infrastructure.Acquisition;

/// <summary>
/// Builds the explicit, deterministic acquisition plan from provider metadata +
/// configuration. Repository-scoped sources contribute one step; discipline-scoped
/// sources contribute one step per supported, selected discipline. Unsupported
/// provider/discipline combinations create no step and are recorded on the plan.
/// Never branches on concrete provider names — only on declared metadata (with an
/// optional configured scope override).
///
/// Stable ordering: (1) provider order as supplied, (2) repository step before
/// discipline steps for that provider, (3) discipline order as supplied.
/// </summary>
public sealed class EvidenceAcquisitionPlanner : IEvidenceAcquisitionPlanner
{
    public EvidenceAcquisitionPlan CreatePlan(
        AnalysisRunConfiguration configuration,
        RepositorySnapshot repository,
        IReadOnlyCollection<IEvidenceProvider> providers,
        IReadOnlyCollection<FindingCategory> disciplines)
    {
        var steps = new List<EvidenceAcquisitionStep>();
        var unsupported = new List<string>();

        foreach (var provider in providers)
        {
            var name = provider.Metadata.Name;
            var scope = configuration.ProviderScopeOverrides.TryGetValue(name, out var overridden)
                ? overridden
                : provider.Metadata.DefaultAcquisitionScope;

            if (scope == EvidenceAcquisitionScope.Repository)
            {
                steps.Add(new EvidenceAcquisitionStep
                {
                    StepId = $"{name}#repository",
                    ProviderName = name,
                    Scope = EvidenceAcquisitionScope.Repository,
                    Discipline = null,
                    InstructionsReference = "repository",
                    ContextSelectionStrategy = "repository-wide",
                    CorrelationId = $"{name}:repository"
                });
                continue;
            }

            foreach (var discipline in disciplines)
            {
                if (!provider.Metadata.Supports(discipline))
                {
                    unsupported.Add($"{name}:{discipline} (provider does not support this discipline)");
                    continue;
                }

                steps.Add(new EvidenceAcquisitionStep
                {
                    StepId = $"{name}#{discipline}",
                    ProviderName = name,
                    Scope = EvidenceAcquisitionScope.Discipline,
                    Discipline = discipline,
                    InstructionsReference = discipline.ToString(),
                    ContextSelectionStrategy = $"{discipline.ToString().ToLowerInvariant()}-focused-v1",
                    CorrelationId = $"{name}:{discipline}"
                });
            }
        }

        return new EvidenceAcquisitionPlan
        {
            RunId = configuration.RunId,
            Steps = steps,
            CreatedAt = DateTimeOffset.UtcNow,
            UnsupportedCombinations = unsupported
        };
    }
}
