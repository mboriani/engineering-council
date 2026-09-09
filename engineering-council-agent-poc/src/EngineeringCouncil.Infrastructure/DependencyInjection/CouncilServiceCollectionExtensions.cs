using EngineeringCouncil.Agent.Analyzers;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Analysis;
using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Llm;
using EngineeringCouncil.Infrastructure.Acquisition;
using EngineeringCouncil.Infrastructure.Evidence;
using EngineeringCouncil.Infrastructure.GraphAssistance;
using EngineeringCouncil.Infrastructure.Interpretation;
using EngineeringCouncil.Infrastructure.Merging;
using EngineeringCouncil.Infrastructure.Reconciliation;
using EngineeringCouncil.Infrastructure.Persistence;
using EngineeringCouncil.Infrastructure.Providers;
using EngineeringCouncil.Infrastructure.Reporting;
using EngineeringCouncil.Infrastructure.Scanning;
using EngineeringCouncil.Infrastructure.Summarizing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EngineeringCouncil.Infrastructure.DependencyInjection;

public sealed class CouncilOptions
{
    /// <summary>Where run artifacts are written. Defaults to "outputs".</summary>
    public string OutputsRoot { get; set; } = "outputs";

    /// <summary>Providers to run (from <c>Evidence:Providers</c> or the CLI). Empty ⇒ ["Claude"].</summary>
    public IReadOnlyList<string> Providers { get; set; } = [];

    /// <summary>Optional per-provider acquisition-scope overrides (from config).</summary>
    public IReadOnlyDictionary<string, EvidenceAcquisitionScope> ProviderScopes { get; set; }
        = new Dictionary<string, EvidenceAcquisitionScope>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Disciplines to analyze (from the CLI). Empty ⇒ all registered disciplines.</summary>
    public IReadOnlyList<FindingCategory> Disciplines { get; set; } = [];

    /// <summary>SARIF files to import as evidence (from <c>Evidence:Sarif:Files</c> or <c>--sarif</c>).</summary>
    public IReadOnlyList<string> SarifFiles { get; set; } = [];

    /// <summary>Whether the native SARIF source is enabled (<c>Evidence:Sarif:Enabled</c>).</summary>
    public bool SarifEnabled { get; set; }

    /// <summary>Real Anthropic Claude source config (<c>Evidence:Claude</c>). Disabled by default.</summary>
    public ClaudeProviderOptions Claude { get; set; } = new();

    /// <summary>Real OpenAI source config (<c>Evidence:OpenAI</c>). Disabled by default.</summary>
    public OpenAiProviderOptions OpenAi { get; set; } = new();

    /// <summary>OpenCode agentic source config (<c>Evidence:OpenCode</c>). Disabled by default.</summary>
    public OpenCodeOptions OpenCode { get; set; } = new();

    /// <summary>Codex agentic source config (<c>Evidence:Codex</c>). Disabled by default.</summary>
    public CodexOptions Codex { get; set; } = new();

    /// <summary>Claude Code agentic source config (<c>Evidence:ClaudeCode</c>). Disabled by default.</summary>
    public ClaudeCodeOptions ClaudeCode { get; set; } = new();

    /// <summary>Targeted semantic reconciliation config (<c>Council:SemanticReconciliation</c>). Disabled by default.</summary>
    public SemanticReconciliationOptions SemanticReconciliation { get; set; } = new();

    /// <summary>Graph-assisted agentic context config (<c>GraphAssistance</c>). Disabled by default.</summary>
    public GraphAssistanceOptions GraphAssistance { get; set; } = new();

    /// <summary>What happens when a selected provider cannot execute (<c>Evidence:ProviderFailureMode</c>).</summary>
    public ProviderFailureMode ProviderFailureMode { get; set; } = ProviderFailureMode.Continue;

    /// <summary>Maximum acquisition steps executed concurrently (<c>Evidence:Execution:MaxConcurrency</c>). 1 = strictly sequential.</summary>
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>Per-step timeout fallback (<c>Evidence:Execution:ProviderTimeout</c>). A provider's own configured timeout takes precedence (M15.2B).</summary>
    public TimeSpan ProviderTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>Max acquisition attempts per logical step (<c>Evidence:Execution:MaxAttempts</c>). 2 = one bounded retry (M15.3B); 1 disables step-level retry.</summary>
    public int MaxAttempts { get; set; } = EvidenceOptions.DefaultMaxAttempts;

    /// <summary>Max files in any single context selection (<c>Evidence:Context:MaximumFiles</c>).</summary>
    public int ContextMaxFiles { get; set; } = 200;

