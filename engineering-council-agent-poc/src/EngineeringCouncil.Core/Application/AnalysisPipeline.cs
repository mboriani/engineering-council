using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Application;

public sealed record AnalysisRequest
{
    public required string TargetPath { get; init; }

    /// <summary>Display name of the provider(s) used (e.g. "Mock", "Claude,Sonar").</summary>
    public string ProviderName { get; init; } = "mock";

    /// <summary>
    /// Disciplines to analyze for this run. Null ⇒ fall back to the configured
    /// <c>Evidence:Disciplines</c>; empty (after intersection) ⇒ all registered analyzers.
    /// </summary>
    public IReadOnlyList<FindingCategory>? Disciplines { get; init; }

    public ScanOptions ScanOptions { get; init; } = new();
}

public sealed record AnalysisResult
{
    public required AnalysisRun Run { get; init; }

    /// <summary>The Engineering Review Package — the primary deliverable of the run.</summary>
    public required EngineeringReviewPackage Package { get; init; }

    /// <summary>Absolute path of the folder where artifacts were written.</summary>
    public required string OutputDirectory { get; init; }
}

/// <summary>
/// End-to-end pipeline (Milestone 008 — discipline-aware acquisition):
///   1. scan (read-only)
///   2. PLAN acquisition (repository-wide + discipline-specific steps)
///   3. execute the plan step by step (context selection + provider), run-scoped telemetry
///   4. interpret evidence into normalized observations
///   5. analyze OVER OBSERVATIONS
///   6. merge → 7. council summary → 8. Engineering Review Package → 9. persist
///
/// Provider topology is decided by an explicit plan (not implicit loops), and
/// telemetry is collected in a per-run collector (no process-global singleton),
/// so concurrent runs never interleave.
/// </summary>
public sealed class AnalysisPipeline
{
    private readonly IRepositoryScanner _scanner;
    private readonly IEvidenceProviderFactory _providerFactory;
    private readonly IEvidenceAcquisitionPlanner _planner;
    private readonly IEvidenceAcquisitionExecutor _acquisitionExecutor;
    private readonly IEvidenceInterpretationPipeline _interpretation;
    private readonly AnalysisOrchestrator _orchestrator;
    private readonly IFindingReconciler _reconciler;
    private readonly ICouncilSummaryGenerator _summaryGenerator;
    private readonly IEngineeringReviewPackageBuilder _packageBuilder;
    private readonly EvidenceOptions _evidenceOptions;
    private readonly IAnalysisRunRepository _repository;
    private readonly ISemanticReconciliationReviewer? _semanticReviewer;
    private readonly SemanticReconciliationOptions _semanticReconciliationOptions;
    private readonly IRepositorySnapshotIdentityProvider? _repositorySnapshotIdentityProvider;

    public AnalysisPipeline(
        IRepositoryScanner scanner,
        IEvidenceProviderFactory providerFactory,
        IEvidenceAcquisitionPlanner planner,
        IEvidenceAcquisitionExecutor acquisitionExecutor,
        IEvidenceInterpretationPipeline interpretation,
        AnalysisOrchestrator orchestrator,
        IFindingReconciler reconciler,
        ICouncilSummaryGenerator summaryGenerator,
        IEngineeringReviewPackageBuilder packageBuilder,
        EvidenceOptions evidenceOptions,
        IAnalysisRunRepository repository,
        ISemanticReconciliationReviewer? semanticReconciliationReviewer = null,
        SemanticReconciliationOptions? semanticReconciliationOptions = null,
        IRepositorySnapshotIdentityProvider? repositorySnapshotIdentityProvider = null)
    {
        _scanner = scanner;
        _providerFactory = providerFactory;
        _planner = planner;
        _acquisitionExecutor = acquisitionExecutor;
        _interpretation = interpretation;
        _orchestrator = orchestrator;
        _reconciler = reconciler;
        _summaryGenerator = summaryGenerator;
        _packageBuilder = packageBuilder;
        _evidenceOptions = evidenceOptions;
        _repository = repository;
        _semanticReviewer = semanticReconciliationReviewer;
        _semanticReconciliationOptions = semanticReconciliationOptions ?? new SemanticReconciliationOptions();
        _repositorySnapshotIdentityProvider = repositorySnapshotIdentityProvider;
    }

