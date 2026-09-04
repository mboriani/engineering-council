using EngineeringCouncil.Agent.Analyzers;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Analysis;
using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Core.Serialization;
using EngineeringCouncil.Infrastructure.Acquisition;
using EngineeringCouncil.Infrastructure.Evidence;
using EngineeringCouncil.Infrastructure.Interpretation;
using EngineeringCouncil.Infrastructure.Persistence;
using EngineeringCouncil.Infrastructure.Reconciliation;
using EngineeringCouncil.Infrastructure.Reporting;
using EngineeringCouncil.Infrastructure.Scanning;
using EngineeringCouncil.Infrastructure.Summarizing;
using EngineeringCouncil.Tests.Contracts;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 013.1 — agentic evidence contract. An agentic source (Codex, Claude
/// Code, opencode, …) is a first-class <see cref="EvidenceProviderType.Agentic"/>
/// provider that explores the repository itself: the council hands it the repository
/// root, discipline, instructions, correlation metadata and output contract — but NOT
/// a bundled controlled context and NOT a fabricated context fingerprint. The same
/// Evidence abstraction and structured interpreter are reused; M12 comparison still
/// excludes non-LLM executions.
/// </summary>
public sealed class AgenticEvidenceTests
{
    private static RepositorySnapshot Snapshot => new()
    {
        RootPath = "/repo",
        SolutionName = "Repo",
        ProjectFiles = ["src/App.csproj"],
        Files =
        [
            new ScannedFile { RelativePath = "src/App.csproj", Extension = ".csproj", SizeBytes = 1, LineCount = 5, Content = "<Project/>" },
            new ScannedFile { RelativePath = "src/Auth.cs", Extension = ".cs", SizeBytes = 1, LineCount = 40, Content = "public class Auth { void Authorize(){} } // Authentication ApiKey" },
            new ScannedFile { RelativePath = "tests/AuthTests.cs", Extension = ".cs", SizeBytes = 1, LineCount = 20, Content = "[Fact] void T(){ Assert.True(true); } // xunit" },
            new ScannedFile { RelativePath = "README.md", Extension = ".md", SizeBytes = 1, LineCount = 10, Content = "# Repo" },
        ]
    };

    // ── Provider type & metadata ─────────────────────────────────────────────

    [Fact] // 1
    public void Agentic_is_a_distinct_provider_type_never_an_llm()
    {
        Assert.Equal(6, (int)EvidenceProviderType.Agentic);
        Assert.NotEqual(EvidenceProviderType.LLM, EvidenceProviderType.Agentic);
        Assert.NotEqual(EvidenceProviderType.StaticAnalyzer, EvidenceProviderType.Agentic);
        Assert.NotEqual(EvidenceProviderType.Custom, EvidenceProviderType.Agentic);
    }

    [Fact] // 2
    public void Agentic_provider_metadata_declares_discipline_scope_and_instructions()
    {
        var metadata = new AgenticEvidenceProvider().Metadata;

        Assert.Equal(EvidenceProviderType.Agentic, metadata.ProviderType);
        Assert.Equal(EvidenceAcquisitionScope.Discipline, metadata.DefaultAcquisitionScope);
        Assert.True(metadata.RequiresAnalyzerInstructions);
        Assert.False(metadata.SupportsRepositoryWideAnalysis);
        Assert.False(string.IsNullOrWhiteSpace(metadata.Version)); // the agent runtime id
        Assert.True(new AgenticEvidenceProvider().IsAvailable);
    }

    // ── Planner: metadata-driven, never name-based ───────────────────────────

    [Fact] // 3
    public void Planner_creates_one_discipline_step_per_selected_discipline_for_an_agentic_provider()
    {
        var provider = new AgenticEvidenceProvider();
        var plan = Plan([provider], [FindingCategory.Security, FindingCategory.Testing]);

        Assert.Equal(2, plan.Steps.Count);
        Assert.All(plan.Steps, s => Assert.Equal(EvidenceAcquisitionScope.Discipline, s.Scope));
        Assert.All(plan.Steps, s => Assert.NotNull(s.Discipline));
        Assert.Equal(2, plan.DisciplineScopedSteps);
    }

    [Fact] // 4
    public void Planner_never_branches_on_provider_name_an_agentic_provider_is_scope_from_metadata()
    {
        // An agentic provider whose NAME looks like a static repository-wide tool must
        // still be planned as Discipline-scoped — the planner reads metadata only.
        var agentic = new RecordingAgenticProvider("Sonar");
        var plan = Plan([agentic], [FindingCategory.Security]);

        Assert.Equal(1, plan.DisciplineScopedSteps);
        Assert.Equal(0, plan.RepositoryScopedSteps);
        Assert.Single(plan.Steps, s => s.Discipline == FindingCategory.Security);
    }

    // ── Executor: agentic request semantics ──────────────────────────────────

    [Fact] // 5
    public async Task Agentic_request_does_not_embed_repository_contents()
    {
        var agentic = new RecordingAgenticProvider("Agentic");
        var result = await Executor(agentic).ExecuteAsync(
            Plan([agentic], [FindingCategory.Security]), Snapshot);

        var request = agentic.LastRequest!;
        Assert.NotNull(request);
        Assert.Empty(request.ContextSelection.Files);
        Assert.Equal(0, request.ContextSelection.SelectedFileCount);
        Assert.Equal(0, request.ContextSelection.EstimatedContentSize);
        Assert.Equal(Snapshot.Files.Count, request.ContextSelection.TotalRepositoryFiles); // scope still recorded
        Assert.All(request.RepositorySnapshot.Files, f => Assert.Null(f.Content)); // no file bodies
        Assert.Equal("agentic", request.ContextSelection.Strategy);
    }

    [Fact] // 6
    public async Task Agentic_request_carries_repository_identity_discipline_and_instructions()
    {
        var agentic = new RecordingAgenticProvider("Agentic");
        var result = await Executor(agentic).ExecuteAsync(
            Plan([agentic], [FindingCategory.Security]), Snapshot);

        var request = agentic.LastRequest!;
        Assert.Equal("/repo", request.RepositorySnapshot.RootPath);
        Assert.Equal("Repo", request.RepositorySnapshot.SolutionName);
        Assert.Equal(FindingCategory.Security, request.Discipline);
        Assert.False(string.IsNullOrWhiteSpace(request.Instructions));
        Assert.False(string.IsNullOrWhiteSpace(request.CorrelationId));
        Assert.Equal(EvidenceAcquisitionScope.Discipline, request.Scope);
        Assert.Equal("Agentic", Assert.Single(request.ProviderNames));
    }

    [Fact] // 7
    public async Task Executor_does_not_fabricate_a_context_fingerprint_for_agentic_steps()
    {
        var agentic = new RecordingAgenticProvider("Agentic");
        var result = await Executor(agentic).ExecuteAsync(
            Plan([agentic], [FindingCategory.Security]), Snapshot);

        var record = Assert.Single(result.Executions);
        Assert.Equal(string.Empty, record.ContextFingerprint);
        Assert.Equal(string.Empty, Assert.Single(result.Evidence).ContextFingerprint);
    }

    [Fact] // 8
    public async Task Agentic_telemetry_marks_autonomous_exploration_and_the_provider_type()
    {
        var agentic = new RecordingAgenticProvider("Agentic");
        var result = await Executor(agentic).ExecuteAsync(
            Plan([agentic], [FindingCategory.Security]), Snapshot);

        var record = Assert.Single(result.Executions);
        Assert.Equal(EvidenceProviderType.Agentic, record.ProviderType);
        Assert.Equal("agentic", record.ContextSelectionStrategy);
        Assert.Equal(0, record.ContextFileCount);
        Assert.Equal(0, record.ContextCharacterCount);
        Assert.Equal(Snapshot.Files.Count, record.ContextFilesConsidered);

        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(EvidenceProviderType.Agentic, evidence.ProviderType);
        Assert.Equal("agentic", evidence.ContextSelectionStrategy);
    }

    [Fact] // 9
    public async Task Unavailable_agentic_provider_never_fabricates_a_context_fingerprint()
    {
        var down = new UnavailableAgenticProvider("Down");
        var result = await Executor(down).ExecuteAsync(Plan([down], [FindingCategory.Security]), Snapshot);

        var record = Assert.Single(result.Executions);
        Assert.False(record.Success);
        Assert.Equal(EvidenceProviderType.Agentic, record.ProviderType);
        Assert.Equal(string.Empty, record.ContextFingerprint);
        Assert.Equal("agentic", record.ContextSelectionStrategy);

        var evidence = Assert.Single(result.Evidence);
        Assert.False(evidence.Success);
        Assert.Equal(string.Empty, evidence.ContextFingerprint);
    }

    // ── Structured interpretation reuse ──────────────────────────────────────

    [Fact] // 10
    public async Task Agentic_evidence_flows_through_the_structured_interpreter()
    {
        var evidence = AgenticEvidence(FindingCategory.Security, "Security", "Medium");
        var observations = await Interpret(evidence);

        var observation = Assert.Single(observations);
        Assert.Equal(FindingCategory.Security, observation.Discipline);
        Assert.Equal("HardcodedSecret", observation.ObservationType);
        Assert.Equal(EvidenceProviderType.Agentic, observation.SourceProviderType);
        Assert.Equal("Agentic", observation.SourceProvider);
        Assert.Contains(observation.FileReferences, f => f.Path == "src/Auth.cs"); // real file kept
        Assert.DoesNotContain(observation.FileReferences, f => f.Path == "src/Invented.cs"); // invented dropped
    }

    [Fact] // 11
    public async Task Discipline_mismatch_rule_still_applies_to_agentic_evidence()
    {
        var evidence = AgenticEvidence(requested: FindingCategory.Security, observationDiscipline: "Architecture", confidence: "High");
        var observation = Assert.Single(await Interpret(evidence));

        Assert.Equal(FindingCategory.Architecture, observation.Discipline); // not rewritten
        Assert.Contains("discipline-mismatch", observation.Tags);
        Assert.True(observation.Confidence < FindingConfidence.High);
    }

    // ── Real provider ────────────────────────────────────────────────────────

    [Fact] // 12
    public async Task Real_agentic_provider_explores_the_file_map_and_returns_structured_evidence()
    {
        var provider = new AgenticEvidenceProvider();
        var focused = Snapshot with
        {
            Files =
            [
                new ScannedFile { RelativePath = "src/Auth.cs", Extension = ".cs", SizeBytes = 1, LineCount = 40, Content = null }
            ]
        };
        var request = new EvidenceRequest
        {
            RunId = "run1",
            RepositorySnapshot = focused,
            Scope = EvidenceAcquisitionScope.Discipline,
            Discipline = FindingCategory.Security,
            Instructions = "Analyze security.",
            ContextSelection = AgenticSelection(focused),
            ProviderNames = ["Agentic"],
            CorrelationId = "corr-1"
        };

        var evidence = Assert.Single(await provider.CollectAsync(request));
        Assert.Equal(EvidenceProviderType.Agentic, evidence.ProviderType);
        Assert.Equal("Agentic", evidence.ProviderName);
        Assert.True(evidence.Success);
        Assert.Equal("agentic-explorer-v1", evidence.ProviderVersion);
        Assert.Equal("agentic-explorer-v1", evidence.Metadata["agentRuntime"]);

        var observations = await Interpret(evidence with
        {
            Success = true,
            AcquisitionScope = EvidenceAcquisitionScope.Discipline,
            RequestedDiscipline = FindingCategory.Security,
            CorrelationId = "corr-1"
        });
        var observation = Assert.Single(observations);
        Assert.Equal(FindingCategory.Security, observation.Discipline);
        Assert.Equal("src/Auth.cs", Assert.Single(observation.FileReferences).Path); // explored by the agent, real file
    }

    // ── M12 comparison boundary ──────────────────────────────────────────────

    [Fact] // 13
    public void Provider_comparison_excludes_agentic_executions()
    {
        var run = Run(
        [
            Rec("Claude", FindingCategory.Security, type: EvidenceProviderType.LLM),
            Rec("OpenAI", FindingCategory.Security, type: EvidenceProviderType.LLM),
            Rec("Agentic", FindingCategory.Security, type: EvidenceProviderType.Agentic)
        ]);

        var report = ProviderComparisonBuilder.Build(run)!;

        var discipline = Assert.Single(report.DisciplineComparisons);
        Assert.Equal(["Claude", "OpenAI"], discipline.Providers);
        Assert.DoesNotContain(report.ComparedProviders, p => p == "Agentic");
        Assert.All(report.OverallExecutionMetrics, m => Assert.NotEqual("Agentic", m.ProviderName));
    }

    // ── End to end: real pipeline with the agentic provider ──────────────────

    [Fact]
    public async Task Agentic_provider_end_to_end_produces_observations_findings_and_a_package()
    {
        Directory.CreateDirectory(Path.Combine(_repo, "src"));
        Directory.CreateDirectory(Path.Combine(_repo, "tests"));
        File.WriteAllText(Path.Combine(_repo, "Sample.sln"), "solution\n");
        File.WriteAllText(Path.Combine(_repo, "src", "Auth.cs"), "public class Auth { void Authorize() { } }\n");
        File.WriteAllText(Path.Combine(_repo, "tests", "AuthTests.cs"), "[Fact] public void T() { }\n");

        var provider = new AgenticEvidenceProvider();
        var pipeline = BuildPipeline([provider], [FindingCategory.Security, FindingCategory.Testing]);
        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = _repo, ProviderName = "Agentic" });

        Assert.True(result.Run.Observations.Count >= 2); // the agent explores every file it deems relevant
        Assert.All(result.Run.Observations, o => Assert.Equal(EvidenceProviderType.Agentic, o.SourceProviderType));
        Assert.Contains(result.Run.Observations, o => o.Discipline == FindingCategory.Security);
        Assert.Contains(result.Run.Observations, o => o.Discipline == FindingCategory.Testing);

        Assert.Contains(result.Run.Findings, f => f.Category == FindingCategory.Security);
        Assert.Contains(result.Run.Findings, f => f.Category == FindingCategory.Testing);

        Assert.All(result.Run.ProviderExecution!.Records, r =>
        {
            Assert.Equal(EvidenceProviderType.Agentic, r.ProviderType);
            Assert.Equal(string.Empty, r.ContextFingerprint);
        });

        var packageJson = File.ReadAllText(Path.Combine(result.OutputDirectory, "engineering-review-package.json"));
        var dto = System.Text.Json.JsonSerializer.Deserialize<PackageContract>(packageJson, CouncilJson.Options)!;
        Assert.Equal("1.1", dto.SchemaVersion);
        Assert.DoesNotContain("providerComparison", packageJson, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private readonly string _repo = Path.Combine(Path.GetTempPath(), "ec-agentic-repo-" + Guid.NewGuid().ToString("N"));
    private readonly string _outputs = Path.Combine(Path.GetTempPath(), "ec-agentic-out-" + Guid.NewGuid().ToString("N"));

    private static AnalysisRunConfiguration Config() => new() { RunId = "run1" };

    private static EvidenceAcquisitionPlan Plan(
        IReadOnlyCollection<IEvidenceProvider> providers, IReadOnlyCollection<FindingCategory> disciplines)
        => new EvidenceAcquisitionPlanner().CreatePlan(Config(), Snapshot, providers, disciplines);

    private static EvidenceAcquisitionExecutor Executor(params IEvidenceProvider[] providers)
        => new(new EvidenceProviderFactory(providers), new RuleBasedAnalysisContextSelector(),
            new EvidenceOptions { ProviderTimeout = TimeSpan.FromSeconds(5) });

    private static AnalysisContextSelection AgenticSelection(RepositorySnapshot repository) => new()
    {
        Strategy = "agentic",
        Files = [],
        TotalRepositoryFiles = repository.Files.Count,
        SelectedFileCount = 0,
        EstimatedContentSize = 0
    };

    private static async Task<IReadOnlyList<EngineeringObservation>> Interpret(Evidence evidence)
        => await new StructuredLlmEvidenceInterpreter().InterpretAsync(
            evidence, new AnalyzerContext { Snapshot = Snapshot, RunId = "run1" });

    private static Evidence AgenticEvidence(FindingCategory requested, string observationDiscipline, string confidence)
        => new()
        {
            ProviderName = "Agentic",
            ProviderType = EvidenceProviderType.Agentic,
            Success = true,
            AcquisitionScope = EvidenceAcquisitionScope.Discipline,
            RequestedDiscipline = requested,
            RawResponse = $$"""
                {
                  "observations": [
                    {
                      "type": "HardcodedSecret",
                      "discipline": "{{observationDiscipline}}",
                      "title": "Sample",
                      "description": "A sample observation.",
                      "severity": "Medium",
                      "confidence": "{{confidence}}",
                      "fileReferences": [ { "path": "src/Auth.cs" }, { "path": "src/Invented.cs" } ]
                    }
                  ]
                }
                """
        };

    private static ProviderExecutionRecord Rec(
        string provider, FindingCategory? discipline = FindingCategory.Security,
        EvidenceProviderType type = EvidenceProviderType.LLM)
        => new()
        {
            RunId = "run-test",
            ProviderName = provider,
            ProviderId = provider,
            ProviderVersion = "test-model",
            ProviderType = type,
            RequestedDiscipline = discipline,
            Success = true,
            ContextFileCount = 1,
            ContextCharacterCount = 100,
            ContextFingerprint = "CTX-abc123",
            Duration = TimeSpan.FromMilliseconds(10)
        };

    private static AnalysisRun Run(IReadOnlyList<ProviderExecutionRecord> records) => new()
    {
        RunId = "run-test",
        TargetPath = "/repo",
        SolutionName = "Repo",
        Branch = "main",
        Commit = "abc123",
        ProviderExecution = new ProviderExecutionReport { Records = records },
        Observations = [],
        RawFindings = [],
        Findings = []
    };

    private AnalysisPipeline BuildPipeline(IReadOnlyList<IEvidenceProvider> providers, IReadOnlyList<FindingCategory> disciplines)
    {
        var evidenceOptions = new EvidenceOptions
        {
            Providers = providers.Select(p => p.Metadata.Name).ToList(),
            Disciplines = disciplines,
            ProviderFailureMode = ProviderFailureMode.Continue
        };
        var factory = new EvidenceProviderFactory(providers);

        return new AnalysisPipeline(
            new FileSystemRepositoryScanner(), factory,
            new EvidenceAcquisitionPlanner(),
            new EvidenceAcquisitionExecutor(factory, new RuleBasedAnalysisContextSelector(), evidenceOptions),
            new EvidenceInterpretationPipeline(new EvidenceInterpreterResolver(
                [new StructuredLlmEvidenceInterpreter()])),
            new AnalysisOrchestrator([new SecurityAnalyzer(), new TestingAnalyzer()]),
            new RuleBasedFindingReconciler(),
            new RuleBasedCouncilSummaryGenerator(),
            new EngineeringReviewPackageBuilder(),
            evidenceOptions,
            new FileSystemAnalysisRunRepository(
                new FileSystemRunRepositoryOptions { OutputsRoot = _outputs },
                new EngineeringReviewMarkdownExporter(), new JsonReportGenerator()));
    }

    private sealed class RecordingAgenticProvider(string name) : IEvidenceProvider
    {
        public EvidenceRequest? LastRequest { get; private set; }
        public bool IsAvailable => true;
        public string? UnavailableReason => null;

        public EvidenceProviderMetadata Metadata => new()
        {
            Name = name,
            ProviderType = EvidenceProviderType.Agentic,
            DefaultAcquisitionScope = EvidenceAcquisitionScope.Discipline,
            RequiresAnalyzerInstructions = true,
            SupportsRepositoryWideAnalysis = false,
            Version = "agentic-test-v1"
        };

        public Task<IReadOnlyList<Evidence>> CollectAsync(EvidenceRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult<IReadOnlyList<Evidence>>(
                [new Evidence { ProviderName = name, ProviderType = EvidenceProviderType.Agentic, RawResponse = "{\"observations\":[]}" }]);
        }
    }

    private sealed class UnavailableAgenticProvider(string name) : IEvidenceProvider
    {
        public bool IsAvailable => false;
        public string? UnavailableReason => "Agentic unavailable for this test.";

        public EvidenceProviderMetadata Metadata => new()
        {
            Name = name,
            ProviderType = EvidenceProviderType.Agentic,
            DefaultAcquisitionScope = EvidenceAcquisitionScope.Discipline,
            RequiresAnalyzerInstructions = true,
            SupportsRepositoryWideAnalysis = false,
            Version = "agentic-test-v1"
        };

        public Task<IReadOnlyList<Evidence>> CollectAsync(EvidenceRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("should not be called");
    }
}
