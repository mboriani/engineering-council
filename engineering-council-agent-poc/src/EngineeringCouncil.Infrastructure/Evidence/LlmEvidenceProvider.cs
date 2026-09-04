using System.Diagnostics;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringCouncil.Infrastructure.Evidence;

/// <summary>
/// Shared implementation for every REAL LLM evidence provider (Milestone 011).
/// Claude and OpenAI differ ONLY in their transport adapter and options — the
/// prompt, schema, validation, retry policy, repair attempt, and telemetry are
/// identical here, so no provider-name branching exists anywhere in the platform.
///
/// A provider is an evidence source: it returns validated, structured candidate
/// observations. It never scans the repository (it receives only the selected
/// context), never creates findings, and never writes artifacts.
/// </summary>
public abstract class LlmEvidenceProvider : IEvidenceProvider
{
    /// <summary>Disciplines every LLM source can serve.</summary>
    private static readonly IReadOnlySet<FindingCategory> AllDisciplines = new HashSet<FindingCategory>
    {
        FindingCategory.Architecture, FindingCategory.CodeQuality, FindingCategory.Reliability,
        FindingCategory.Security, FindingCategory.Testing, FindingCategory.Documentation,
        FindingCategory.Observability
    };

    private readonly ILlmChatClient _client;
    private readonly LlmProviderOptions _options;
    private readonly IEvidencePromptBuilder _promptBuilder;
    private readonly ILogger _logger;

    protected LlmEvidenceProvider(
        ILlmChatClient client, LlmProviderOptions options, IEvidencePromptBuilder promptBuilder, ILogger? logger = null)
    {
        _client = client;
        _options = options;
        _promptBuilder = promptBuilder;
        _logger = logger ?? NullLogger.Instance;
    }

    public bool IsAvailable => _options.IsUsable;

    public string? UnavailableReason => _options.UnavailableReason;

    public EvidenceProviderMetadata Metadata => new()
    {
        Name = _options.ProviderName,
        ProviderType = EvidenceProviderType.LLM,
        DefaultAcquisitionScope = EvidenceAcquisitionScope.Discipline,
        SupportedDisciplines = AllDisciplines,
        RequiresAnalyzerInstructions = true,
        SupportsRepositoryWideAnalysis = false,
        Version = _options.Model
    };

    public async Task<IReadOnlyList<Core.Domain.Evidence>> CollectAsync(
        EvidenceRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            throw new LlmProviderException(LlmErrorCategory.Configuration,
                UnavailableReason ?? $"{_options.ProviderName} is not configured.");

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        var prompt = _promptBuilder.Build(request);
        var completionRequest = new LlmCompletionRequest
        {
            SystemInstructions = prompt.SystemInstructions,
            UserContent = prompt.UserContent,
            Model = _options.Model,
            MaxOutputTokens = _options.MaxOutputTokens
        };

        var (response, retries) = await SendWithRetryAsync(completionRequest, cancellationToken).ConfigureAwait(false);
        var validation = LlmEvidenceResponseValidator.Validate(response.Text, request.Discipline, response.Truncated);

        // At most ONE structured-response repair attempt — never a repair loop.
        var repairAttempted = false;
        var repairSucceeded = false;
        if (!validation.IsValid && _options.EnableStructuredRepair)
        {
            repairAttempted = true;
            _logger.LogWarning("{Provider} returned an invalid structured response for {Discipline}; attempting one repair. Errors: {Errors}",
                _options.ProviderName, request.Discipline, validation.ErrorSummary);

            var repair = completionRequest with { UserContent = BuildRepairContent(response.Text, validation, prompt) };
            var (repaired, repairRetries) = await SendWithRetryAsync(repair, cancellationToken).ConfigureAwait(false);
            retries += repairRetries;
            var repairedValidation = LlmEvidenceResponseValidator.Validate(repaired.Text, request.Discipline, repaired.Truncated);
            if (repairedValidation.IsValid)
            {
                repairSucceeded = true;
                response = repaired;
                validation = repairedValidation;
            }
        }

        stopwatch.Stop();

        if (!validation.IsValid)
            throw new LlmProviderException(LlmErrorCategory.SchemaValidation,
                $"{_options.ProviderName} returned a structured response that failed validation"
                + (repairAttempted ? " (after one repair attempt)" : "") + $": {validation.ErrorSummary}",
                retryCount: retries,
                repairAttemptCount: repairAttempted ? 1 : 0,
                repairSucceeded: repairAttempted ? repairSucceeded : null,
                responseTruncated: response.Truncated);

        _logger.LogInformation(
            "Provider={Provider} Model={Model} Discipline={Discipline} InputFiles={Files} InputCharacters={Chars} Observations={Observations} Retries={Retries} DurationMs={Duration} Success=true",
            _options.ProviderName, _options.Model, request.Discipline?.ToString() ?? "repository",
            prompt.ContextFileCount, prompt.ContextCharacterCount, validation.ObservationCount, retries, stopwatch.ElapsedMilliseconds);

        return [BuildEvidence(request, prompt, response, validation, startedAt, stopwatch.Elapsed, retries, repairAttempted, repairSucceeded)];
    }

    // ── Transport with bounded retry of TRANSIENT failures only ────────────────

    private async Task<(LlmCompletionResponse Response, int Retries)> SendWithRetryAsync(
        LlmCompletionRequest request, CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(0, _options.MaxRetries);

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.Timeout);