    public async Task<AnalysisResult> RunAsync(
        AnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        // Fail fast (issue A6): a requested discipline with no registered analyzer
        // aborts the run BEFORE any repository scan, provider execution, or package
        // generation. Validated here (the shared entry point) so the CLI, API and any
        // other caller share the same rule; all unsupported disciplines are reported.
        // The effective set is `request ?? config`; an empty effective set (no
        // registered analyzers at all) can never produce an Excellent "no findings" run.
        var registeredDisciplines = _orchestrator.Disciplines;
        var effectiveDisciplines = request.Disciplines ?? _evidenceOptions.Disciplines;
        AnalyzerDisciplineValidator.EnsureSupported(effectiveDisciplines, registeredDisciplines);
        if (registeredDisciplines.Count == 0)
            throw new InvalidOperationException(
                "No analyzer agents are registered; the effective discipline set is empty and no review package can be produced.");

        var runId = AnalysisRunId.New();
        var target = Path.GetFullPath(request.TargetPath);

        var run = new AnalysisRun
        {
            RunId = runId,
            TargetPath = target,
            SolutionName = new DirectoryInfo(target).Name,
            Provider = request.ProviderName,
            Status = AnalysisRunStatus.Scanning,
            StartedAt = DateTimeOffset.UtcNow
        };

        EvidenceAcquisitionPlan? plan = null;
        // Records the executor OWNS and returns (no process-global state → run isolation).
        IReadOnlyList<ProviderExecutionRecord> records = [];

        // Operational stage timings (diagnostic only — never affect analysis or the package).
        var runStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stage = System.Diagnostics.Stopwatch.StartNew();
        TimeSpan scanTime = default, acquisitionTime = default, interpretationTime = default,
            analysisTime = default, reconciliationTime = default, summaryTime = default;

        try
        {
            // 1. scan
            var snapshot = await _scanner.ScanAsync(target, request.ScanOptions, cancellationToken)
                .ConfigureAwait(false);
            scanTime = stage.Elapsed;

            run = run with
            {
                SolutionName = snapshot.SolutionName,
                Branch = snapshot.Branch ?? "(unknown)",
                Commit = snapshot.Commit ?? "(unknown)",
                Projects = snapshot.ProjectFiles.Count,
                FilesScanned = snapshot.TotalFiles,
                Status = AnalysisRunStatus.Analyzing
            };

            var context = new AnalyzerContext { Snapshot = snapshot, RunId = runId };

            // 1b. M15.3C — capture the analyzed repository state BEFORE acquisition
            // begins (the start identity is authoritative and is never replaced).
            if (_repositorySnapshotIdentityProvider is { } identityProvider)
            {
                var startIdentity = await identityProvider
                    .CaptureAsync(snapshot, target, request.ScanOptions, cancellationToken)
                    .ConfigureAwait(false);
                run = run with { RepositoryIdentity = startIdentity };
            }

            // 2. plan acquisition (repository-wide + discipline-specific) from provider metadata
            var disciplines = SelectedDisciplines(request.Disciplines);
            var providers = ResolveConfiguredProviders();
            var configuration = new AnalysisRunConfiguration
            {
                RunId = runId,
                ProviderScopeOverrides = _evidenceOptions.ProviderScopes,
                ContextMaxFiles = _evidenceOptions.ContextMaxFiles,
                ContextMaxCharacters = _evidenceOptions.ContextMaxCharacters
            };
            plan = _planner.CreatePlan(configuration, snapshot, providers, disciplines);

            // 3. execute the plan (per-step context selection + provider) → owned telemetry
            stage.Restart();
            var acquisition = await _acquisitionExecutor
                .ExecuteAsync(plan, snapshot, cancellationToken).ConfigureAwait(false);
            acquisitionTime = stage.Elapsed;
            records = acquisition.Executions;

            // 3b. M15.3C — verify ONCE, at the end of acquisition, that the analyzed
            // state did not mutate mid-run. A detected mutation surfaces as a warning
            // (RepositoryChangedDuringRun) and NEVER fails the run, and the start
            // fingerprint is never replaced by the end state.
            if (run.RepositoryIdentity is { SnapshotFingerprint: { } startFingerprint }
                && _repositorySnapshotIdentityProvider is { } snapshotIdentityProvider)
            {
                var changedDuringRun = false;
                try
                {
                    changedDuringRun = await snapshotIdentityProvider
                        .ChangedDuringRunAsync(target, request.ScanOptions, startFingerprint, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw; // run cancellation always propagates
                }
                catch (Exception)
                {
                    // Identity verification is diagnostic: an unreadable end state
                    // keeps the start identity and reports no change.
                }
                run = run with
                {
                    RepositoryIdentity = run.RepositoryIdentity with { RepositoryChangedDuringRun = changedDuringRun }
                };
            }

            // Strict mode: any provider that could not execute fails the whole run.
            // Default (Continue) keeps the successful evidence and records the failures.
            if (_evidenceOptions.ProviderFailureMode == ProviderFailureMode.FailRun
                && records.Any(r => !r.Success))
            {
                var failures = records.Where(r => !r.Success)
                    .Select(r => $"{r.ProviderName}{(r.RequestedDiscipline is { } d ? $"/{d}" : "")}: {r.ErrorMessage}");
                throw new InvalidOperationException(
                    "Provider execution failed and Evidence:ProviderFailureMode is 'FailRun'. "
                    + string.Join(" | ", failures));
            }

            // 4. interpret evidence → observations (carry acquisition provenance)
            stage.Restart();
            var interpretation = await _interpretation.InterpretAsync(acquisition.Evidence, context, cancellationToken)
                .ConfigureAwait(false);
            interpretationTime = stage.Elapsed;

            // Enrich the owned records with observations-produced per acquisition step.
            var observationsByStep = interpretation.Observations
                .GroupBy(o => o.AcquisitionStepId)
                .ToDictionary(g => g.Key, g => g.Count());
            records = records
                .Select(r => observationsByStep.TryGetValue(r.StepId, out var count)
                    ? r with { ObservationsProduced = count }
                    : r)
                .ToList();

            // 5. analyze over observations → raw findings
            stage.Restart();
            var rawFindings = await _orchestrator.AnalyzeAsync(interpretation.Observations, context, cancellationToken)
                .ConfigureAwait(false);
            analysisTime = stage.Elapsed;

            // 6. deterministic multi-source reconciliation → consolidated findings
            stage.Restart();
            var reconciliation = _reconciler.Reconcile(rawFindings, interpretation.Observations, cancellationToken);
            var consolidated = reconciliation.ConsolidatedFindings;
            reconciliationTime = stage.Elapsed;

            run = run with
            {
                AcquisitionPlan = plan,
                RequestedDisciplines = disciplines,
                Evidence = acquisition.Evidence,
                Observations = interpretation.Observations,
                ObservationInterpretationSummary = interpretation.Summary,
                RawFindings = rawFindings,
                Findings = consolidated,
                ReconciliationSummary = reconciliation.Summary,
                ReconciliationGroups = reconciliation.Groups,
                ProviderExecution = ProviderExecutionReport.FromRecords(records, plan),
                Status = AnalysisRunStatus.Completed,
                CompletedAt = DateTimeOffset.UtcNow
            };

            // 7. council summary
            stage.Restart();
            var summary = await _summaryGenerator.GenerateAsync(run, consolidated, cancellationToken)
                .ConfigureAwait(false);
            summaryTime = stage.Elapsed;
            run = run with { Summary = summary };

            // 7b. internal provider comparison (M12.2): a deterministic projection of
            // the run's own data — no provider calls, no rescan, no re-interpretation.
            // Null unless ≥2 comparable LLM executions exist; never part of the package.
            run = run with { ProviderComparison = ProviderComparisonBuilder.Build(run) };

            // 7c. internal calibration diagnostics (M12.3): a deterministic projection
            // of the comparison + reconciled findings — Comparable disciplines only;
            // NonComparable/Incomplete are limitations. No LLM calls, no semantic
            // matching, no ranking/scoring; never part of the package.
            run = run with { CalibrationDiagnostics = CalibrationDiagnosticsBuilder.Build(run) };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            run = run with
            {
                Status = AnalysisRunStatus.Cancelled,
                CompletedAt = DateTimeOffset.UtcNow
            };
            throw;
        }
        catch (Exception ex)
        {
            run = run with
            {
                Status = AnalysisRunStatus.Failed,
                Error = ex.ToString(),
                AcquisitionPlan = plan,
                ProviderExecution = ProviderExecutionReport.FromRecords(records, plan),
                CompletedAt = DateTimeOffset.UtcNow
            };
        }

        // 8. assemble the Engineering Review Package (the primary deliverable)
        stage.Restart();
        var package = _packageBuilder.Build(run);
        var packageBuildTime = stage.Elapsed;

        // 8b. OPTIONAL targeted semantic reconciliation (Milestone 014.4): reviews
        // ONLY consolidated findings whose Council assessment flags an ObservationType
        // disagreement (Decision B, M14.3). Disabled by default — offline/default
        // behavior stays identical to M14.3. A reviewer or enrichment failure must
        // NEVER destroy an otherwise valid, already-built Council package.
        if (_semanticReconciliationOptions.Enabled && _semanticReviewer is not null)
        {
            try
            {
                var reviewed = await SemanticReconciliationBuilder
                    .ApplyAsync(package.Findings, run.Observations, _semanticReviewer, cancellationToken)
                    .ConfigureAwait(false);
                package = package with { Findings = reviewed };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // run cancellation must still propagate
            }
            catch (Exception)
            {
                // Semantic reconciliation is optional enrichment; package.Findings simply
                // keeps its pre-review values (no SemanticReview stamped on any finding).
            }
        }

        // 9. persist
        stage.Restart();
        var outputDir = await _repository.SaveAsync(run, package, cancellationToken).ConfigureAwait(false);
        var persistenceTime = stage.Elapsed;
        runStopwatch.Stop();

        // Operational telemetry for the evaluation framework (diagnostic only; the
        // package was already built above and is unaffected by these numbers).
        run = run with
        {
            StageTimings = new RunStageTimings
            {
                Scan = scanTime,
                Acquisition = acquisitionTime,
                Interpretation = interpretationTime,
                Analysis = analysisTime,
                Reconciliation = reconciliationTime,
                CouncilSummary = summaryTime,
                PackageBuild = packageBuildTime,
                Persistence = persistenceTime,
                Total = runStopwatch.Elapsed
            }
        };

        return new AnalysisResult { Run = run, Package = package, OutputDirectory = outputDir };
    }

    /// <summary>
    /// Resolves the configured provider names to registered providers (in configured
    /// order). Unknown names are skipped — the planner reads metadata off the resolved
    /// providers. Availability is enforced later by the executor per step.
    /// </summary>
    private IReadOnlyCollection<IEvidenceProvider> ResolveConfiguredProviders()
    {
        var resolved = new List<IEvidenceProvider>();
        foreach (var name in _evidenceOptions.Providers)
            if (_providerFactory.TryGetProvider(name, out var provider))
                resolved.Add(provider);
        return resolved;
    }

    /// <summary>
    /// Selected disciplines intersected with registered analyzers; default = all
    /// registered. A per-request selection (from the API) wins over configuration.
    /// </summary>
    private IReadOnlyList<FindingCategory> SelectedDisciplines(IReadOnlyList<FindingCategory>? requested)
    {
        var registered = _orchestrator.Disciplines;
        var selected = requested ?? _evidenceOptions.Disciplines;
        return selected.Count == 0
            ? registered
            : registered.Where(selected.Contains).ToList();
    }
}