    /// <summary>Max characters across a single context selection (<c>Evidence:Context:MaximumCharacters</c>).</summary>
    public int ContextMaxCharacters { get; set; } = 500_000;

    /// <summary>Max characters rendered for any single file (<c>Evidence:Context:MaximumCharactersPerFile</c>).
    /// The selector budgets and the renderer truncates at this same limit.</summary>
    public int ContextMaxCharactersPerFile { get; set; } = 8_000;

    /// <summary>Force the offline mock provider even if a real one is available.</summary>
    public bool ForceMockProvider { get; set; }
}

/// <summary>Describes the evidence providers actually selected, for display/attribution.</summary>
public sealed record SelectedProviders(IReadOnlyList<string> Names, bool AllMock)
{
    public string DisplayName => string.Join(", ", Names);
    public bool IsMock => AllMock;
}

public static class CouncilServiceCollectionExtensions
{
    /// <summary>
    /// Registers the whole system: scanner, evidence providers + factory, the
    /// specialized analyzers (which obtain evidence through the factory), the
    /// orchestrator, the consolidation layer, the report exporters, the run
    /// repository and the pipeline.
    ///
    /// Adding a provider = one <c>AddEvidenceProvider&lt;T&gt;()</c> line; adding an
    /// analyzer = one <c>AddAnalyzer&lt;T&gt;()</c> line. Neither touches the other.
    /// </summary>
    public static IServiceCollection AddEngineeringCouncil(
        this IServiceCollection services,
        Action<CouncilOptions>? configure = null)
    {
        var options = new CouncilOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton(new FileSystemRunRepositoryOptions { OutputsRoot = options.OutputsRoot });

        services.TryAddSingleton<IRepositoryScanner, FileSystemRepositoryScanner>();
        services.TryAddSingleton<IRepositorySnapshotIdentityProvider, RepositorySnapshotIdentityProvider>();
        services.TryAddSingleton<IEngineeringReviewMarkdownExporter, EngineeringReviewMarkdownExporter>();
        services.TryAddSingleton<IJsonReportGenerator, JsonReportGenerator>();
        services.TryAddSingleton<IAnalysisRunRepository, FileSystemAnalysisRunRepository>();

        // Discipline-aware acquisition (Milestone 008 / 011.3). ONE shared content
        // policy drives both the selector's budgeting and the renderer's truncation,
        // so selection accounting and the rendered context always agree (C4).
        services.TryAddSingleton<ContextContentPolicy>(sp =>
        {
            var evidenceOptions = sp.GetRequiredService<EvidenceOptions>();
            return new ContextContentPolicy
            {
                MaxFiles = evidenceOptions.ContextMaxFiles,
                MaxCharacters = evidenceOptions.ContextMaxCharacters,
                MaxCharactersPerFile = evidenceOptions.ContextMaxCharactersPerFile
            };
        });
        services.TryAddSingleton<IAnalysisContextSelector>(sp =>
            new RuleBasedAnalysisContextSelector(sp.GetRequiredService<ContextContentPolicy>()));
        services.TryAddSingleton<RepositoryContextBuilder>(sp =>
            new RepositoryContextBuilder(sp.GetRequiredService<ContextContentPolicy>()));
        services.TryAddSingleton<IEvidenceAcquisitionPlanner, EvidenceAcquisitionPlanner>();
        services.TryAddSingleton<IEvidenceAcquisitionExecutor, EvidenceAcquisitionExecutor>();

        // Evidence interpretation layer (Milestone 007) — Evidence → Observations.
        services.AddSingleton<IEvidenceInterpreter, StructuredLlmEvidenceInterpreter>();
        services.AddSingleton<IEvidenceInterpreter, SarifEvidenceInterpreter>(); // Milestone 009
        services.TryAddSingleton<IEvidenceInterpreterResolver, EvidenceInterpreterResolver>();
        services.TryAddSingleton<IEvidenceInterpretationPipeline, EvidenceInterpretationPipeline>();

        // Consolidation layer (Milestone 003) — rule-based, no LLM yet. The merger is
        // retained for backward compatibility; the pipeline now consolidates via the
        // deterministic multi-source reconciler (Milestone 010).
        services.TryAddSingleton<IFindingMerger, RuleBasedFindingMerger>();
        services.TryAddSingleton<IFindingReconciler, RuleBasedFindingReconciler>();
        services.TryAddSingleton<ICouncilSummaryGenerator, RuleBasedCouncilSummaryGenerator>();

        // Engineering Review Package (Milestone 006) — the primary deliverable.
        services.TryAddSingleton<IEngineeringReviewPackageBuilder, EngineeringReviewPackageBuilder>();

        AddEvidence(services, options);

        // Targeted semantic reconciliation (Milestone 014.4) — disabled by default.
        // The real implementation depends ONLY on the generic ILlmChatClient seam
        // (never a specific provider); it is registered unconditionally, but the
        // pipeline gates every invocation on SemanticReconciliationOptions.Enabled.
        services.AddSingleton(options.SemanticReconciliation);
        services.TryAddSingleton<ISemanticReconciliationReviewer>(sp =>
            new LlmSemanticReconciliationReviewer(
                sp.GetRequiredService<IClaudeClient>(), sp.GetRequiredService<SemanticReconciliationOptions>()));

        // Graph-assisted agentic context (M16.3) — disabled by default, opt-in.
        // Provides discipline-specific navigation context to agentic providers via
        // EvidenceRequest.AdditionalContext. Falls back silently on any failure.
        services.AddSingleton(options.GraphAssistance);
        services.TryAddSingleton<GraphCache>(sp =>
            new GraphCache(sp.GetRequiredService<GraphAssistanceOptions>()));
        services.TryAddSingleton<GraphifyCliRunner>(sp =>
            new GraphifyCliRunner(sp.GetRequiredService<GraphAssistanceOptions>()));
        services.TryAddSingleton<IGraphContextProvider, GraphContextProvider>();

        // Specialized analyzers — they obtain evidence through the factory only.
        services.AddAnalyzer<ArchitectureAnalyzer>();
        services.AddAnalyzer<CodeQualityAnalyzer>();
        services.AddAnalyzer<ReliabilityAnalyzer>();
        services.AddAnalyzer<SecurityAnalyzer>();
        services.AddAnalyzer<TestingAnalyzer>();
        services.AddAnalyzer<DocumentationAnalyzer>();
        services.AddAnalyzer<ObservabilityAnalyzer>();

        services.AddSingleton<AnalysisOrchestrator>();
        services.AddSingleton<AnalysisPipeline>();

        return services;
    }

