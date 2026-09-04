namespace EngineeringCouncil.Infrastructure.Evaluation;

/// <summary>
/// One repository in the evaluation dataset, described by its <c>evaluation.json</c>
/// manifest. Each entry documents WHY the repository exists so results are readable.
/// </summary>
public sealed record EvaluationRepository
{
    public required string Name { get; init; }
    public required string Path { get; init; }

    /// <summary>Why this repository is in the dataset (what it is meant to exercise).</summary>
    public string Purpose { get; init; } = string.Empty;

    /// <summary>Disciplines the repository is expected to exercise. Informational only.</summary>
    public IReadOnlyList<string> ExpectedSignals { get; init; } = [];

    /// <summary>Optional SARIF file (relative to the repository) to import for this case.</summary>
    public string? Sarif { get; init; }
}

/// <summary>Operational stage timings for one evaluation run (milliseconds).</summary>
public sealed record OperationalMetrics
{
    public double TotalMs { get; init; }
    public double ScanMs { get; init; }
    public double AcquisitionMs { get; init; }
    public double InterpretationMs { get; init; }
    public double AnalysisMs { get; init; }
    public double ReconciliationMs { get; init; }
    public double PackageBuildMs { get; init; }
    public double PersistenceMs { get; init; }
    public string SlowestStage { get; init; } = string.Empty;

    public double ProviderLatencyAverageMs { get; init; }
    public double ProviderLatencyMaxMs { get; init; }
}

/// <summary>How much repository context each provider step actually received.</summary>
public sealed record ContextMetrics
{
    public int RepositoryFiles { get; init; }
    public double SelectedFilesAverage { get; init; }
    public int SelectedFilesMax { get; init; }
    public double SelectedCharactersAverage { get; init; }
    public int SelectedCharactersMax { get; init; }

    /// <summary>Repository files NOT sent for the average step (scanned − selected).</summary>
    public double OmittedFilesAverage { get; init; }

    /// <summary>Average characters actually sent as a prompt (LLM steps that reported it).</summary>
    public double PromptCharactersAverage { get; init; }

    /// <summary>Responses the provider reported as cut off by the output-token limit.</summary>
    public int ResponseTruncations { get; init; }

    /// <summary>
    /// Redundant file sends across steps: total files selected over all steps minus the
    /// repository's file count. Positive values mean discipline contexts re-send the same
    /// files (a selector-tuning signal — the selector is NOT changed in this milestone).
    /// </summary>
    public int RepeatedFileSelections { get; init; }
}

/// <summary>Structured-output quality of the model-backed sources.</summary>
public sealed record PromptCalibrationMetrics
{
    public int LlmExecutions { get; init; }
    public int SchemaValidationFailures { get; init; }
    public int RepairAttempts { get; init; }
    public int RepairSuccesses { get; init; }
    public double RepairSuccessRate => RepairAttempts == 0 ? 0 : Math.Round((double)RepairSuccesses / RepairAttempts, 3);
    public double InvalidResponseRate => LlmExecutions == 0 ? 0 : Math.Round((double)SchemaValidationFailures / LlmExecutions, 3);

    /// <summary>File references claimed by a source that do not exist in the snapshot.</summary>
    public int HallucinatedFileReferences { get; init; }

    /// <summary>Observations with no file reference at all.</summary>
    public int ObservationsMissingLocation { get; init; }

    /// <summary>Observations whose type is not a known <c>ObservationTypes</c> value.</summary>
    public int UnsupportedObservationTypes { get; init; }

    public int DisciplineMismatches { get; init; }
    public int UnsupportedEvidence { get; init; }
    public int InterpreterFailures { get; init; }
}

