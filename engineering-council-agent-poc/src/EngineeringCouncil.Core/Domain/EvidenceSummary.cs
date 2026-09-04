namespace EngineeringCouncil.Core.Domain;

/// <summary>Health of a single provider across a run.</summary>
public sealed record ProviderHealthEntry
{
    public required string ProviderName { get; init; }
    public int Executions { get; init; }
    public int Failures { get; init; }
    public int EvidenceCount { get; init; }
    public TimeSpan TotalDuration { get; init; }

    /// <summary>"Healthy" (no failures), "Degraded" (some), or "Failed" (no evidence).</summary>
    public string Status =>
        EvidenceCount == 0 ? "Failed"
        : Failures > 0 ? "Degraded"
        : "Healthy";
}

/// <summary>
/// Summary of the evidence collection for a run: which sources were used, how
/// much evidence they produced, and how healthy each provider was. Derived from
/// the <see cref="ProviderExecutionReport"/>.
/// </summary>
public sealed record EvidenceSummary
{
    public IReadOnlyList<string> EvidenceSourcesUsed { get; init; } = [];
    public int EvidenceCount { get; init; }
    public int ProviderFailures { get; init; }
    public IReadOnlyList<ProviderHealthEntry> ProviderHealth { get; init; } = [];
    public IReadOnlyDictionary<string, string> ExecutionTimes { get; init; }
        = new Dictionary<string, string>();

    // ── Observation interpretation (Milestone 007) ────────────────────────────
    public int TotalObservations { get; init; }
    public int UnsupportedEvidence { get; init; }
    public int InterpreterFailures { get; init; }
    public IReadOnlyDictionary<string, int> ObservationsByProvider { get; init; }
        = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> ObservationsByDiscipline { get; init; }
        = new Dictionary<string, int>();

    /// <summary>Observations no analyzer claims (discipline <see cref="FindingCategory.Unknown"/>).</summary>
    public int UnclaimedObservations { get; init; }

    public static EvidenceSummary From(
        ProviderExecutionReport? report,
        ObservationInterpretationSummary? interpretation = null)
    {
        if (report is null && interpretation is null) return new EvidenceSummary();

        var obs = interpretation ?? new ObservationInterpretationSummary();
        if (report is null)
            return new EvidenceSummary
            {
                TotalObservations = obs.ObservationsProduced,
                UnsupportedEvidence = obs.EvidenceUnsupported,
                InterpreterFailures = obs.InterpreterFailures,
                ObservationsByProvider = obs.ObservationsByProvider,
                ObservationsByDiscipline = obs.ObservationsByDiscipline,
                UnclaimedObservations = obs.UnclaimedObservationCount
            };

        var health = report.ByProvider
            .Select(p => new ProviderHealthEntry
            {
                ProviderName = p.ProviderName,
                Executions = p.Executions,
                Failures = p.Failures,
                EvidenceCount = p.EvidenceCount,
                TotalDuration = p.TotalDuration
            })
            .ToList();

        return new EvidenceSummary
        {
            EvidenceSourcesUsed = health.Where(h => h.EvidenceCount > 0).Select(h => h.ProviderName).ToList(),
            EvidenceCount = report.EvidenceCount,
            ProviderFailures = report.Failures,
            ProviderHealth = health,
            ExecutionTimes = health.ToDictionary(h => h.ProviderName, h => h.TotalDuration.ToString()),
            TotalObservations = obs.ObservationsProduced,
            UnsupportedEvidence = obs.EvidenceUnsupported,
            InterpreterFailures = obs.InterpreterFailures,
            ObservationsByProvider = obs.ObservationsByProvider,
            ObservationsByDiscipline = obs.ObservationsByDiscipline,
            UnclaimedObservations = obs.UnclaimedObservationCount
        };
    }
}
