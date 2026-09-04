using System.Diagnostics;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringCouncil.Infrastructure.Evidence;

/// <summary>
/// The Claude Code-backed agentic evidence provider (Milestone 013.5). It is
/// <see cref="EvidenceProviderType.Agentic"/> and <b>Discipline</b>-scoped: it
/// launches the native <c>claude</c> executable against the analyzed repository
/// root, gives it one focused, read-only discipline analysis instruction, and
/// converts the agent's structured <c>observations</c> envelope into the SAME
/// <c>Evidence</c> abstraction consumed by every other source. Downstream
/// (interpreter → observations → analyzers → reconciliation → package) never learns
/// that Claude Code exists.
///
/// Naming (M13.5): <c>ClaudeCode</c> is the agentic Claude Code CLI runtime. It is a
/// DISTINCT provider from <c>Claude</c>, the direct Anthropic API provider — the two
/// are never silently conflated.
///
/// Claude Code is an external runtime: the council does NOT bundle a controlled
/// context and does NOT fabricate a <c>ContextFingerprint</c> (it cannot vouch for
/// the effective context the agent explored). Authentication stays entirely with
/// Claude Code's own login (<c>claude auth</c> / the configured account) — the
/// council never reads, stores, or forwards a credential. When a model is explicitly
/// configured (<c>Evidence:ClaudeCode:Model</c>), it is passed through the safe
/// process invocation as <c>--model &lt;model&gt;</c> and preserved as telemetry
/// metadata — the provider represents Claude Code, never a specific model.
/// </summary>
public sealed class ClaudeCodeEvidenceProvider : IEvidenceProvider
{
    /// <summary>Disciplines every agentic source can serve.</summary>
    private static readonly IReadOnlySet<FindingCategory> AllDisciplines = new HashSet<FindingCategory>
    {
        FindingCategory.Architecture, FindingCategory.CodeQuality, FindingCategory.Reliability,
        FindingCategory.Security, FindingCategory.Testing, FindingCategory.Documentation,
        FindingCategory.Observability
    };

    /// <summary>Claude Code's official model-selection flag: <c>--model &lt;model&gt;</c>.</summary>
    private const string ModelArgument = "--model";

    /// <summary>Claude Code's supported non-interactive output format: <c>--output-format json</c>.</summary>
    private const string JsonOutputFormat = "json";

    /// <summary>
    /// The smallest supported read-only tool restriction: <c>--tools "Read,Glob,Grep"</c>
    /// removes write-capable built-in tools (Edit/Write/Bash/MultiEdit) from the available
    /// toolset entirely. Verified against `claude --help` (v2.1.223).
    /// </summary>
    private const string ReadOnlyTools = "Read,Glob,Grep";

    private readonly ClaudeCodeOptions _options;
    private readonly IClaudeCodeProcessRunner _runner;
    private readonly ILogger _logger;

    public ClaudeCodeEvidenceProvider(
        ClaudeCodeOptions options,
        IClaudeCodeProcessRunner runner,
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
        Version = "claudecode-adapter-v1",
        Timeout = _options.Timeout
    };

