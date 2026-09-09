using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.DependencyInjection;
using EngineeringCouncil.Infrastructure.GraphAssistance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Engineering Council Agent — CLI
//
// Usage:
//   analyze --path <target-solution-folder> [--outputs <dir>] [--provider <name>] [--mock]
//
// The tool is READ-ONLY over the target: it scans, analyzes and writes results
// only under the outputs directory of THIS POC. It never modifies the target.

// "evaluate" is the internal calibration verb (Milestone 012): it measures the platform
// over the evaluation dataset and writes evaluation-report.md. It changes nothing about
// how a normal review runs or what the Engineering Review Package contains.
if (args.Length > 0 && string.Equals(args[0], "evaluate", StringComparison.OrdinalIgnoreCase))
    return await EngineeringCouncil.Cli.EvaluateCommand.RunAsync(args);

var parsed = CliArgs.Parse(args);
if (parsed is null)
{
    CliArgs.PrintUsage();
    return 1;
}

// Fail fast on invalid disciplines — before any scan/analysis runs.
if (parsed.InvalidDisciplines.Count > 0)
{
    Console.Error.WriteLine(
        $"Unknown discipline(s): {string.Join(", ", parsed.InvalidDisciplines)}. "
        + $"Valid disciplines: {string.Join(", ", Enum.GetNames<FindingCategory>())}.");
    return 1;
}