/// <summary>What reconciliation did to the raw findings.</summary>
public sealed record ReconciliationMetrics
{
    public int RawFindings { get; init; }
    public int ConsolidatedFindings { get; init; }
    public double DuplicateReductionPercent => RawFindings == 0
        ? 0 : Math.Round(100.0 * (RawFindings - ConsolidatedFindings) / RawFindings, 1);
    public int MultiProviderFindings { get; init; }
    public int SingleProviderFindings { get; init; }
    public int Contradictions { get; init; }

    /// <summary>Findings keyed by their provider-agreement count ("1", "2", …).</summary>
    public IReadOnlyDictionary<string, int> AgreementDistribution { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// Consolidated findings that merged sources with NO shared file — flagged for manual
    /// false-merge review. Not an error, a review queue.
    /// </summary>
    public int FalseMergeCandidates { get; init; }
}

/// <summary>Informational quality distributions. They never influence package generation.</summary>
public sealed record QualityMetrics
{
    public int Observations { get; init; }
    public IReadOnlyDictionary<string, int> ObservationsByDiscipline { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> ObservationsByProvider { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> FindingsByDiscipline { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> FindingsByProvider { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> FindingsBySeverity { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> FindingsByConfidence { get; init; } = new Dictionary<string, int>();

    /// <summary>Share of consolidated findings corroborated by ≥2 providers.</summary>
    public double ProviderAgreementPercent { get; init; }
}

/// <summary>Provider execution + usage totals. Cost is only present when pricing is configured.</summary>
public sealed record ProviderUsageMetrics
{
    public int Executions { get; init; }
    public int Failures { get; init; }
    public int EvidenceCount { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public int? TotalTokens { get; init; }
    public decimal? EstimatedCost { get; init; }
    public string Currency { get; init; } = "USD";
    public bool CostIsEstimated => EstimatedCost is not null;

    /// <summary>Why cost is absent, when it is (e.g. no pricing configured / no usage reported).</summary>
    public string? CostUnavailableReason { get; init; }

    public IReadOnlyDictionary<string, string> ModelsUsed { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> FailureReasons { get; init; } = [];
}

/// <summary>Checks that the external integration artifact is still well-formed.</summary>
public sealed record PackageValidationMetrics
{
    public string SchemaVersion { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public double SerializationMs { get; init; }
    public bool DeterministicSerialization { get; init; }
    public bool RequiredRootPropertiesPresent { get; init; }
    public IReadOnlyList<string> MissingRootProperties { get; init; } = [];
    public bool ContainsNoRuntimeTypeLeaks { get; init; }
}

/// <summary>The complete measurement of ONE repository × ONE provider selection.</summary>
public sealed record EvaluationRun
{
    public required string Repository { get; init; }
    public string RepositoryPurpose { get; init; } = string.Empty;
    public required IReadOnlyList<string> Providers { get; init; }
    public required IReadOnlyList<string> Disciplines { get; init; }
    public string RunId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Error { get; init; }
    public int RepositoryFiles { get; init; }
    public int Projects { get; init; }

    public OperationalMetrics Operational { get; init; } = new();
    public ContextMetrics Context { get; init; } = new();
    public PromptCalibrationMetrics PromptCalibration { get; init; } = new();
    public ReconciliationMetrics Reconciliation { get; init; } = new();
    public QualityMetrics Quality { get; init; } = new();
    public ProviderUsageMetrics Usage { get; init; } = new();
    public PackageValidationMetrics Package { get; init; } = new();

    public string OutputDirectory { get; init; } = string.Empty;
}

/// <summary>The aggregate evaluation: every run, plus the provider comparison view.</summary>
public sealed record EvaluationReport
{
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public required IReadOnlyList<EvaluationRun> Runs { get; init; }
    public IReadOnlyList<string> RepositoriesEvaluated { get; init; } = [];
    public IReadOnlyList<string> ProvidersEvaluated { get; init; } = [];

    /// <summary>Providers that were requested but could not execute (e.g. no credentials).</summary>
    public IReadOnlyList<string> ProvidersUnavailable { get; init; } = [];
}
