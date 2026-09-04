using System.Diagnostics;
using System.Runtime.ExceptionServices;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Analysis;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringCouncil.Infrastructure.Acquisition;

// This namespace has sibling ".Evidence"/".Interpretation" namespaces that shadow
// the domain Evidence type, so it is referenced as Core.Domain.Evidence here.

/// <summary>
/// Executes an <see cref="EvidenceAcquisitionPlan"/> step by step: selects the
/// context for the step's discipline, builds the request, calls the provider,
/// stamps acquisition provenance onto the evidence, and OWNS the execution
/// telemetry — everything is returned in an <see cref="EvidenceAcquisitionResult"/>,
/// with no process-global state, so two concurrent runs never interleave. Each
/// step is isolated: an unavailable provider, an exception, or a timeout becomes a
/// failed <see cref="Core.Domain.Evidence"/> and the remaining steps still run.
///
/// Milestone 015.1 — independent steps run concurrently up to
/// <see cref="EvidenceOptions.MaxConcurrency"/> (bounded via <see cref="SemaphoreSlim"/>,
/// never one unbounded Task per step), and the final Evidence/records are always
/// aggregated in plan order, so parallel execution is a pure execution concern and
/// never makes the artifacts nondeterministic. <c>1</c> preserves strictly
/// sequential execution.
/// </summary>
public sealed class EvidenceAcquisitionExecutor : IEvidenceAcquisitionExecutor
{
    private readonly IEvidenceProviderFactory _factory;
    private readonly IAnalysisContextSelector _contextSelector;
    private readonly EvidenceOptions _options;
    private readonly ContextContentPolicy _contentPolicy;
    private readonly ILogger<EvidenceAcquisitionExecutor> _logger;

    public EvidenceAcquisitionExecutor(
        IEvidenceProviderFactory factory,
        IAnalysisContextSelector contextSelector,
        EvidenceOptions options,
        ILogger<EvidenceAcquisitionExecutor>? logger = null,
        ContextContentPolicy? contentPolicy = null)
    {
        _factory = factory;
        _contextSelector = contextSelector;
        _options = options;
        _contentPolicy = contentPolicy ?? new ContextContentPolicy();
        _logger = logger ?? NullLogger<EvidenceAcquisitionExecutor>.Instance;
    }

