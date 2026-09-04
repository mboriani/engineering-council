namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// One execution of the engineering council over a target repository.
/// Aggregates the findings and enough metadata for the dashboard to render
/// a run and for the system to be re-run reproducibly.
/// </summary>
public sealed record AnalysisRun
{
    /// <summary>Unique run identifier; also used as the output folder name.</summary>
    public required string RunId { get; init; }

    /// <summary>Absolute path of the target repository/solution that was scanned.</summary>
    public required string TargetPath { get; init; }

    /// <summary>Friendly name of the solution/repo (defaults to folder name).</summary>
    public string SolutionName { get; init; } = string.Empty;

    /// <summary>Git branch of the target, when it is a git repository.</summary>
    public string Branch { get; init; } = "(unknown)";

    /// <summary>Git commit of the target, when it is a git repository.</summary>
    public string Commit { get; init; } = "(unknown)";

    /// <summary>
    /// Deterministic identity of the analyzed repository working state (Milestone
    /// 015.3C): VCS facts plus the snapshot fingerprint captured BEFORE acquisition
    /// began, and whether a mid-run mutation was detected. Null when a pre-M15.3C
    /// caller constructs a run without identity. Never contains source/secrets.
    /// </summary>
    public RepositorySnapshotIdentity? RepositoryIdentity { get; init; }

    /// <summary>Number of .csproj projects discovered.</summary>
    public int Projects { get; init; }

    public AnalysisRunStatus Status { get; init; } = AnalysisRunStatus.Pending;

    /// <summary>Provider used to run the analysis, e.g. "mock" or "openai:gpt-4o".</summary>
    public string Provider { get; init; } = "mock";

    /// <summary>Number of files considered after applying ignore rules.</summary>
    public int FilesScanned { get; init; }

    /// <summary>Raw evidence collected by the providers (pre-interpretation).</summary>
    public IReadOnlyList<Evidence> Evidence { get; init; } = [];

    /// <summary>Normalized observations produced by interpreting the evidence.</summary>
    public IReadOnlyList<EngineeringObservation> Observations { get; init; } = [];

    /// <summary>Telemetry for the evidence-interpretation stage.</summary>
    public ObservationInterpretationSummary? ObservationInterpretationSummary { get; init; }

    /// <summary>The raw findings emitted by the analyzers, before reconciliation.</summary>
    public IReadOnlyList<Finding> RawFindings { get; init; } = [];

    /// <summary>
    /// The consolidated findings after deterministic multi-source reconciliation
    /// (Milestone 010). This is the set the Engineering Review Package and
    /// <c>findings.json</c> expose; <see cref="RawFindings"/> stays available for
    /// <c>raw-findings.json</c>.
    /// </summary>
    public IReadOnlyList<Finding> Findings { get; init; } = [];

    /// <summary>Provider-neutral summary of the reconciliation pass (Milestone 010).</summary>
    public ReconciliationSummary? ReconciliationSummary { get; init; }

    /// <summary>Reconciliation traceability groups (internal; ride in the package appendix).</summary>
    public IReadOnlyList<ReconciliationGroup> ReconciliationGroups { get; init; } = [];

    /// <summary>Per-stage operational timings (diagnostic only; never part of the package).</summary>
    public RunStageTimings? StageTimings { get; init; }

    /// <summary>Council-level executive summary over the consolidated findings.</summary>
    public CouncilSummary? Summary { get; init; }

    /// <summary>The evidence acquisition plan executed for this run.</summary>
    public EvidenceAcquisitionPlan? AcquisitionPlan { get; init; }

    /// <summary>Disciplines requested for this run (empty = all registered analyzers).</summary>
    public IReadOnlyList<FindingCategory> RequestedDisciplines { get; init; } = [];

    /// <summary>Report of the multi-provider evidence collection for this run.</summary>
    public ProviderExecutionReport? ProviderExecution { get; init; }

    /// <summary>
    /// Internal provider-neutral comparison of the LLM providers executed in this
    /// run (Milestone 012.2); null when fewer than two comparable LLM executions
    /// exist. Diagnostic only — never part of the external package contract.
    /// </summary>
    public ProviderComparisonReport? ProviderComparison { get; init; }

    /// <summary>
    /// Internal finding-calibration diagnostics (Milestone 012.3); null when no
    /// provider comparison exists. Built from the comparison + reconciled findings —
    /// no LLM calls, no reinterpretation, no semantic matching. Diagnostic only —
    /// never part of the external package contract.
    /// </summary>
    public CalibrationDiagnosticsReport? CalibrationDiagnostics { get; init; }

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Populated when <see cref="Status"/> is <see cref="AnalysisRunStatus.Failed"/>.</summary>
    public string? Error { get; init; }

    public AnalysisRun With(
        AnalysisRunStatus? status = null,
        int? filesScanned = null,
        IReadOnlyList<Finding>? findings = null,
        DateTimeOffset? completedAt = null,
        string? error = null,
        string? provider = null)
        => this with
        {
            Status = status ?? Status,
            FilesScanned = filesScanned ?? FilesScanned,
            Findings = findings ?? Findings,
            CompletedAt = completedAt ?? CompletedAt,
            Error = error ?? Error,
            Provider = provider ?? Provider
        };
}
