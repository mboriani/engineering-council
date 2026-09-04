using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Analysis;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Acquisition;
using EngineeringCouncil.Infrastructure.Evidence;
using EngineeringCouncil.Infrastructure.Interpretation;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 008 — discipline-aware evidence acquisition. Covers the planner
/// (metadata-driven topology, unsupported combinations, determinism, scope
/// overrides), the rule-based context selector (relevance + limits + reasons),
/// the executor (run-scoped telemetry ownership, isolation), the interpreter's
/// discipline-mismatch rule, and coverage rollup.
/// </summary>
public sealed class AcquisitionTests
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

    private static readonly FindingCategory[] AllDisciplines =
    [
        FindingCategory.Architecture, FindingCategory.CodeQuality, FindingCategory.Reliability,
        FindingCategory.Security, FindingCategory.Testing, FindingCategory.Documentation, FindingCategory.Observability
    ];

    // ── Planner ──────────────────────────────────────────────────────────────

    [Fact] // 1
    public void Repository_scoped_provider_gets_one_step()
    {
        var plan = Plan([Provider("Sonar", EvidenceAcquisitionScope.Repository)], AllDisciplines);

        var step = Assert.Single(plan.Steps);
        Assert.Equal(EvidenceAcquisitionScope.Repository, step.Scope);
        Assert.Null(step.Discipline);
    }

    [Fact] // 2
    public void Discipline_scoped_provider_gets_one_step_per_selected_discipline()
    {
        var plan = Plan([Provider("Claude", EvidenceAcquisitionScope.Discipline)], AllDisciplines);

        Assert.Equal(AllDisciplines.Length, plan.Steps.Count);
        Assert.All(plan.Steps, s => Assert.Equal(EvidenceAcquisitionScope.Discipline, s.Scope));
        Assert.All(plan.Steps, s => Assert.NotNull(s.Discipline));
    }

    [Fact] // 3
    public void Only_the_selected_disciplines_are_planned()
    {
        var plan = Plan(
            [Provider("Claude", EvidenceAcquisitionScope.Discipline)],
            [FindingCategory.Architecture, FindingCategory.Security]);

        Assert.Equal(2, plan.Steps.Count);
        Assert.Collection(plan.Steps.Select(s => s.Discipline),
            d => Assert.Equal(FindingCategory.Architecture, d),
            d => Assert.Equal(FindingCategory.Security, d));
    }

    [Fact] // 4
    public void Unsupported_discipline_creates_no_step_and_is_recorded()
    {
        var provider = Provider("Claude", EvidenceAcquisitionScope.Discipline,
            supported: new HashSet<FindingCategory> { FindingCategory.Architecture });
        var plan = Plan([provider], [FindingCategory.Architecture, FindingCategory.Security]);

        var step = Assert.Single(plan.Steps);
        Assert.Equal(FindingCategory.Architecture, step.Discipline);
        Assert.Contains(plan.UnsupportedCombinations, u => u.Contains("Security"));
    }

    [Fact] // 5
    public void Two_discipline_and_one_repository_provider_form_the_right_matrix()
    {
        var plan = Plan(
        [
            Provider("Claude", EvidenceAcquisitionScope.Discipline),
            Provider("Codex", EvidenceAcquisitionScope.Discipline),
            Provider("Sonar", EvidenceAcquisitionScope.Repository),
        ], AllDisciplines);

        Assert.Equal(AllDisciplines.Length * 2 + 1, plan.Steps.Count);
        Assert.Equal(2 * AllDisciplines.Length, plan.DisciplineScopedSteps);
        Assert.Equal(1, plan.RepositoryScopedSteps);
    }

    [Fact] // 6
    public void Plan_is_deterministic_for_the_same_inputs()
    {
        IReadOnlyCollection<IEvidenceProvider> providers =
            [Provider("Claude", EvidenceAcquisitionScope.Discipline), Provider("Sonar", EvidenceAcquisitionScope.Repository)];
        var planner = new EvidenceAcquisitionPlanner();

        var a = planner.CreatePlan(Config(), Snapshot, providers, AllDisciplines);
        var b = planner.CreatePlan(Config(), Snapshot, providers, AllDisciplines);

        Assert.Equal(a.Steps.Select(s => s.StepId), b.Steps.Select(s => s.StepId));
    }

    [Fact] // 7
    public void Config_scope_override_flips_a_provider_to_repository()
    {
        var config = Config() with
        {
            ProviderScopeOverrides = new Dictionary<string, EvidenceAcquisitionScope> { ["Claude"] = EvidenceAcquisitionScope.Repository }
        };
        var plan = new EvidenceAcquisitionPlanner().CreatePlan(
            config, Snapshot, [Provider("Claude", EvidenceAcquisitionScope.Discipline)], AllDisciplines);

        Assert.Equal(1, plan.RepositoryScopedSteps);
        Assert.Equal(0, plan.DisciplineScopedSteps);
    }

    [Fact] // 8
    public void Planner_never_branches_on_provider_name_default_comes_from_metadata()
    {
        // A provider "Sonar" declared Discipline-scoped is treated as Discipline —
        // the planner reads metadata, not the name.
        var plan = Plan([Provider("Sonar", EvidenceAcquisitionScope.Discipline)], [FindingCategory.Security]);
        Assert.Equal(1, plan.DisciplineScopedSteps);
    }

    // ── Context selector ─────────────────────────────────────────────────────

    [Fact] // 9
    public void Selector_chooses_discipline_relevant_files()
    {
        var selector = new RuleBasedAnalysisContextSelector();

        var security = selector.Select(Snapshot, FindingCategory.Security);
        Assert.Contains(security.Files, f => f.RelativePath == "src/Auth.cs");
        Assert.Contains("focused", security.Strategy);

        var testing = selector.Select(Snapshot, FindingCategory.Testing);
        Assert.Contains(testing.Files, f => f.RelativePath == "tests/AuthTests.cs");
    }

    [Fact] // 10
    public void Repository_wide_selection_uses_the_repository_strategy()
    {
        var repo = new RuleBasedAnalysisContextSelector().Select(Snapshot, discipline: null);
        Assert.Equal("repository-wide", repo.Strategy);
        Assert.Equal(Snapshot.Files.Count, repo.TotalRepositoryFiles);
    }

    [Fact] // 11
    public void Selection_respects_the_file_limit_and_records_the_exclusion()
    {
        var selector = new RuleBasedAnalysisContextSelector(new ContextContentPolicy { MaxFiles = 1, MaxCharacters = 1_000_000 });
        var selection = selector.Select(Snapshot, discipline: null);

        Assert.Equal(1, selection.SelectedFileCount);
        Assert.Single(selection.Files);
        Assert.Contains(selection.SelectionReasons, r => r.Contains("file limit"));
    }

    [Fact] // 12
    public void Selection_respects_the_character_limit_and_records_the_exclusion()
    {
        // A tiny character budget still includes the first (whole) file, then excludes the rest.
        var selector = new RuleBasedAnalysisContextSelector(new ContextContentPolicy { MaxFiles = 100, MaxCharacters = 5 });
        var selection = selector.Select(Snapshot, discipline: null);

        Assert.True(selection.SelectedFileCount >= 1);
        Assert.True(selection.SelectedFileCount < Snapshot.Files.Count);
        Assert.Contains(selection.SelectionReasons, r => r.Contains("character limit"));
    }

    [Fact] // 13
    public void Selection_is_deterministic()
    {
        var selector = new RuleBasedAnalysisContextSelector();
        var a = selector.Select(Snapshot, FindingCategory.Security);
        var b = selector.Select(Snapshot, FindingCategory.Security);
        Assert.Equal(a.Files.Select(f => f.RelativePath), b.Files.Select(f => f.RelativePath));
    }

    // ── Milestone 011.3 — context budget correctness (C4, C6) ─────────────────

    [Fact] // C4
    public void Selector_budgets_effective_size_not_raw_file_length()
    {
        // Every file is longer than the per-file render limit, so selection must
        // budget each file by its EFFECTIVE (rendered) size, not its full length.
        var snapshot = new RepositorySnapshot
        {
            RootPath = "/repo",
            SolutionName = "Repo",
            Files = Enumerable.Range(0, 3)
                .Select(i => new ScannedFile
                {
                    RelativePath = $"src/f{i}.cs",
                    Extension = ".cs",
                    SizeBytes = 1,
                    LineCount = 5,
                    Content = new string('a', 100)
                })
                .ToList()
        };
        var policy = new ContextContentPolicy { MaxFiles = 10, MaxCharacters = 150, MaxCharactersPerFile = 60 };
        var selection = new RuleBasedAnalysisContextSelector(policy).Select(snapshot, discipline: null);

        // Each file contributes min(100, 60) = 60; two fit (120 ≤ 150), the third does not.
        Assert.Equal(2, selection.SelectedFileCount);
        Assert.Equal(120, (int)selection.EstimatedContentSize);
        Assert.Contains(selection.SelectionReasons, r => r.Contains("character limit"));
    }

    [Fact] // C4
    public void Selection_budget_matches_the_rendered_context_size()
    {
        var policy = new ContextContentPolicy { MaxFiles = 200, MaxCharacters = 500_000, MaxCharactersPerFile = 8 };
        var selection = new RuleBasedAnalysisContextSelector(policy).Select(Snapshot, discipline: null);
        var rendered = new RepositoryContextBuilder(policy).Build(Snapshot, selection);

        // Selection's character accounting equals the sum of the per-file-limited
        // sizes the renderer actually produces — budgeting and rendering agree.
        var expectedBudget = selection.Files.Sum(f => Math.Min(f.Content?.Length ?? 0, 8));
        Assert.Equal(expectedBudget, (int)selection.EstimatedContentSize);
        Assert.Contains("…truncated…", rendered); // fixture files exceed the per-file limit
    }

    [Fact] // C6
    public void Coverage_context_files_considered_is_the_repository_scope_not_the_max_selected()
    {
        var plan = Plan(
            [Provider("Sonar", EvidenceAcquisitionScope.Repository),
             Provider("Claude", EvidenceAcquisitionScope.Discipline)],
            [FindingCategory.Security]);

        var records = new List<ProviderExecutionRecord>
        {
            new() { ProviderName = "Sonar", StepId = "Sonar#repository", Scope = EvidenceAcquisitionScope.Repository, Success = true, ContextFileCount = 2, ContextFilesConsidered = 4 },
            new() { ProviderName = "Claude", StepId = "Claude#Security", Scope = EvidenceAcquisitionScope.Discipline, RequestedDiscipline = FindingCategory.Security, Success = true, ContextFileCount = 3, ContextFilesConsidered = 4 }
        };
        var report = ProviderExecutionReport.FromRecords(records, plan);
        var coverage = AcquisitionCoverage.From(plan, report, [FindingCategory.Security]);

        // "Considered" is the repository scope the selector ranks, not the largest
        // single-step SELECTION (which would be 3 and undercount the scope).
        Assert.Equal(4, coverage.ContextFilesConsidered);
        Assert.Equal(5, coverage.ContextFilesSelected); // sum of per-step selections
        Assert.True(coverage.ContextFilesConsidered > records.Max(r => r.ContextFileCount));
    }

    [Fact] // C6
    public async Task Executor_stamps_context_files_considered_from_the_repository_scope()
    {
        var claude = Provider("Claude", EvidenceAcquisitionScope.Discipline);
        var plan = Plan([claude], [FindingCategory.Security]);

        var result = await Executor(claude).ExecuteAsync(plan, Snapshot);

        var record = Assert.Single(result.Executions);
        Assert.Equal(Snapshot.Files.Count, record.ContextFilesConsidered);
        Assert.True(record.ContextFileCount > 0); // a subset of the considered scope
    }

    // ── Executor ─────────────────────────────────────────────────────────────

    [Fact] // 14
    public async Task Executor_runs_each_step_and_stamps_provenance()
    {
        var provider = Provider("Claude", EvidenceAcquisitionScope.Discipline);
        var executor = Executor(provider);
        var plan = Plan([provider], AllDisciplines);

        var result = await executor.ExecuteAsync(plan, Snapshot);

        Assert.Equal(AllDisciplines.Length, result.Evidence.Count);
        Assert.All(result.Evidence, e => Assert.Equal(EvidenceAcquisitionScope.Discipline, e.AcquisitionScope));
        Assert.All(result.Evidence, e => Assert.NotNull(e.RequestedDiscipline));
        Assert.All(result.Evidence, e => Assert.False(string.IsNullOrEmpty(e.AcquisitionStepId)));
        Assert.All(result.Evidence, e => Assert.False(string.IsNullOrEmpty(e.CorrelationId)));
        Assert.Equal(AllDisciplines.Length, result.Executions.Count);
    }

    [Fact] // 15
    public async Task Executor_owns_the_records_and_echoes_the_plan()
    {
        var provider = Provider("Claude", EvidenceAcquisitionScope.Discipline);
        var plan = Plan([provider], [FindingCategory.Security]);

        var result = await Executor(provider).ExecuteAsync(plan, Snapshot);

        Assert.Same(plan, result.Plan);
        Assert.All(result.Executions, r => Assert.Equal("run1", r.RunId));
        Assert.All(result.Executions, r => Assert.False(string.IsNullOrEmpty(r.StepId)));
    }

    [Fact] // 16
    public async Task A_failing_step_does_not_stop_remaining_steps()
    {
        var claude = Provider("Claude", EvidenceAcquisitionScope.Discipline);
        var codex = new ThrowingProvider("Codex");
        var executor = Executor(claude, codex);
        var plan = Plan([claude, codex], AllDisciplines);

        var result = await executor.ExecuteAsync(plan, Snapshot);

        Assert.Contains(result.Evidence, e => e.ProviderName == "Codex" && !e.Success);
        Assert.Contains(result.Evidence, e => e.ProviderName == "Claude" && e.Success);
        Assert.Equal(AllDisciplines.Length, result.Executions.Count(r => r.ProviderName == "Codex" && !r.Success));
    }

    [Fact] // 17
    public async Task An_unavailable_provider_produces_a_failed_evidence_but_the_run_continues()
    {
        var available = Provider("Claude", EvidenceAcquisitionScope.Discipline);
        var unavailable = new UnavailableProvider("Down");
        var plan = Plan([available, unavailable], [FindingCategory.Security]);

        var result = await Executor(available, unavailable).ExecuteAsync(plan, Snapshot);

        Assert.Contains(result.Evidence, e => e.ProviderName == "Down" && !e.Success);
        Assert.Contains(result.Evidence, e => e.ProviderName == "Claude" && e.Success);
    }

    [Fact] // 18
    public async Task Telemetry_is_isolated_between_concurrent_runs()
    {
        var provider = Provider("Claude", EvidenceAcquisitionScope.Discipline);
        var executor = Executor(provider);
        var plan = Plan([provider], AllDisciplines);

        var runs = await Task.WhenAll(
            executor.ExecuteAsync(plan, Snapshot),
            executor.ExecuteAsync(plan, Snapshot));

        Assert.All(runs, r => Assert.Equal(AllDisciplines.Length, r.Executions.Count));
    }

    // ── Interpreter: discipline-mismatch rule ────────────────────────────────

    [Fact] // 19
    public async Task Discipline_mismatch_lowers_confidence_and_tags_but_does_not_rewrite()
    {
        // Security-scoped request, but the observation claims Architecture.
        var evidence = LlmEvidence(
            requested: FindingCategory.Security,
            observationDiscipline: "Architecture",
            confidence: "High");

        var observations = await Interpret(evidence);
        var observation = Assert.Single(observations);

        Assert.Equal(FindingCategory.Architecture, observation.Discipline); // not rewritten
        Assert.Contains("discipline-mismatch", observation.Tags);
        Assert.True(observation.Confidence < FindingConfidence.High); // lowered
    }

    [Fact] // 20
    public async Task Matching_discipline_is_not_flagged_as_a_mismatch()
    {
        var evidence = LlmEvidence(
            requested: FindingCategory.Security,
            observationDiscipline: "Security",
            confidence: "High");

        var observation = Assert.Single(await Interpret(evidence));
        Assert.DoesNotContain("discipline-mismatch", observation.Tags);
    }

    [Fact] // 21
    public async Task Observation_carries_the_acquisition_step_id()
    {
        var evidence = LlmEvidence(FindingCategory.Security, "Security", "Medium") with { AcquisitionStepId = "Claude#Security" };
        var observation = Assert.Single(await Interpret(evidence));
        Assert.Equal("Claude#Security", observation.AcquisitionStepId);
    }

    // ── Coverage rollup ──────────────────────────────────────────────────────

    [Fact] // 22
    public async Task Coverage_reports_requested_covered_sources_and_unsupported()
    {
        var claude = Provider("Claude", EvidenceAcquisitionScope.Discipline, supported: new HashSet<FindingCategory> { FindingCategory.Architecture, FindingCategory.Security });
        var sonar = Provider("Sonar", EvidenceAcquisitionScope.Repository);
        IReadOnlyList<FindingCategory> requested = [FindingCategory.Architecture, FindingCategory.Security, FindingCategory.Testing];

        var plan = new EvidenceAcquisitionPlanner().CreatePlan(Config(), Snapshot, [claude, sonar], requested);
        var result = await Executor(claude, sonar).ExecuteAsync(plan, Snapshot);
        var report = ProviderExecutionReport.FromRecords(result.Executions, plan);

        var coverage = AcquisitionCoverage.From(plan, report, requested);

        Assert.Contains("Sonar", coverage.RepositoryWideSourcesExecuted);
        Assert.Contains("Claude", coverage.DisciplineScopedSourcesExecuted);
        Assert.Equal(3, coverage.DisciplinesRequested.Count);
        Assert.Contains("Architecture", coverage.DisciplinesCovered);
        Assert.Contains("Security", coverage.DisciplinesCovered);
        Assert.Contains(coverage.UnsupportedCombinations, u => u.Contains("Testing"));
    }

    // ── Provider metadata defaults (documented contract) ─────────────────────

    [Fact] // 23
    public void Provider_default_scopes_match_the_documented_contract()
    {
        Assert.Equal(EvidenceAcquisitionScope.Discipline, new MockEvidenceProvider().Metadata.DefaultAcquisitionScope);
        Assert.Equal(EvidenceAcquisitionScope.Discipline, new OllamaEvidenceProvider().Metadata.DefaultAcquisitionScope);
        Assert.Equal(EvidenceAcquisitionScope.Repository, new RoslynEvidenceProvider().Metadata.DefaultAcquisitionScope);
        Assert.Equal(EvidenceAcquisitionScope.Repository, new SonarEvidenceProvider().Metadata.DefaultAcquisitionScope);
        Assert.Equal(EvidenceAcquisitionScope.Repository, new SarifEvidenceProvider(new SarifOptions()).Metadata.DefaultAcquisitionScope);
        Assert.Equal(EvidenceAcquisitionScope.Repository, new GitEvidenceProvider().Metadata.DefaultAcquisitionScope);
        Assert.Equal(EvidenceAcquisitionScope.Repository, new CoverageEvidenceProvider().Metadata.DefaultAcquisitionScope);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AnalysisRunConfiguration Config() => new() { RunId = "run1" };

    private static EvidenceAcquisitionPlan Plan(
        IReadOnlyCollection<IEvidenceProvider> providers, IReadOnlyCollection<FindingCategory> disciplines)
        => new EvidenceAcquisitionPlanner().CreatePlan(Config(), Snapshot, providers, disciplines);

    private static EvidenceAcquisitionExecutor Executor(params IEvidenceProvider[] providers)
        => new(new EvidenceProviderFactory(providers), new RuleBasedAnalysisContextSelector(),
            new EvidenceOptions { ProviderTimeout = TimeSpan.FromSeconds(5) });

    private static FakeProvider Provider(
        string name, EvidenceAcquisitionScope scope, IReadOnlySet<FindingCategory>? supported = null)
        => new(name, scope, supported ?? new HashSet<FindingCategory>());

    private static async Task<IReadOnlyList<EngineeringObservation>> Interpret(Evidence evidence)
        => await new StructuredLlmEvidenceInterpreter().InterpretAsync(
            evidence, new AnalyzerContext { Snapshot = Snapshot, RunId = "run1" });

    private static Evidence LlmEvidence(FindingCategory requested, string observationDiscipline, string confidence)
        => new()
        {
            ProviderName = "Claude",
            ProviderType = EvidenceProviderType.LLM,
            Success = true,
            AcquisitionScope = EvidenceAcquisitionScope.Discipline,
            RequestedDiscipline = requested,
            RawResponse = $$"""
                {
                  "observations": [
                    {
                      "type": "GeneralObservation",
                      "discipline": "{{observationDiscipline}}",
                      "title": "Sample",
                      "description": "A sample observation.",
                      "severity": "Medium",
                      "confidence": "{{confidence}}",
                      "fileReferences": [ { "path": "src/Auth.cs" } ]
                    }
                  ]
                }
                """
        };

    private sealed class FakeProvider(string name, EvidenceAcquisitionScope scope, IReadOnlySet<FindingCategory> supported)
        : IEvidenceProvider
    {
        public bool IsAvailable => true;
        public EvidenceProviderMetadata Metadata => new()
        {
            Name = name,
            ProviderType = EvidenceProviderType.LLM,
            DefaultAcquisitionScope = scope,
            SupportedDisciplines = supported,
            RequiresAnalyzerInstructions = scope == EvidenceAcquisitionScope.Discipline,
            SupportsRepositoryWideAnalysis = scope == EvidenceAcquisitionScope.Repository
        };
        public Task<IReadOnlyList<Evidence>> CollectAsync(EvidenceRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Evidence>>(
                [new Evidence { ProviderName = name, ProviderType = EvidenceProviderType.LLM, RawResponse = "{\"observations\":[]}" }]);
    }

    private sealed class ThrowingProvider(string name) : IEvidenceProvider
    {
        public bool IsAvailable => true;
        public EvidenceProviderMetadata Metadata => new()
        {
            Name = name,
            ProviderType = EvidenceProviderType.LLM,
            DefaultAcquisitionScope = EvidenceAcquisitionScope.Discipline
        };
        public Task<IReadOnlyList<Evidence>> CollectAsync(EvidenceRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("kaboom");
    }

    private sealed class UnavailableProvider(string name) : IEvidenceProvider
    {
        public bool IsAvailable => false;
        public EvidenceProviderMetadata Metadata => new()
        {
            Name = name,
            ProviderType = EvidenceProviderType.LLM,
            DefaultAcquisitionScope = EvidenceAcquisitionScope.Discipline
        };
        public Task<IReadOnlyList<Evidence>> CollectAsync(EvidenceRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("should not be called");
    }
}