            try
            {
                var response = await _client.CompleteAsync(request, timeoutCts.Token).ConfigureAwait(false);
                return (response, attempt);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("{Provider} request was cancelled by the run; no retry.", _options.ProviderName);
                throw; // the RUN was cancelled — propagate, never retry
            }
            catch (OperationCanceledException ex)
            {
                if (attempt >= maxRetries)
                {
                    _logger.LogWarning("{Provider} timed out after {Seconds}s and {Retries} retries.",
                        _options.ProviderName, _options.Timeout.TotalSeconds, maxRetries);
                    throw new LlmProviderException(LlmErrorCategory.Timeout,
                        $"{_options.ProviderName} timed out after {_options.Timeout.TotalSeconds:0}s.", ex,
                        retryCount: attempt, responseTruncated: false);
                }
            }
            catch (LlmProviderException ex) when (ex.Category == LlmErrorCategory.Cancelled)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("{Provider} request was cancelled by the run; no retry.", _options.ProviderName);
                    throw; // genuine run cancellation — never retried
                }
                if (attempt >= maxRetries)
                {
                    _logger.LogWarning("{Provider} timed out after {Seconds}s and {Retries} retries.",
                        _options.ProviderName, _options.Timeout.TotalSeconds, maxRetries);
                    throw new LlmProviderException(LlmErrorCategory.Timeout,
                        $"{_options.ProviderName} timed out after {_options.Timeout.TotalSeconds:0}s.", ex,
                        retryCount: attempt, responseTruncated: false);
                }
                _logger.LogWarning("{Provider} timed out; retry {Attempt}/{Max}.",
                    _options.ProviderName, attempt + 1, maxRetries);
            }
            catch (LlmProviderException ex) when (ex.IsTransient)
            {
                if (attempt >= maxRetries)
                {
                    throw new LlmProviderException(ex.Category, ex.Message, ex,
                        retryCount: attempt, responseTruncated: ex.ResponseTruncated);
                }
                _logger.LogWarning("{Provider} transient failure ({Category}); retry {Attempt}/{Max}.",
                    _options.ProviderName, ex.Category, attempt + 1, maxRetries);
            }

            // Bounded exponential backoff, cancellable.
            var delay = TimeSpan.FromMilliseconds(Math.Min(2000, 200 * Math.Pow(2, attempt)));
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string BuildRepairContent(string? invalidResponse, LlmResponseValidation validation, EvidencePrompt prompt)
        => $"""
            Your previous response failed structured-output validation.

            Validation errors:
            {string.Join("\n", validation.Errors.Select(e => $"- {e}"))}

            Previous response:
            {invalidResponse}

            Return ONLY the corrected JSON object for schemaVersion {prompt.ResponseSchemaVersion}
            and discipline "{prompt.Discipline?.ToString() ?? "Repository"}". Do NOT analyze the
            repository again, do not add new observations, and do not include any text outside
            the JSON object.
            """;

    private Core.Domain.Evidence BuildEvidence(
        EvidenceRequest request, EvidencePrompt prompt, LlmCompletionResponse response,
        LlmResponseValidation validation, DateTimeOffset startedAt, TimeSpan duration, int retries, bool repairAttempted,
        bool repairSucceeded)
    {
        // Provider-neutral metadata only. Unavailable values are omitted, never invented.
        var metadata = new Dictionary<string, string>
        {
            ["provider"] = _options.ProviderName,
            ["providerId"] = _client.ProviderId,
            ["model"] = _options.Model,
            ["discipline"] = request.Discipline?.ToString() ?? "repository",
            ["acquisitionStepId"] = request.CorrelationId,
            ["responseSchemaVersion"] = validation.SchemaVersion,
            ["observationCount"] = validation.ObservationCount.ToString(),
            ["startedAt"] = startedAt.ToString("O"),
            ["completedAt"] = startedAt.Add(duration).ToString("O"),
            ["retryCount"] = retries.ToString(),
            ["repairAttempted"] = repairAttempted ? "true" : "false",
            ["truncated"] = response.Truncated ? "true" : "false",
            ["contextFileCount"] = prompt.ContextFileCount.ToString(),
            ["contextCharacterCount"] = prompt.ContextCharacterCount.ToString()
        };
        if (!string.IsNullOrWhiteSpace(response.ResponseId)) metadata["responseId"] = response.ResponseId!;
        if (!string.IsNullOrWhiteSpace(response.RequestId)) metadata["requestId"] = response.RequestId!;
        if (!string.IsNullOrWhiteSpace(response.FinishReason)) metadata["finishReason"] = response.FinishReason!;
        if (response.InputTokens is { } input) metadata["inputTokens"] = input.ToString();
        if (response.OutputTokens is { } output) metadata["outputTokens"] = output.ToString();
        if (response.TotalTokens is { } total) metadata["totalTokens"] = total.ToString();

        return new Core.Domain.Evidence
        {
            ProviderName = _options.ProviderName,
            ProviderId = _client.ProviderId,
            ProviderVersion = _options.Model,
            ProviderType = EvidenceProviderType.LLM,
            RawResponse = validation.Json,          // the validated structured payload only
            Confidence = 0.7,
            ExecutionTime = duration,
            Duration = duration,
            Success = true,
            TokensUsed = response.TotalTokens,
            CostEstimate = null,                     // pricing is configuration, never hardcoded here
            InputTokens = response.InputTokens,
            OutputTokens = response.OutputTokens,
            RetryCount = retries,
            RepairAttemptCount = repairAttempted ? 1 : 0,
            RepairSucceeded = repairAttempted ? repairSucceeded : null,
            FinishReason = string.IsNullOrWhiteSpace(response.FinishReason) ? null : response.FinishReason,
            ResponseTruncated = response.Truncated,
            ErrorCategory = null,
            Metadata = metadata
        };
    }
}
