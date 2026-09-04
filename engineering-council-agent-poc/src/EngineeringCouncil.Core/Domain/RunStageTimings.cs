namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// Per-stage wall-clock timings for one run (Milestone 012). Purely operational
/// telemetry used by the evaluation framework to identify the slowest stages. It
/// lives on the diagnostic <see cref="AnalysisRun"/> only — it is NOT part of the
/// Engineering Review Package and never influences analysis or package generation.
/// </summary>
public sealed record RunStageTimings
{
    public TimeSpan Scan { get; init; }

    /// <summary>Total wall-clock of the acquisition stage (all provider steps).</summary>
    public TimeSpan Acquisition { get; init; }

    public TimeSpan Interpretation { get; init; }
    public TimeSpan Analysis { get; init; }
    public TimeSpan Reconciliation { get; init; }
    public TimeSpan CouncilSummary { get; init; }
    public TimeSpan PackageBuild { get; init; }
    public TimeSpan Persistence { get; init; }

    public TimeSpan Total { get; init; }

    /// <summary>The slowest named stage (excluding Total), for quick triage.</summary>
    public string SlowestStage
    {
        get
        {
            var stages = new (string Name, TimeSpan Duration)[]
            {
                ("Scan", Scan), ("Acquisition", Acquisition), ("Interpretation", Interpretation),
                ("Analysis", Analysis), ("Reconciliation", Reconciliation),
                ("CouncilSummary", CouncilSummary), ("PackageBuild", PackageBuild), ("Persistence", Persistence)
            };
            return stages.OrderByDescending(s => s.Duration).First().Name;
        }
    }
}
