namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// Quantitative metrics over a run's consolidated findings and provider
/// execution. Purely derived (rule-based); part of the Engineering Review Package.
/// </summary>
public sealed record EngineeringMetrics
{
    public int TotalFindings { get; init; }
    public int Critical { get; init; }
    public int High { get; init; }
    public int Medium { get; init; }
    public int Low { get; init; }
    public int Info { get; init; }

    public int MergedFindings { get; init; }

    /// <summary>Total normalized observations produced by interpretation.</summary>
    public int TotalObservations { get; init; }

    /// <summary>Distinct providers that produced at least one piece of evidence.</summary>
    public int EvidenceSources { get; init; }

    /// <summary>Providers whose executions ALL succeeded (disjoint from Partial/Failed).</summary>
    public int SuccessfulProviders { get; init; }

    /// <summary>Providers with at least one successful AND one failed execution (disjoint from Successful/Failed).</summary>
    public int PartialProviders { get; init; }

    /// <summary>Providers whose executions ALL failed (disjoint from Successful/Partial).</summary>
    public int FailedProviders { get; init; }

    /// <summary>Observations no analyzer claims (discipline is <see cref="FindingCategory.Unknown"/>).</summary>
    public int UnclaimedObservations { get; init; }

    public int RepositoryFiles { get; init; }
    public int Projects { get; init; }

    public TimeSpan AnalysisDuration { get; init; }

    /// <summary>Average finding confidence on a 0..1 scale (Low=0, Medium=0.5, High=1).</summary>
    public double AverageConfidence { get; init; }

    /// <summary>Consolidated finding counts keyed by discipline (category name).</summary>
    public IReadOnlyDictionary<string, int> CoverageByDiscipline { get; init; }
        = new Dictionary<string, int>();

    public static EngineeringMetrics From(AnalysisRun run)
    {
        var findings = run.Findings;
        var bySeverity = findings.GroupBy(f => f.Severity).ToDictionary(g => g.Key, g => g.Count());
        var pe = run.ProviderExecution;

        return new EngineeringMetrics
        {
            TotalFindings = findings.Count,
            Critical = bySeverity.GetValueOrDefault(FindingSeverity.Critical),
            High = bySeverity.GetValueOrDefault(FindingSeverity.High),
            Medium = bySeverity.GetValueOrDefault(FindingSeverity.Medium),
            Low = bySeverity.GetValueOrDefault(FindingSeverity.Low),
            Info = bySeverity.GetValueOrDefault(FindingSeverity.Info),
            MergedFindings = findings.Count(f => f.Status == FindingStatus.Merged),
            TotalObservations = run.Observations.Count,
            EvidenceSources = pe?.ByProvider.Count(p => p.EvidenceCount > 0) ?? 0,
            // Provider states are mutually exclusive by execution outcome:
            //   Successful = all executions succeeded (≥1)
            //   Partial    = at least one success AND one failure
            //   Failed     = all executions failed (≥1)
            // A provider with zero executions is never Successful/Failed/Partial.
            SuccessfulProviders = pe?.ByProvider.Count(p => p.Executions > 0 && p.Failures == 0) ?? 0,
            PartialProviders = pe?.ByProvider.Count(p => p.Failures > 0 && p.Failures < p.Executions) ?? 0,
            FailedProviders = pe?.ByProvider.Count(p => p.Executions > 0 && p.Failures == p.Executions) ?? 0,
            UnclaimedObservations = run.Observations.Count(o => o.Discipline == FindingCategory.Unknown),
            RepositoryFiles = run.FilesScanned,
            Projects = run.Projects,
            AnalysisDuration = (run.CompletedAt ?? run.StartedAt) - run.StartedAt,
            AverageConfidence = AverageConfidenceOf(findings),
            CoverageByDiscipline = findings
                .GroupBy(f => f.Category.ToString())
                .OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }

    private static double AverageConfidenceOf(IReadOnlyList<Finding> findings)
    {
        if (findings.Count == 0) return 0;
        // Low=0, Medium=1, High=2 → normalise to 0..1.
        var avgOrdinal = findings.Average(f => (int)f.Confidence);
        return Math.Round(avgOrdinal / 2.0, 3);
    }
}