    public async Task<IReadOnlyList<Core.Domain.Evidence>> CollectAsync(
        EvidenceRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            throw new LlmProviderException(LlmErrorCategory.Configuration,
                UnavailableReason ?? $"{_options.ProviderName} is not configured.");

        var instruction = ClaudeCodePromptBuilder.Build(request);
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        // Claude Code non-interactive mode:
        //   claude -p --output-format json --no-session-persistence
        //          --tools "Read,Glob,Grep" [--model <model>] -- "<instruction>"
        // Everything travels via ArgumentList — never a shell string. `--tools`
        // restricts the built-in toolset to read-only tools (the CLI's own supported
        // read-only mechanism; the prompt is the second boundary). `--no-session-
        // persistence` keeps the run session-local (mirrors Codex's `--ephemeral`).
        // Because `--tools <tools...>` is a variadic option, `--` terminates option
        // parsing so the instruction is passed as the positional prompt argument
        // (verified against `claude --help`, v2.1.223) — without it the variadic
        // option would swallow the prompt as another tool name.
        var arguments = new List<string>
        {
            "-p",
            "--output-format", JsonOutputFormat,
            "--no-session-persistence",
            "--tools", ReadOnlyTools
        };
        if (!string.IsNullOrWhiteSpace(_options.Model))
        {
            arguments.Add(ModelArgument);
            arguments.Add(_options.Model);
        }
        arguments.Add("--");
        arguments.Add(instruction);

        ClaudeCodeProcessResult result;
        try
        {
            result = await _runner.RunAsync(new ClaudeCodeProcessRequest
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

        // The CLI wraps the agent's final message in a JSON envelope (`--output-format
        // json`); unwrap that ONE documented field, then reuse the existing structured
        // LLM evidence validator. The same envelope carries the CLI's OWN authoritative
        // session usage (usage.input_tokens / usage.output_tokens), lifted here for
        // provider-neutral telemetry. Malformed output is a categorized provider
        // failure, never arbitrary prose or an LLM repair call.
        if (!ClaudeCodeOutputExtractor.TryExtractResult(result.StandardOutput, out var responseText, out var envelopeError, out var usage))
            throw new LlmProviderException(LlmErrorCategory.SchemaValidation,
                $"{_options.ProviderName} returned a structured response that failed validation: {envelopeError}");

        var validation = LlmEvidenceResponseValidator.Validate(responseText, request.Discipline, truncated: false);
        if (!validation.IsValid)
            throw new LlmProviderException(LlmErrorCategory.SchemaValidation,
                $"{_options.ProviderName} returned a structured response that failed validation: {validation.ErrorSummary}");

        _logger.LogInformation(
            "Provider={Provider} Runtime=claude-code{Model} Discipline={Discipline} ExitCode={ExitCode} Observations={Observations} DurationMs={Duration} Success=true",
            _options.ProviderName,
            string.IsNullOrWhiteSpace(_options.Model) ? string.Empty : $" Model={_options.Model}",
            request.Discipline?.ToString() ?? "repository",
            result.ExitCode, validation.ObservationCount, stopwatch.ElapsedMilliseconds);

        // The configured model, when explicitly selected, is preserved so the run can
        // be attributed to Claude Code + <model>. It is never guessed: a model that was
        // not explicitly configured is omitted (M13.3 convention).
        var metadata = new Dictionary<string, string>
        {
            ["provider"] = _options.ProviderName,
            ["agentRuntime"] = "claude-code",
            ["discipline"] = request.Discipline?.ToString() ?? "repository",
            ["responseSchemaVersion"] = validation.SchemaVersion,
            ["observationCount"] = validation.ObservationCount.ToString(),
            ["exitCode"] = result.ExitCode.ToString(),
            ["startedAt"] = startedAt.ToString("O")
        };
        if (!string.IsNullOrWhiteSpace(_options.Model))
            metadata["model"] = _options.Model;
        if (usage.InputTokens is { } inputTokens) metadata["inputTokens"] = inputTokens.ToString();
        if (usage.OutputTokens is { } outputTokens) metadata["outputTokens"] = outputTokens.ToString();
        if (usage.TotalTokens is { } totalTokens) metadata["totalTokens"] = totalTokens.ToString();
        if (usage.CacheReadInputTokens is { } cacheRead) metadata["cacheReadInputTokens"] = cacheRead.ToString();
        if (usage.CacheCreationInputTokens is { } cacheCreation) metadata["cacheCreationInputTokens"] = cacheCreation.ToString();

        var evidence = new Core.Domain.Evidence
        {
            ProviderName = _options.ProviderName,
            ProviderId = "claudecode",
            ProviderVersion = Metadata.Version!,
            ProviderType = EvidenceProviderType.Agentic,
            RawResponse = validation.Json,
            Confidence = 0.5,
            ExecutionTime = stopwatch.Elapsed,
            Duration = stopwatch.Elapsed,
            Success = true,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            TokensUsed = usage.TotalTokens,
            CacheReadInputTokens = usage.CacheReadInputTokens,
            CacheCreationInputTokens = usage.CacheCreationInputTokens,
            Metadata = metadata
        };

        return [evidence];
    }

    private static string BoundedDiagnostic(string stderr)
        => string.IsNullOrWhiteSpace(stderr) ? string.Empty : $" Diagnostic: {stderr.Trim()}";
}
