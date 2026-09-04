namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// A generic, provider-agnostic summary of what the acquisition plan covered.
/// Part of the Engineering Review Package (no provider-specific types leak into
/// the central model).
/// </summary>
public sealed record AcquisitionCoverage
{
    public IReadOnlyList<string> RepositoryWideSourcesExecuted { get; init; } = [];
    public IReadOnlyList<string> DisciplineScopedSourcesExecuted { get; init; } = [];
    public IReadOnlyList<string> DisciplinesRequested { get; init; } = [];
    public IReadOnlyList<string> DisciplinesCovered { get; init; } = [];
    public int AcquisitionSteps { get; init; }
    public int FailedSteps { get; init; }

    /// <summary>Files in scope for context selection across the run (the repository
    /// file total the selector ranks). Constant across steps; a run-level figure.</summary>
    public int ContextFilesConsidered { get; init; }

    /// <summary>Files actually included in context across all steps (sum of per-step selections).</summary>
    public int ContextFilesSelected { get; init; }
    public IReadOnlyList<string> UnsupportedCombinations { get; init; } = [];

    /// <summary>
    /// Builds the coverage summary from the plan, the execution report, and the
    /// disciplines that were requested for the run.
    /// </summary>
    public static AcquisitionCoverage From(
        EvidenceAcquisitionPlan? plan,
        ProviderExecutionReport? report,
        IReadOnlyList<FindingCategory> requestedDisciplines)
    {
        if (plan is null)
            return new AcquisitionCoverage
            {
                DisciplinesRequested = requestedDisciplines.Select(d => d.ToString()).ToList()
            };

        var repoSources = plan.Steps
            .Where(s => s.Scope == EvidenceAcquisitionScope.Repository)
            .Select(s => s.ProviderName).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();

        var disciplineSources = plan.Steps
            .Where(s => s.Scope == EvidenceAcquisitionScope.Discipline)
            .Select(s => s.ProviderName).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();

        // A discipline is "covered" when at least one step for it produced evidence.
        var succeededStepIds = report is null
            ? new HashSet<string>()
            : report.Records.Where(r => r.Success).Select(r => r.StepId).ToHashSet();

        var covered = plan.Steps
            .Where(s => s.Discipline is not null && succeededStepIds.Contains(s.StepId))
            .Select(s => s.Discipline!.Value.ToString())
            .Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();

        return new AcquisitionCoverage
        {
            RepositoryWideSourcesExecuted = repoSources,
            DisciplineScopedSourcesExecuted = disciplineSources,
            DisciplinesRequested = requestedDisciplines.Select(d => d.ToString()).ToList(),
            DisciplinesCovered = covered,
            AcquisitionSteps = plan.Steps.Count,
            FailedSteps = report?.Failures ?? 0,
            // Files CONSIDERED for context selection = the repository scope the
            // selector ranks (constant across steps in a run). Not the largest
            // single-step SELECTION — that would make the run-level figure depend
            // on per-step limits and undercount the considered scope.
            ContextFilesConsidered = report?.Records.Count > 0 ? report.Records.Max(r => r.ContextFilesConsidered) : 0,
            ContextFilesSelected = report?.ContextFilesSelected ?? 0,
            UnsupportedCombinations = plan.UnsupportedCombinations
        };
    }
}
