using System.Diagnostics;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringCouncil.Infrastructure.Evidence;

/// <summary>
/// The OpenCode-backed agentic evidence provider (Milestone 013.2, extended
/// Milestone 013.3). It is <see cref="EvidenceProviderType.Agentic"/> and
/// <b>Discipline</b>-scoped: it launches the OpenCode executable against the analyzed
/// repository root, gives it one focused, read-only discipline analysis instruction,
/// and converts the agent's structured <c>observations</c> envelope into the SAME
/// <c>Evidence</c> abstraction consumed by every other source. Downstream
/// (interpreter → observations → analyzers → reconciliation → package) never learns
/// that OpenCode exists.
///
/// OpenCode is an external runtime: the council does NOT bundle a controlled context
/// and does NOT fabricate a <c>ContextFingerprint</c> (it cannot vouch for the
/// effective context the agent explored). When a model is explicitly configured
/// (<c>Evidence:OpenCode:Model</c>), it is passed through the safe process invocation
/// as <c>--model &lt;provider/model&gt;</c> and preserved as telemetry metadata — the
/// provider represents OpenCode, never a specific model, and credentials stay in
/// OpenCode's own auth mechanism.
/// </summary>
public sealed class OpenCodeEvidenceProvider : IEvidenceProvider
{
    /// <summary>Disciplines every agentic source can serve.</summary>
    private static readonly IReadOnlySet<FindingCategory> AllDisciplines = new HashSet<FindingCategory>
    {
        FindingCategory.Architecture, FindingCategory.CodeQuality, FindingCategory.Reliability,
        FindingCategory.Security, FindingCategory.Testing, FindingCategory.Documentation,
        FindingCategory.Observability
    };

    /// <summary>OpenCode's official model-selection flag: <c>--model &lt;provider/model&gt;</c>.</summary>
    private const string ModelArgument = "--model";

    /// <summary>OpenCode's local-server port flag: <c>--port [&lt;n&gt;]</c>.</summary>
    private const string PortArgument = "--port";

    private readonly OpenCodeOptions _options;
    private readonly IOpenCodeProcessRunner _runner;
    private readonly ILogger _logger;

    public OpenCodeEvidenceProvider(
        OpenCodeOptions options,
        IOpenCodeProcessRunner runner,
        ILogger? logger = null)
    {
        _options = options;
        _runner = runner;
        _logger = logger ?? NullLogger.Instance;
    }

    public bool IsAvailable => _options.IsUsable;

    public string? UnavailableReason => _options.UnavailableReason;

    public EvidenceProviderMetadata Metadata => new()
    {
        Name = _options.ProviderName,
        ProviderType = EvidenceProviderType.Agentic,
        DefaultAcquisitionScope = EvidenceAcquisitionScope.Discipline,
        SupportedDisciplines = AllDisciplines,
        RequiresAnalyzerInstructions = true,
        SupportsRepositoryWideAnalysis = false,
        Version = "opencode-adapter-v1",
        Timeout = _options.Timeout
    };

    public async Task<IReadOnlyList<Core.Domain.Evidence>> CollectAsync(
        EvidenceRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            throw new LlmProviderException(LlmErrorCategory.Configuration,
                UnavailableReason ?? $"{_options.ProviderName} is not configured.");

        var instruction = OpenCodePromptBuilder.Build(request);
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        // OpenCode non-interactive mode: `opencode run [--model <provider/model>]
        // "<instruction>"`. Everything travels via ArgumentList — never a shell string.
        var arguments = new List<string> { "run" };
        if (!string.IsNullOrWhiteSpace(_options.Model))
        {
            arguments.Add(ModelArgument);
            arguments.Add(_options.Model);
        }
        if (_options.Port is { } port)
        {
            // OpenCode 1.18+ consumes a bare `--port` followed by a non-flag token as its
            // value, which would swallow the analysis instruction ("You must provide a
            // message or a command"). Always emit `--port <n>` with a concrete number:
            // a configured port, or a random free port when the config requests 0.
            arguments.Add(PortArgument);
            arguments.Add((port > 0 ? port : FindFreePort()).ToString());
        }
        arguments.Add(instruction);

        OpenCodeProcessResult result;
        try
        {
            result = await _runner.RunAsync(new OpenCodeProcessRequest
            {
                Executable = _options.Executable,
                WorkingDirectory = request.RepositorySnapshot.RootPath,
                Arguments = arguments,
                Timeout = _options.Timeout
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Run/user cancellation — propagates; it is never an ordinary provider failure.
            throw;
        }

        stopwatch.Stop();

        if (result.TimedOut)
            throw new LlmProviderException(LlmErrorCategory.Timeout,
                $"{_options.ProviderName} exceeded the {_options.Timeout.TotalSeconds:0}s timeout.");

        if (result.ExitCode != 0)
            throw new LlmProviderException(LlmErrorCategory.ProcessError,
                $"{_options.ProviderName} exited with code {result.ExitCode}.{BoundedDiagnostic(result.StandardError)}");

        // Reuse the existing structured LLM evidence validator — OpenCode returns the
        // same envelope; malformed output is a categorized provider failure, never
        // arbitrary prose or an LLM repair call.
        var validation = LlmEvidenceResponseValidator.Validate(result.StandardOutput, request.Discipline, truncated: false);
        if (!validation.IsValid)
            throw new LlmProviderException(LlmErrorCategory.SchemaValidation,
                $"{_options.ProviderName} returned a structured response that failed validation: {validation.ErrorSummary}");

        _logger.LogInformation(
            "Provider={Provider} Runtime=opencode{Model} Discipline={Discipline} ExitCode={ExitCode} Observations={Observations} DurationMs={Duration} Success=true",
            _options.ProviderName,
            string.IsNullOrWhiteSpace(_options.Model) ? string.Empty : $" Model={_options.Model}",
            request.Discipline?.ToString() ?? "repository",
            result.ExitCode, validation.ObservationCount, stopwatch.ElapsedMilliseconds);

        // The configured model, when explicitly selected, is preserved so the run can
        // be attributed to OpenCode + <model>. It is never guessed: a model that was
        // not explicitly configured is omitted (M13.3).
        var metadata = new Dictionary<string, string>
        {
            ["provider"] = _options.ProviderName,
            ["agentRuntime"] = "opencode",
            ["discipline"] = request.Discipline?.ToString() ?? "repository",
            ["responseSchemaVersion"] = validation.SchemaVersion,
            ["observationCount"] = validation.ObservationCount.ToString(),
            ["exitCode"] = result.ExitCode.ToString(),
            ["startedAt"] = startedAt.ToString("O")
        };
        if (!string.IsNullOrWhiteSpace(_options.Model))
            metadata["model"] = _options.Model;

        var evidence = new Core.Domain.Evidence
        {
            ProviderName = _options.ProviderName,
            ProviderId = "opencode",
            ProviderVersion = Metadata.Version!,
            ProviderType = EvidenceProviderType.Agentic,
            RawResponse = validation.Json,
            Confidence = 0.5,
            ExecutionTime = stopwatch.Elapsed,
            Duration = stopwatch.Elapsed,
            Success = true,
            Metadata = metadata
        };

        return [evidence];
    }

    private static string BoundedDiagnostic(string stderr)
        => string.IsNullOrWhiteSpace(stderr) ? string.Empty : $" Diagnostic: {stderr.Trim()}";

    /// <summary>
    /// Finds a random free TCP port for an isolated OpenCode run. The port is released
    /// immediately after binding so OpenCode can claim it; a collision is unlikely and
    /// OpenCode falls back to its own random port if the given one is busy.
    /// </summary>
    private static int FindFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}