using var host = Host.CreateDefaultBuilder(args)
    // M14.1 hang: appsettings.json must come from the app base directory, not the
    // process current directory. `dotnet run --project src\EngineeringCouncil.Cli`
    // from the repo root otherwise never loads this project's appsettings.json, so
    // Evidence:OpenCode:Port=0 (isolated --port) is silently inactive and OpenCode
    // falls back to the shared default server and queues until the run hangs.
    .ConfigureAppConfiguration((_, config) => config.SetBasePath(AppContext.BaseDirectory))
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .ConfigureServices((context, services) =>
    {
        // Evidence:Providers may be plain names ["Claude"] or objects [{Name,Scope}].
        var (configNames, configScopes) = CliArgs.ParseProviderConfig(context.Configuration);

        // Provider precedence: --providers/--provider > --mock > config > offline default.
        // An empty list lets DI apply the offline default (Mock) — a real external
        // provider is never selected implicitly (Milestone 011).
        IReadOnlyList<string> requested =
            parsed.Providers is { Count: > 0 } ? parsed.Providers
            : parsed.ForceMock ? ["Mock"]
            : configNames;

        var (maxFiles, maxChars, maxCharsPerFile) = CliArgs.ParseContextLimits(context.Configuration);

        // SARIF files: CLI --sarif merged with Evidence:Sarif config (CLI wins/adds).
        var (sarifEnabledCfg, sarifFilesCfg) = CliArgs.ParseSarifConfig(context.Configuration);
        var sarifFiles = sarifFilesCfg.Concat(parsed.SarifFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var sarifEnabled = sarifEnabledCfg || parsed.SarifFiles.Count > 0;

        services.AddEngineeringCouncil(o =>
        {
            o.OutputsRoot = parsed.OutputsRoot;
            o.Providers = requested;
            o.ProviderScopes = configScopes;
            o.Disciplines = parsed.Disciplines;
            o.ContextMaxFiles = maxFiles;
            o.ContextMaxCharacters = maxChars;
            o.ContextMaxCharactersPerFile = maxCharsPerFile;
            o.SarifFiles = sarifFiles;
            o.SarifEnabled = sarifEnabled;
            o.ForceMockProvider = parsed.ForceMock;

            // Real LLM sources (Milestone 011): Evidence:Claude / Evidence:OpenAI.
            // Only the NAME of the API-key environment variable is configurable.
            CliArgs.BindLlmProvider(context.Configuration.GetSection("Evidence:Claude"), o.Claude);
            CliArgs.BindLlmProvider(context.Configuration.GetSection("Evidence:OpenAI"), o.OpenAi);
            CliArgs.BindOpenCode(context.Configuration.GetSection("Evidence:OpenCode"), o.OpenCode);
            CliArgs.BindCodex(context.Configuration.GetSection("Evidence:Codex"), o.Codex);
            CliArgs.BindClaudeCode(context.Configuration.GetSection("Evidence:ClaudeCode"), o.ClaudeCode);
            o.ProviderFailureMode = CliArgs.ParseFailureMode(context.Configuration);
            o.MaxConcurrency = CliArgs.ParseMaxConcurrency(context.Configuration);
            o.ProviderTimeout = CliArgs.ParseProviderTimeout(context.Configuration);
            o.MaxAttempts = CliArgs.ParseMaxAttempts(context.Configuration);

            // Targeted semantic reconciliation (Milestone 014.4): Council:SemanticReconciliation.
            // Disabled by default — offline/default behavior stays identical to M14.3.
            CliArgs.BindSemanticReconciliation(
                context.Configuration.GetSection(SemanticReconciliationOptions.SectionName), o.SemanticReconciliation);

            // Graph-assisted agentic context (M16.3): GraphAssistance section.
            // Disabled by default — opt-in per repo/snapshot.
            CliArgs.BindGraphAssistance(
                context.Configuration.GetSection("GraphAssistance"), o.GraphAssistance);
        });
    })
    .Build();

var providers = host.Services.GetRequiredService<SelectedProviders>();
var pipeline = host.Services.GetRequiredService<AnalysisPipeline>();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("cli");

// Fail fast (issue A6): a requested discipline with no registered analyzer aborts
// BEFORE any scan/execution — reporting ALL unsupported disciplines at once.
var orchestrator = host.Services.GetRequiredService<AnalysisOrchestrator>();
var unsupported = AnalyzerDisciplineValidator.Unsupported(parsed.Disciplines, orchestrator.Disciplines);
if (unsupported.Count > 0)
{
    Console.Error.WriteLine(AnalyzerDisciplineValidator.FormatUnsupported(unsupported));
    return 1;
}

// Pre-flight provider configuration check (Milestone 011). Reports a clear,
// categorized, secret-safe reason per unusable provider BEFORE any analysis runs.
var evidenceOptions = host.Services.GetRequiredService<EvidenceOptions>();
var providerFactory = host.Services.GetRequiredService<IEvidenceProviderFactory>();
var providerProblems = new List<string>();
foreach (var name in evidenceOptions.Providers)
{
    if (!providerFactory.TryGetProvider(name, out var provider))
        providerProblems.Add($"'{name}' is not a registered evidence provider.");
    else if (!provider.IsAvailable)
        providerProblems.Add(provider.UnavailableReason ?? $"'{name}' is not available.");
}

if (providerProblems.Count > 0)
{
    Console.Error.WriteLine("Provider configuration error:");
    foreach (var problem in providerProblems) Console.Error.WriteLine($"  {problem}");

    if (evidenceOptions.ProviderFailureMode == ProviderFailureMode.FailRun)
    {
        Console.Error.WriteLine("Evidence:ProviderFailureMode is 'FailRun' — aborting.");
        return 1;
    }
    Console.Error.WriteLine("Continuing with the remaining providers (Evidence:ProviderFailureMode = 'Continue').");
}

if (providers.IsMock && !parsed.ForceMock)
    logger.LogWarning("Running with the offline MOCK provider only — findings are placeholders (enable a real provider to analyze with a model).");

logger.LogInformation("Analyzing '{Target}' with evidence providers: {Providers}", parsed.TargetPath, providers.DisplayName);

var result = await pipeline.RunAsync(new AnalysisRequest
{
    TargetPath = parsed.TargetPath,
    ProviderName = providers.DisplayName
});

var run = result.Run;
var package = result.Package;
Console.WriteLine();

if (parsed.IsReview)
{
    // "review" surfaces the Engineering Review Package, not raw findings.
    Console.WriteLine("Engineering Review Package");
    Console.WriteLine($"  Version:     {package.Version}");
    Console.WriteLine($"  Repository:  {package.Repository} ({package.Branch} @ {package.Commit})");
    Console.WriteLine($"  Run:         {package.AnalysisRunId}");
    Console.WriteLine($"  Health:      {package.OverallEngineeringHealth}");
    Console.WriteLine($"  Risk:        {package.OverallRisk}");
    Console.WriteLine($"  Findings:    {package.Metrics.TotalFindings} "
        + $"(C{package.Metrics.Critical}/H{package.Metrics.High}/M{package.Metrics.Medium}/L{package.Metrics.Low})");
    Console.WriteLine($"  Providers:   {run.Provider}");
    Console.WriteLine($"  Summary:     {package.ExecutiveSummary}");
    Console.WriteLine($"  Output:      {result.OutputDirectory}");
}
else
{
    Console.WriteLine($"Run:        {run.RunId}");
    Console.WriteLine($"Status:     {run.Status}");
    Console.WriteLine($"Providers:  {run.Provider}");
    if (run.ProviderExecution is { } pe)
        Console.WriteLine($"Executions: {pe.TotalExecutions} ({pe.Failures} failed, {pe.EvidenceCount} evidence)");
    Console.WriteLine($"Files:      {run.FilesScanned}");
    Console.WriteLine($"Raw:        {run.RawFindings.Count}");
    Console.WriteLine($"Findings:   {run.Findings.Count}");
    Console.WriteLine($"Health:     {package.OverallEngineeringHealth} · Risk: {package.OverallRisk}");
    Console.WriteLine($"Output:     {result.OutputDirectory}");
}

if (run.Error is not null)
    Console.WriteLine($"Error:      {run.Error.Split('\n')[0]}");

return run.Error is null ? 0 : 2;

internal sealed record ParsedArgs(
    string TargetPath, string OutputsRoot, IReadOnlyList<string>? Providers,
    IReadOnlyList<FindingCategory> Disciplines, IReadOnlyList<string> InvalidDisciplines,
    IReadOnlyList<string> SarifFiles, bool ForceMock, bool IsReview);

internal static class CliArgs
{
    public static ParsedArgs? Parse(string[] args)
    {
        if (args.Length == 0) return null;

        var command = args[0].ToLowerInvariant();
        if (command is "-h" or "--help" or "help") return null;

        // Verbs: "analyze" (findings view) and "review" (Engineering Review Package view).
        var isReview = command == "review";
        var start = command is "analyze" or "review" ? 1 : 0;

        string? path = null;
        List<string>? providers = null;
        List<FindingCategory> disciplines = [];
        List<string> invalidDisciplines = [];
        List<string> sarifFiles = [];
        var outputs = "outputs";
        var mock = false;

        for (var i = start; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--path" or "-p" when i + 1 < args.Length:
                    path = args[++i];
                    break;
                case "--outputs" or "-o" when i + 1 < args.Length:
                    outputs = args[++i];
                    break;
                case "--provider" when i + 1 < args.Length:      // repeatable single provider
                    (providers ??= []).Add(args[++i]);
                    break;
                case "--providers" when i + 1 < args.Length:     // comma-separated list
                    (providers ??= []).AddRange(
                        args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "--disciplines" when i + 1 < args.Length:   // comma-separated disciplines
                    foreach (var token in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (Enum.TryParse<FindingCategory>(token, ignoreCase: true, out var c))
                            disciplines.Add(c);
                        else
                            invalidDisciplines.Add(token);
                    }
                    disciplines = disciplines.Distinct().ToList();
                    break;
                case "--discipline" when i + 1 < args.Length:    // repeatable single discipline
                    if (Enum.TryParse<FindingCategory>(args[++i], ignoreCase: true, out var single))
                    {
                        disciplines.Add(single);
                        disciplines = disciplines.Distinct().ToList();
                    }
                    else invalidDisciplines.Add(args[i]);
                    break;
                case "--sarif" when i + 1 < args.Length:         // repeatable SARIF file path
                    sarifFiles.Add(args[++i]);
                    break;
                case "--mock":
                    mock = true;
                    break;
                default:
                    path ??= args[i]; // bare positional path
                    break;
            }
        }

        return string.IsNullOrWhiteSpace(path)
            ? null
            : new ParsedArgs(path!, outputs, providers, disciplines, invalidDisciplines, sarifFiles, mock, isReview);
    }

    /// <summary>Reads Evidence:Context:{MaximumFiles,MaximumCharacters,MaximumCharactersPerFile} (with defaults).</summary>
    public static (int MaxFiles, int MaxCharacters, int MaxCharactersPerFile) ParseContextLimits(IConfiguration config)
    {
        var section = config.GetSection("Evidence:Context");
        var maxFiles = int.TryParse(section["MaximumFiles"], out var f) && f > 0 ? f : 200;
        var maxChars = int.TryParse(section["MaximumCharacters"], out var c) && c > 0 ? c : 500_000;
        var maxCharsPerFile = int.TryParse(section["MaximumCharactersPerFile"], out var p) && p > 0 ? p : 8_000;
        return (maxFiles, maxChars, maxCharsPerFile);
    }

    /// <summary>
    /// Binds an <c>Evidence:{Provider}</c> section onto the provider options. Only the
    /// NAME of the API-key environment variable is bound — never a key value, which is
    /// always read from the environment at call time.
    /// </summary>
    public static void BindLlmProvider(IConfiguration section, LlmProviderOptions options)
    {
        if (bool.TryParse(section["Enabled"], out var enabled)) options.Enabled = enabled;
        if (section["ApiKeyEnvironmentVariable"] is { Length: > 0 } env) options.ApiKeyEnvironmentVariable = env;
        if (section["Model"] is { Length: > 0 } model) options.Model = model;
        if (section["Endpoint"] is { Length: > 0 } endpoint) options.Endpoint = endpoint;
        if (int.TryParse(section["MaxOutputTokens"], out var maxTokens) && maxTokens > 0) options.MaxOutputTokens = maxTokens;
        if (int.TryParse(section["TimeoutSeconds"], out var timeout) && timeout > 0) options.TimeoutSeconds = timeout;
        if (int.TryParse(section["MaxRetries"], out var retries) && retries >= 0) options.MaxRetries = retries;
        if (bool.TryParse(section["EnableStructuredRepair"], out var repair)) options.EnableStructuredRepair = repair;
    }

    /// <summary>Binds an <c>Evidence:OpenCode</c> section onto the OpenCode agentic options.</summary>
    public static void BindOpenCode(IConfiguration section, OpenCodeOptions options)
    {
        if (bool.TryParse(section["Enabled"], out var enabled)) options.Enabled = enabled;
        if (section["Executable"] is { Length: > 0 } executable) options.Executable = executable;
        if (section["Model"] is { Length: > 0 } model) options.Model = model;
        if (int.TryParse(section["Port"], out var port) && port >= 0) options.Port = port;
        if (int.TryParse(section["TimeoutSeconds"], out var timeout) && timeout > 0) options.TimeoutSeconds = timeout;
    }

    /// <summary>Binds an <c>Evidence:Codex</c> section onto the Codex agentic options.</summary>
    public static void BindCodex(IConfiguration section, CodexOptions options)
    {
        if (bool.TryParse(section["Enabled"], out var enabled)) options.Enabled = enabled;
        if (section["Executable"] is { Length: > 0 } executable) options.Executable = executable;
        if (section["Model"] is { Length: > 0 } model) options.Model = model;
        if (int.TryParse(section["TimeoutSeconds"], out var timeout) && timeout > 0) options.TimeoutSeconds = timeout;
    }

    /// <summary>Binds an <c>Evidence:ClaudeCode</c> section onto the Claude Code agentic options.</summary>
    public static void BindClaudeCode(IConfiguration section, ClaudeCodeOptions options)
    {
        if (bool.TryParse(section["Enabled"], out var enabled)) options.Enabled = enabled;
        if (section["Executable"] is { Length: > 0 } executable) options.Executable = executable;
        if (section["Model"] is { Length: > 0 } model) options.Model = model;
        if (int.TryParse(section["TimeoutSeconds"], out var timeout) && timeout > 0) options.TimeoutSeconds = timeout;
    }

    /// <summary>Binds a <c>Council:SemanticReconciliation</c> section onto the semantic reconciliation options.</summary>
    public static void BindSemanticReconciliation(IConfiguration section, SemanticReconciliationOptions options)
    {
        if (bool.TryParse(section["Enabled"], out var enabled)) options.Enabled = enabled;
        if (section["Model"] is { Length: > 0 } model) options.Model = model;
        if (int.TryParse(section["MaxOutputTokens"], out var maxTokens) && maxTokens > 0) options.MaxOutputTokens = maxTokens;
        if (int.TryParse(section["TimeoutSeconds"], out var timeout) && timeout > 0) options.TimeoutSeconds = timeout;
    }

    /// <summary>Binds a <c>GraphAssistance</c> section onto the graph assistance options.</summary>
    public static void BindGraphAssistance(IConfiguration section, GraphAssistanceOptions options)
    {
        if (bool.TryParse(section["Enabled"], out var enabled)) options.Enabled = enabled;
        if (section["CacheDirectory"] is { Length: > 0 } cacheDir) options.CacheDirectory = cacheDir;
        if (section["GraphifyExecutable"] is { Length: > 0 } exe) options.GraphifyExecutable = exe;
        if (int.TryParse(section["ExtractionTimeoutSeconds"], out var timeout) && timeout > 0) options.ExtractionTimeoutSeconds = timeout;
    }

    /// <summary>Reads Evidence:ProviderFailureMode (Continue | FailRun); defaults to Continue.</summary>
    public static ProviderFailureMode ParseFailureMode(IConfiguration config)
        => Enum.TryParse<ProviderFailureMode>(config["Evidence:ProviderFailureMode"], ignoreCase: true, out var mode)
            ? mode
            : ProviderFailureMode.Continue;

    /// <summary>Reads Evidence:Execution:MaxConcurrency; defaults to 1 (strictly sequential).</summary>
    public static int ParseMaxConcurrency(IConfiguration config)
        => int.TryParse(config["Evidence:Execution:MaxConcurrency"], out var concurrency) && concurrency >= 1
            ? concurrency
            : 1;

    /// <summary>
    /// Reads Evidence:Execution:ProviderTimeout (seconds); defaults to 120. This is
    /// the FALLBACK step timeout — a provider with its own configured timeout uses
    /// that instead (M15.2B).
    /// </summary>
    public static TimeSpan ParseProviderTimeout(IConfiguration config)
        => TimeSpan.FromSeconds(
            int.TryParse(config["Evidence:Execution:ProviderTimeout"], out var seconds) && seconds > 0
                ? seconds
                : 120);

    /// <summary>
    /// Reads Evidence:Execution:MaxAttempts; defaults to 2 (one bounded retry, M15.3B).
    /// Clamped to [1, EvidenceOptions.MaxAttemptsLimit].
    /// </summary>
    public static int ParseMaxAttempts(IConfiguration config)
        => int.TryParse(config["Evidence:Execution:MaxAttempts"], out var attempts) && attempts >= 1
            ? Math.Min(attempts, EvidenceOptions.MaxAttemptsLimit)
            : EvidenceOptions.DefaultMaxAttempts;

    /// <summary>Reads Evidence:Sarif:{Enabled,Files} (with defaults).</summary>
    public static (bool Enabled, IReadOnlyList<string> Files) ParseSarifConfig(IConfiguration config)
    {
        var section = config.GetSection("Evidence:Sarif");
        var enabled = bool.TryParse(section["Enabled"], out var e) && e;
        var files = section.GetSection("Files").GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList();
        return (enabled, files);
    }

    /// <summary>Reads Evidence:Providers as plain names (["Claude"]) or objects ([{Name,Scope}]).</summary>
    public static (IReadOnlyList<string> Names, IReadOnlyDictionary<string, EvidenceAcquisitionScope> Scopes) ParseProviderConfig(IConfiguration config)
    {
        var names = new List<string>();
        var scopes = new Dictionary<string, EvidenceAcquisitionScope>(StringComparer.OrdinalIgnoreCase);

        foreach (var child in config.GetSection("Evidence:Providers").GetChildren())
        {
            var name = child["Name"] ?? child.Value;
            if (string.IsNullOrWhiteSpace(name)) continue;
            names.Add(name);
            if (Enum.TryParse<EvidenceAcquisitionScope>(child["Scope"], ignoreCase: true, out var scope))
                scopes[name] = scope;
        }

        return (names, scopes);
    }

    public static void PrintUsage()
    {
        Console.WriteLine("Engineering Council Agent (POC)");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  review   --path <target> [options]  Produce an Engineering Review Package.");
        Console.WriteLine("  analyze  --path <target> [options]  Same pipeline, findings-oriented output.");
        Console.WriteLine("  evaluate --dataset <dir> [options]  Internal calibration run over the evaluation dataset.");
        Console.WriteLine();
        Console.WriteLine("  [--outputs <dir>] [--provider <name> | --providers a,b] [--disciplines a,b] [--mock]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --path, -p     Path to the .NET solution/repo to analyze (read-only).");
        Console.WriteLine("  --outputs, -o  Output directory (default: outputs).");
        Console.WriteLine("  --provider     An evidence provider (repeatable, e.g. --provider Claude --provider OpenAI).");
        Console.WriteLine("  --providers    Comma-separated providers, all executed (e.g. Claude,OpenAI,Sarif).");
        Console.WriteLine("  --disciplines  Comma-separated disciplines to analyze (default: all).");
        Console.WriteLine("  --discipline   A single discipline (repeatable, e.g. --discipline Security).");
        Console.WriteLine("  --sarif        Import a SARIF 2.1.0 file as evidence (repeatable). Enables the SARIF source.");
        Console.WriteLine("  --mock         Shortcut for --provider Mock (offline).");
        Console.WriteLine();
        Console.WriteLine("Configuration (appsettings.json):");
        Console.WriteLine("  Evidence:Providers   Providers every analyzer runs (default: [\"Claude\"]).");
        Console.WriteLine("  Evidence:Execution:MaxConcurrency   Max concurrent acquisition steps (default: 1 = sequential).");
        Console.WriteLine("  Evidence:Execution:ProviderTimeout   Fallback per-step timeout in seconds (default: 120); a provider's own timeout wins.");
        Console.WriteLine("  Evidence:Execution:MaxAttempts       Max acquisition attempts per step (default: 2 = one bounded retry); only timeouts are retried.");
        Console.WriteLine();
        Console.WriteLine("Environment (for the Claude/LLM provider):");
        Console.WriteLine("  OPENAI_API_KEY   Enables the LLM-backed provider (OpenAI-compatible backend).");
        Console.WriteLine("  OPENAI_MODEL     Model id (default: gpt-4o-mini).");
        Console.WriteLine("  OPENAI_ENDPOINT  Optional base endpoint (e.g. an Ollama gateway).");
        Console.WriteLine();
        Console.WriteLine("Agentic runtimes (read the repository themselves; no API key here):");
        Console.WriteLine("  --provider OpenCode   Milestone 013.2 — opencode CLI.");
        Console.WriteLine("  --provider Codex      Milestone 013.4 — the Codex CLI (authenticated via `codex login`).");
        Console.WriteLine("  --provider ClaudeCode Milestone 013.5 — the Claude Code CLI (authenticated via `claude auth`).");
        Console.WriteLine("  --provider Agentic    Milestone 013.1 — no-network demonstration provider.");
    }
}
