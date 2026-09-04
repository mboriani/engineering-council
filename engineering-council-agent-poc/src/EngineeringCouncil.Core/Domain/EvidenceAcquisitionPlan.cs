namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// One explicit acquisition step: run <see cref="ProviderName"/> at
/// <see cref="Scope"/> (for <see cref="Discipline"/> when discipline-scoped).
/// Making the topology explicit (rather than implicit loops) makes it
/// inspectable and testable.
/// </summary>
public sealed record EvidenceAcquisitionStep
{
    public required string StepId { get; init; }
    public required string ProviderName { get; init; }
    public required EvidenceAcquisitionScope Scope { get; init; }

    /// <summary>The discipline for a discipline-scoped step; null for repository-wide.</summary>
    public FindingCategory? Discipline { get; init; }

    /// <summary>Reference to the instructions used (discipline name or "repository").</summary>
    public required string InstructionsReference { get; init; }

    /// <summary>Context-selection strategy the step will use.</summary>
    public required string ContextSelectionStrategy { get; init; }

    /// <summary>Stable id correlating this step to its evidence, observations and telemetry.</summary>
    public required string CorrelationId { get; init; }
}

/// <summary>The full, ordered set of acquisition steps for a run.</summary>
public sealed record EvidenceAcquisitionPlan
{
    public required string RunId { get; init; }
    public required IReadOnlyList<EvidenceAcquisitionStep> Steps { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Provider/discipline combinations that were excluded (unsupported), with the reason.</summary>
    public IReadOnlyList<string> UnsupportedCombinations { get; init; } = [];

    public int RepositoryScopedSteps => Steps.Count(s => s.Scope == EvidenceAcquisitionScope.Repository);
    public int DisciplineScopedSteps => Steps.Count(s => s.Scope == EvidenceAcquisitionScope.Discipline);
}