    /// <summary>Registers an analyzer as one of the <c>IEnumerable&lt;IAnalyzerAgent&gt;</c>.</summary>
    public static IServiceCollection AddAnalyzer<TAnalyzer>(this IServiceCollection services)
        where TAnalyzer : class, IAnalyzerAgent
    {
        services.AddSingleton<IAnalyzerAgent, TAnalyzer>();
        return services;
    }

    /// <summary>Registers a provider as one of the <c>IEnumerable&lt;IEvidenceProvider&gt;</c>.</summary>
    public static IServiceCollection AddEvidenceProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, IEvidenceProvider
    {
        services.AddSingleton<IEvidenceProvider, TProvider>();
        return services;
    }

    private static void AddEvidence(IServiceCollection services, CouncilOptions options)
    {
        // Native SARIF source (Milestone 009) — real provider, imports SARIF files.
        var sarifOptions = new SarifOptions { Enabled = options.SarifEnabled, Files = options.SarifFiles };
        var effective = ResolveProviders(options, sarifOptions);

        // Real LLM sources (Milestone 011): disabled by default so nothing leaves the
        // machine implicitly. EXPLICITLY selecting one (CLI/config provider list) opts
        // in, so the only remaining blocker is the API key — reported by name, never
        // by value. Keys are read from the configured environment variable at call time.
        if (effective.Contains("Claude", StringComparer.OrdinalIgnoreCase)) options.Claude.Enabled = true;
        if (effective.Contains("OpenAI", StringComparer.OrdinalIgnoreCase)) options.OpenAi.Enabled = true;

        // OpenCode agentic source (Milestone 013.2): an external runtime, disabled by
        // default. EXPLICITLY selecting it (--provider OpenCode / config) opts in; the
        // only remaining blocker is the executable being installed/on PATH.
        if (effective.Contains("OpenCode", StringComparer.OrdinalIgnoreCase)) options.OpenCode.Enabled = true;

        // Codex agentic source (Milestone 013.4): an external runtime, disabled by
        // default. EXPLICITLY selecting it (--provider Codex / config) opts in; the
        // only remaining blocker is the executable being installed/on PATH.
        if (effective.Contains("Codex", StringComparer.OrdinalIgnoreCase)) options.Codex.Enabled = true;

        // Claude Code agentic source (Milestone 013.5): an external runtime, disabled
        // by default. EXPLICITLY selecting it (--provider ClaudeCode / config) opts
        // in; the only remaining blocker is the executable being installed/on PATH.
        if (effective.Contains("ClaudeCode", StringComparer.OrdinalIgnoreCase)) options.ClaudeCode.Enabled = true;

        services.AddSingleton(options.Claude);
        services.AddSingleton(options.OpenAi);
        services.AddSingleton(options.OpenCode);
        services.AddSingleton(options.Codex);
        services.AddSingleton(options.ClaudeCode);
        services.TryAddSingleton<IEvidencePromptBuilder, DisciplineEvidencePromptBuilder>();
        services.TryAddSingleton<IClaudeClient>(sp => new ClaudeClient(sp.GetRequiredService<ClaudeProviderOptions>()));
        services.TryAddSingleton<IOpenAiClient>(sp => new OpenAiClient(sp.GetRequiredService<OpenAiProviderOptions>()));
        services.TryAddSingleton<IOpenCodeProcessRunner, OpenCodeProcessRunner>();
        services.TryAddSingleton<ICodexProcessRunner, CodexProcessRunner>();
        services.TryAddSingleton<IClaudeCodeProcessRunner, ClaudeCodeProcessRunner>();

        // Register every provider independently. Adding one here (or from another
        // assembly via AddEvidenceProvider) never touches analyzers.
        services.AddEvidenceProvider<ClaudeEvidenceProvider>();
        services.AddEvidenceProvider<OpenAiEvidenceProvider>();
        services.AddEvidenceProvider<MockEvidenceProvider>();
        services.AddEvidenceProvider<AgenticEvidenceProvider>();
        services.AddEvidenceProvider<OpenCodeEvidenceProvider>();
        services.AddEvidenceProvider<CodexEvidenceProvider>();
        services.AddEvidenceProvider<ClaudeCodeEvidenceProvider>();
        services.AddEvidenceProvider<OllamaEvidenceProvider>();
        services.AddEvidenceProvider<RoslynEvidenceProvider>();
        services.AddEvidenceProvider<SonarEvidenceProvider>();
        services.AddEvidenceProvider<SemgrepEvidenceProvider>();
        services.AddEvidenceProvider<NDependEvidenceProvider>();
        services.AddEvidenceProvider<GitEvidenceProvider>();
        services.AddEvidenceProvider<CoverageEvidenceProvider>();

        services.AddSingleton(sarifOptions);
        services.AddEvidenceProvider<SarifEvidenceProvider>();

        services.TryAddSingleton<IEvidenceProviderFactory, EvidenceProviderFactory>();

        var allMock = effective.All(p => string.Equals(p, "Mock", StringComparison.OrdinalIgnoreCase));

        services.AddSingleton(new EvidenceOptions
        {
            Providers = effective,
            ProviderScopes = options.ProviderScopes,
            Disciplines = options.Disciplines,
            ContextMaxFiles = options.ContextMaxFiles,
            ContextMaxCharacters = options.ContextMaxCharacters,
            ContextMaxCharactersPerFile = options.ContextMaxCharactersPerFile,
            ProviderFailureMode = options.ProviderFailureMode,
            MaxConcurrency = options.MaxConcurrency,
            ProviderTimeout = options.ProviderTimeout,
            MaxAttempts = options.MaxAttempts
        });
        services.AddSingleton(new SelectedProviders(effective, allMock));
    }

