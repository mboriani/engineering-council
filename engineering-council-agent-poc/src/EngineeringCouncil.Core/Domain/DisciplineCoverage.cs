namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// Deterministic per-discipline evidence coverage state (Milestone 015.3A).
/// Distinguishes "evidence was acquired and findings exist" from "evidence was
/// acquired and no findings" from "no successful evidence at all" — because the
/// ABSENCE of evidence must never be presented as assurance that a discipline is
/// clean.
/// </summary>
public enum DisciplineCoverageStatus
{
    /// <summary>At least one successful evidence acquisition AND consolidated findings exist.</summary>
    CoveredWithFindings = 0,

    /// <summary>At least one successful evidence acquisition and zero consolidated findings.</summary>
    CoveredNoFindings = 1,

    /// <summary>Zero successful evidence acquisitions (timeouts/failures do not count).</summary>
    NoEvidence = 2
}

/// <summary>
/// Coverage for ONE requested discipline: the state plus how many providers were
/// attempted vs. succeeded. Provider-neutral; counts, never a score/percentage.
/// </summary>
public sealed record DisciplineCoverageEntry
{
    /// <summary>The discipline name (a <see cref="FindingCategory"/> name).</summary>
    public required string Discipline { get; init; }

    public DisciplineCoverageStatus Status { get; init; }

    /// <summary>Distinct providers with a successful acquisition step for this discipline.</summary>
    public int SuccessfulProviders { get; init; }

    /// <summary>Distinct providers with an acquisition step (success or failure) for this discipline.</summary>
    public int AttemptedProviders { get; init; }
}

/// <summary>
/// The authoritative per-discipline evidence coverage for a run. Computed exactly
/// once from existing run facts (<see cref="ProviderExecutionReport"/> +
/// consolidated findings + requested disciplines) — never recomputed by exporters.
/// Order-independent: entries are emitted in <see cref="FindingCategory"/> order.
/// </summary>
public sealed record DisciplineCoverage
{
    public IReadOnlyList<DisciplineCoverageEntry> Entries { get; init; } = [];

    public static DisciplineCoverage From(
        ProviderExecutionReport? report,
        IReadOnlyList<Finding> consolidatedFindings,
        IReadOnlyList<FindingCategory> requestedDisciplines)
    {
        var entries = new List<DisciplineCoverageEntry>();

        foreach (var discipline in requestedDisciplines.OrderBy(d => d))
        {
            var disciplineRecords = report?.Records
                .Where(r => r.RequestedDiscipline == discipline)
                .ToList() ?? [];

            // "Providers" = distinct provider names with a step for the discipline.
            var attempted = disciplineRecords
                .Select(r => r.ProviderName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var successful = disciplineRecords
                .Where(r => r.Success)
                .Select(r => r.ProviderName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            // A timeout/failure is NOT successful evidence. Partial success still
            // counts as evidence (coverage requires ≥1 success, never 3/3).
            var status = successful == 0
                ? DisciplineCoverageStatus.NoEvidence
                : consolidatedFindings.Any(f => f.Category == discipline)
                    ? DisciplineCoverageStatus.CoveredWithFindings
                    : DisciplineCoverageStatus.CoveredNoFindings;

            entries.Add(new DisciplineCoverageEntry
            {
                Discipline = discipline.ToString(),
                Status = status,
                SuccessfulProviders = successful,
                AttemptedProviders = attempted
            });
        }

        return new DisciplineCoverage { Entries = entries };
    }
}