    public async Task<EvidenceAcquisitionResult> ExecuteAsync(
        EvidenceAcquisitionPlan plan,
        RepositorySnapshot repository,
        CancellationToken cancellationToken = default)
    {
        // Milestone 015.1 — bounded parallel execution of INDEPENDENT acquisition
        // steps. Steps run concurrently up to MaxConcurrency (SemaphoreSlim; no
        // custom scheduler). Each step keeps its own provider, request, timeout,
        // cancellation and per-step locals — one step never touches another's state.
        // Results are aggregated strictly in plan order AFTER every task completes,
        // so completion order never leaks into Evidence, records, JSON or Markdown.
        //
        // Failure isolation is unchanged: ExecuteStepAsync converts provider problems
        // into a failed Evidence + record (Continue semantics). Cancellation semantics
        // are unchanged: a run cancellation throws (never a Failed step), queued steps
        // do not start, and provider timeout stays distinct from run cancellation.
        var maxConcurrency = Math.Max(1, _options.MaxConcurrency);
        var outcomes = new StepOutcome[plan.Steps.Count];

        if (plan.Steps.Count > 0)
        {
            using var semaphore = new SemaphoreSlim(maxConcurrency);

            var tasks = plan.Steps.Select(async (step, index) =>
            {
                // Wait OUTSIDE the try/finally so a cancelled waiter never releases a
                // permit it did not acquire (SemaphoreSlim.Release on the queue would throw).
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var (items, record) = await ExecuteStepAsync(plan.RunId, step, repository, cancellationToken)
                        .ConfigureAwait(false);
                    outcomes[index] = StepOutcome.Of(items, record);
                }
                catch (OperationCanceledException ex)
                {
                    // Never converted into a failed Evidence; propagated below. If the
                    // RUN token is cancelled this surfaces as run cancellation; otherwise
                    // (a provider-internal token) it is rethrown exactly as before.
                    outcomes[index] = StepOutcome.Of(ex);
                }
                catch (Exception ex)
                {
                    // Unexpected leakage from a step (ExecuteStepAsync normally absorbs
                    // failures) — captured so it can be rethrown deterministically.
                    outcomes[index] = StepOutcome.Of(ex);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            // Wait for EVERY step (success, failure or cancellation) before aggregating.
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        // Deterministic propagation: run cancellation wins, then the first step
        // failure in plan order — never completion order.
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var outcome in outcomes)
        {
            if (outcome.Exception is { } ex)
                ExceptionDispatchInfo.Capture(ex).Throw();
        }

        var evidence = new List<Core.Domain.Evidence>(plan.Steps.Count);
        var records = new List<ProviderExecutionRecord>(plan.Steps.Count);
        foreach (var outcome in outcomes)
        {
            evidence.AddRange(outcome.Items);
            if (outcome.Record is { } record)
                records.Add(record);
        }

        return new EvidenceAcquisitionResult
        {
            Evidence = evidence,
            Executions = records,
            Plan = plan
        };
    }

    private async Task<(IReadOnlyList<Core.Domain.Evidence>, ProviderExecutionRecord)> ExecuteStepAsync(
        string runId, EvidenceAcquisitionStep step, RepositorySnapshot repository, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!_factory.TryGetProvider(step.ProviderName, out var provider))
        {
            var unregisteredSelection = _contextSelector.Select(repository, step.Discipline, cancellationToken);
            var unregisteredFingerprint = ContextFingerprint.Compute(unregisteredSelection, _contentPolicy);
            return Failure(runId, step, unregisteredSelection, $"Provider '{step.ProviderName}' is not registered.", stopwatch.Elapsed, contextFingerprint: unregisteredFingerprint);
        }

        // Agentic sources explore the repository themselves: the council does NOT
        // bundle a controlled context and does NOT fabricate a context fingerprint
        // (it never saw the agent's effective context). Decision is metadata-driven
        // (ProviderType), never by provider name.
        var isAgentic = provider.Metadata.ProviderType == EvidenceProviderType.Agentic;
        var selection = isAgentic
            ? AgenticSelection(repository)
            : _contextSelector.Select(repository, step.Discipline, cancellationToken);
        var fingerprint = isAgentic
            ? string.Empty
            : ContextFingerprint.Compute(selection, _contentPolicy);
        var requestRepository = isAgentic ? WithoutContents(repository) : repository;

        if (!provider.IsAvailable)
            // Secret-safe, actionable reason (e.g. which environment variable is missing).
            return Failure(runId, step, selection,
                provider.UnavailableReason ?? $"Provider '{step.ProviderName}' is not available.",
                stopwatch.Elapsed, provider, contextFingerprint: fingerprint);

        var request = new EvidenceRequest
        {
            RunId = runId,
            RepositorySnapshot = requestRepository,
            Scope = step.Scope,
            Discipline = step.Discipline,
            Instructions = DisciplinePrompts.BuildInstructions(step.Scope, step.Discipline),
            ContextSelection = selection,
            ProviderNames = [step.ProviderName],
            CorrelationId = step.CorrelationId
        };

        // Per-step timeout (M15.2B): the provider's OWN configured timeout wins;
        // Evidence:Execution:ProviderTimeout is the fallback/default. Timeouts are
        // per acquisition step — never one shared Council timer — and run
        // cancellation stays independent (see the catch below).
        //
        // M15.3B bounded retry: a retry-eligible failure (Timeout ONLY) gets at most
        // ONE retry (Evidence:Execution:MaxAttempts, default 2). The retry belongs to
        // the SAME logical step — same provider, request, effective timeout and
        // concurrency permit — and never runs a third attempt. Cancellation,
        // authentication/configuration, schema validation and unexpected errors are
        // never retried. Telemetry: AttemptCount, RetryCount (step-level retries plus
        // the final attempt's provider-internal retries), RetryExhausted.
        var stepTimeout = provider.Metadata.Timeout ?? _options.ProviderTimeout;
        var maxAttempts = Math.Clamp(_options.MaxAttempts, 1, EvidenceOptions.MaxAttemptsLimit);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(stepTimeout);

            try
            {
                var collected = await provider.CollectAsync(request, timeoutCts.Token).ConfigureAwait(false);
                stopwatch.Stop();

                // Stamp acquisition provenance onto every evidence item the step produced.
                var stamped = collected
                    .Select(item => item with
                    {
                        Success = true,
                        Duration = stopwatch.Elapsed,
                        AcquisitionScope = step.Scope,
                        RequestedDiscipline = step.Discipline,
                        CorrelationId = step.CorrelationId,
                        AcquisitionStepId = step.StepId,
                        ContextFileCount = selection.SelectedFileCount,
                        ContextCharacterCount = (int)selection.EstimatedContentSize,
                        ContextSelectionStrategy = selection.Strategy,
                        ContextFingerprint = fingerprint,
                        ProviderId = string.IsNullOrEmpty(item.ProviderId) ? provider.Metadata.Name.ToLowerInvariant() : item.ProviderId,
                        AttemptCount = attempt,
                        RetryCount = item.RetryCount + (attempt - 1),
                        RetryExhausted = false
                    })
                    .ToList();

                var record = Record(runId, step, provider, selection, success: true, stopwatch.Elapsed,
                    evidenceCount: stamped.Count,
                    tokens: Sum(stamped.Select(e => e.TokensUsed)),
                    cost: Sum(stamped.Select(e => e.CostEstimate)),
                    metrics: collected.FirstOrDefault(),
                    error: null,
                    attemptCount: attempt,
                    stepRetries: attempt - 1,
                    retryExhausted: false,
                    contextFingerprint: fingerprint);

                return (stamped, record);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                if (attempt < maxAttempts)
                {
                    _logger.LogWarning("Acquisition step {Step} timed out after {Timeout}; retrying (attempt {Attempt}/{MaxAttempts})",
                        step.StepId, stepTimeout, attempt, maxAttempts);
                    continue;
                }

                stopwatch.Stop();
                _logger.LogWarning("Acquisition step {Step} timed out after {Timeout}", step.StepId, stepTimeout);
                return Failure(runId, step, selection, $"Timed out after {stepTimeout}.", stopwatch.Elapsed, provider,
                    errorCategory: LlmErrorCategory.Timeout.ToString(),
                    attemptCount: attempt, stepRetries: attempt - 1, retryExhausted: true,
                    contextFingerprint: fingerprint);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (LlmProviderException ex)
            {
                if (ex.Category == LlmErrorCategory.Timeout && attempt < maxAttempts)
                {
                    _logger.LogWarning(ex, "Acquisition step {Step} timed out; retrying (attempt {Attempt}/{MaxAttempts})",
                        step.StepId, attempt, maxAttempts);
                    continue;
                }

                stopwatch.Stop();
                _logger.LogWarning(ex, "Acquisition step {Step} failed", step.StepId);
                return Failure(runId, step, selection, ex.Message, stopwatch.Elapsed, provider,
                    errorCategory: ex.Category.ToString(),
                    retryCount: ex.RetryCount,
                    repairAttemptCount: ex.RepairAttemptCount,
                    repairSucceeded: ex.RepairSucceeded,
                    responseTruncated: ex.ResponseTruncated == true,
                    attemptCount: attempt,
                    stepRetries: attempt - 1,
                    retryExhausted: ex.Category == LlmErrorCategory.Timeout,
                    contextFingerprint: fingerprint);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogWarning(ex, "Acquisition step {Step} failed", step.StepId);
                return Failure(runId, step, selection, ex.Message, stopwatch.Elapsed, provider,
                    attemptCount: attempt,
                    contextFingerprint: fingerprint);
            }
        }

        // Unreachable: the bounded loop always returns on its final iteration.
        throw new InvalidOperationException($"Bounded retry loop for step {step.StepId} terminated without a result.");
    }

    private static int? Sum(IEnumerable<int?> values)
        => values.Any(v => v.HasValue) ? values.Sum(v => v ?? 0) : null;

    private static decimal? Sum(IEnumerable<decimal?> values)
        => values.Any(v => v.HasValue) ? values.Sum(v => v ?? 0m) : null;

    /// <summary>
    /// Context selection for an agentic step: an EMPTY, explicit "the agent explores"
    /// selection. It records the repository scope (files considered) but bundles no
    /// files and no content — the council does not control the agent's effective
    /// context, so it provides none.
    /// </summary>
    private static AnalysisContextSelection AgenticSelection(RepositorySnapshot repository) => new()
    {
        Strategy = "agentic",
        Files = [],
        TotalRepositoryFiles = repository.Files.Count,
        SelectedFileCount = 0,
        EstimatedContentSize = 0,
        SelectionReasons =
        [
            "Autonomous exploration: the external agent reads the repository itself; the council bundles no controlled context."
        ]
    };

    /// <summary>
    /// A copy of the repository snapshot with all file CONTENT stripped, used in
    /// agentic requests. The agent receives the repository identity (root, solution,
    /// branch/commit) and file structure but NOT the file bodies — it is responsible
    /// for exploration. Content is the only thing stripped; paths and metadata stay.
    /// </summary>
    private static RepositorySnapshot WithoutContents(RepositorySnapshot repository)
        => repository with
        {
            Files = repository.Files.Select(f => f with { Content = null }).ToList()
        };

    private static (IReadOnlyList<Core.Domain.Evidence>, ProviderExecutionRecord) Failure(
        string runId, EvidenceAcquisitionStep step, AnalysisContextSelection selection, string error, TimeSpan duration,
        IEvidenceProvider? provider = null,
        string? errorCategory = null, int? retryCount = null, int? repairAttemptCount = null,
        bool? repairSucceeded = null, bool responseTruncated = false, string? contextFingerprint = null,
        int attemptCount = 1, int stepRetries = 0, bool retryExhausted = false)
    {
        var providerType = provider?.Metadata.ProviderType ?? EvidenceProviderType.Custom;

        var evidence = new Core.Domain.Evidence
        {
            ProviderName = step.ProviderName,
            ProviderId = step.ProviderName.ToLowerInvariant(),
            ProviderType = providerType,
            ProviderVersion = provider?.Metadata.Version ?? string.Empty,
            RawResponse = string.Empty,
            Success = false,
            ErrorMessage = error,
            Duration = duration,
            AcquisitionScope = step.Scope,
            RequestedDiscipline = step.Discipline,
            CorrelationId = step.CorrelationId,
            AcquisitionStepId = step.StepId,
            ContextFileCount = selection.SelectedFileCount,
            ContextCharacterCount = (int)selection.EstimatedContentSize,
            ContextSelectionStrategy = selection.Strategy,
            ContextFingerprint = contextFingerprint ?? string.Empty,
            RetryCount = (retryCount ?? 0) + stepRetries,
            AttemptCount = attemptCount,
            RetryExhausted = retryExhausted,
            RepairAttemptCount = repairAttemptCount ?? 0,
            RepairSucceeded = repairSucceeded,
            ResponseTruncated = responseTruncated,
            ErrorCategory = errorCategory
        };

        var record = new ProviderExecutionRecord
        {
            RunId = runId,
            StepId = step.StepId,
            ProviderName = step.ProviderName,
            ProviderId = step.ProviderName.ToLowerInvariant(),
            ProviderType = providerType,
            ProviderVersion = provider?.Metadata.Version ?? string.Empty,
            Scope = step.Scope,
            RequestedDiscipline = step.Discipline,
            CorrelationId = step.CorrelationId,
        ContextSelectionStrategy = selection.Strategy,
        ContextFileCount = selection.SelectedFileCount,
        ContextFilesConsidered = selection.TotalRepositoryFiles,
        ContextCharacterCount = (int)selection.EstimatedContentSize,
        ContextFingerprint = contextFingerprint ?? string.Empty,
        EvidenceCount = 0,
        Success = false,
            Duration = duration,
            ErrorMessage = error,
            RetryCount = (retryCount ?? 0) + stepRetries,
            AttemptCount = attemptCount,
            RetryExhausted = retryExhausted,
            RepairAttemptCount = repairAttemptCount ?? 0,
            RepairSucceeded = repairSucceeded,
            ResponseTruncated = responseTruncated,
            ErrorCategory = errorCategory
        };

        return (new[] { evidence }, record);
    }

    private static ProviderExecutionRecord Record(
        string runId, EvidenceAcquisitionStep step, IEvidenceProvider provider, AnalysisContextSelection selection,
        bool success, TimeSpan duration, int evidenceCount, int? tokens, decimal? cost,
        Core.Domain.Evidence? metrics, string? error, string? contextFingerprint = null,
        int attemptCount = 1, int stepRetries = 0, bool retryExhausted = false)
    {
        var activity = TokenEfficiencyMetrics.ContextTokenActivity(
            metrics?.InputTokens, metrics?.CacheCreationInputTokens, metrics?.CacheReadInputTokens);

        return new ProviderExecutionRecord
        {
            RunId = runId,
            StepId = step.StepId,
            ProviderName = provider.Metadata.Name,
            ProviderId = provider.Metadata.Name.ToLowerInvariant(),
            ProviderType = provider.Metadata.ProviderType,
            ProviderVersion = provider.Metadata.Version ?? string.Empty,
            Scope = step.Scope,
            RequestedDiscipline = step.Discipline,
            CorrelationId = step.CorrelationId,
            ContextSelectionStrategy = selection.Strategy,
            ContextFileCount = selection.SelectedFileCount,
            ContextFilesConsidered = selection.TotalRepositoryFiles,
            ContextCharacterCount = (int)selection.EstimatedContentSize,
            ContextFingerprint = contextFingerprint ?? string.Empty,
            EvidenceCount = evidenceCount,
            Success = success,
            Duration = duration,
            TokensUsed = tokens,
            CostEstimate = cost,
            InputTokens = metrics?.InputTokens,
            OutputTokens = metrics?.OutputTokens,
            CacheReadInputTokens = metrics?.CacheReadInputTokens,
            CacheCreationInputTokens = metrics?.CacheCreationInputTokens,
            ContextTokenActivity = activity,
            FreshContextTokens = TokenEfficiencyMetrics.FreshContextTokens(
                metrics?.InputTokens, metrics?.CacheCreationInputTokens),
            CacheReuseRatio = TokenEfficiencyMetrics.CacheReuseRatio(
                metrics?.CacheReadInputTokens, activity),
            RetryCount = (metrics?.RetryCount ?? 0) + stepRetries,
            AttemptCount = attemptCount,
            RetryExhausted = retryExhausted,
            RepairAttemptCount = metrics?.RepairAttemptCount ?? 0,
            RepairSucceeded = metrics?.RepairSucceeded,
            FinishReason = metrics?.FinishReason,
            ResponseTruncated = metrics?.ResponseTruncated ?? false,
            ErrorCategory = metrics?.ErrorCategory,
            ErrorMessage = error
        };
    }

    /// <summary>
    /// Deterministic per-step result captured by the parallel scheduler: either a
    /// completed step (evidence + execution record) or a captured exception (run
    /// cancellation, or an unexpected step leak) to propagate in plan order — never
    /// completion order.
    /// </summary>
    private readonly record struct StepOutcome(IReadOnlyList<Core.Domain.Evidence> Items, ProviderExecutionRecord? Record, Exception? Exception)
    {
        public static StepOutcome Of(IReadOnlyList<Core.Domain.Evidence> items, ProviderExecutionRecord record)
            => new(items, record, null);

        public static StepOutcome Of(Exception exception) => new([], null, exception);
    }
}