    /// <summary>
    /// Computes the providers actually run. Honors an explicit list; auto-includes the
    /// SARIF source when it is active; de-duplicates; never returns empty.
    ///
    /// Naming (Milestone 013.4): "Codex" is now the REAL agentic Codex CLI provider,
    /// NOT an alias for the OpenAI API provider. A code-focused model of the direct
    /// API provider is selected as "OpenAI" with <c>Evidence:OpenAI:Model</c>; the
    /// legacy "Codex ⇒ OpenAI" alias was removed so the two providers are unambiguous.
    ///
    /// An EXPLICITLY selected real LLM source is never silently swapped for Mock
    /// (Milestone 011): it stays selected so the run reports a clear, categorized
    /// reason (e.g. the missing environment variable) per the configured
    /// <see cref="ProviderFailureMode"/>. External providers are never the
    /// zero-config default — an unconfigured run uses the offline Mock.
    /// </summary>
    private static IReadOnlyList<string> ResolveProviders(CouncilOptions options, SarifOptions sarif)
    {
        IEnumerable<string> requested =
            options.ForceMockProvider ? ["Mock"]
            : options.Providers.Count > 0 ? options.Providers
            : sarif.IsActive ? []           // SARIF-only run when files are configured
            : ["Mock"];                     // zero-config default stays offline

        var effective = requested
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        // Ensure SARIF participates whenever it is active, even if not listed explicitly.
        if (sarif.IsActive && !effective.Any(p => string.Equals(p, "SARIF", StringComparison.OrdinalIgnoreCase)))
            effective.Add("SARIF");

        effective = effective.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return effective.Count > 0 ? effective : ["Mock"];
    }
}
