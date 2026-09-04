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
/// Milestone 013.4 — the Codex process adapter (the SECOND real agentic runtime,
/// following OpenCode in M13.2/M13.3). Codex is a real
/// <see cref="EvidenceProviderType.Agentic"/> source: the council launches the
/// executable against the repository root, hands it one discipline-specific
/// read-only instruction, and converts its structured <c>observations</c> envelope
/// into existing <c>Evidence</c>. Downstream (interpreter → observations → analyzers
/// → reconciliation → package) never learns that Codex exists; M12 comparison still
/// excludes agentic executions; the package stays schemaVersion 1.1.
///
/// Naming (M13.4): "Codex" is the REAL agentic Codex CLI provider, NOT a legacy
/// alias for the OpenAI API provider (that alias was removed so the two are
/// unambiguous).
///
/// All normal tests use a fake <see cref="ICodexProcessRunner"/> — no Codex
/// installation, no network, no credentials, no paid calls.
/// </summary>
public sealed class CodexEvidenceTests
{
    private static RepositorySnapshot Snapshot => new()
    {
        RootPath = "/repo",
        SolutionName = "Repo",
        ProjectFiles = ["src/App.csproj"],
        Files =
        [
            new ScannedFile { RelativePath = "src/Auth.cs", Extension = ".cs", SizeBytes = 1, LineCount = 40, Content = "public class Auth { void Authorize(){} } // Authentication ApiKey" },
            new ScannedFile { RelativePath = "tests/AuthTests.cs", Extension = ".cs", SizeBytes = 1, LineCount = 20, Content = "[Fact] void T(){ Assert.True(true); } // xunit" },
            new ScannedFile { RelativePath = "README.md", Extension = ".md", SizeBytes = 1, LineCount = 10, Content = "# Repo" },
        ]
    };

    // ── 1. Provider type & metadata ───────────────────────────────────────────

    [Fact]
    public void Codex_metadata_is_agentic_and_discipline_scoped()
    {
        var metadata = new CodexEvidenceProvider(EnabledOptions(), new FakeCodexRunner()).Metadata;

        Assert.Equal("Codex", metadata.Name);
        Assert.Equal(EvidenceProviderType.Agentic, metadata.ProviderType);
        Assert.Equal(EvidenceAcquisitionScope.Discipline, metadata.DefaultAcquisitionScope);
        Assert.True(metadata.RequiresAnalyzerInstructions);
        Assert.False(metadata.SupportsRepositoryWideAnalysis);
        Assert.True(metadata.Supports(FindingCategory.Security));
        Assert.False(string.IsNullOrWhiteSpace(metadata.Version));
    }

    [Fact]
    public void Codex_is_disabled_by_default()
    {
        var options = new CodexOptions();

        Assert.False(options.Enabled);
        Assert.False(options.IsUsable);
        Assert.NotNull(options.UnavailableReason);
        Assert.False(new CodexEvidenceProvider(options, new FakeCodexRunner()).IsAvailable);
    }

    // ── 2. Process runner invocation ──────────────────────────────────────────

    [Fact]
    public async Task Codex_uses_the_configured_executable()
    {
        var fake = new FakeCodexRunner();
        var provider = new CodexEvidenceProvider(
            new CodexOptions { Enabled = true, Executable = "custom-codex" }, fake);

        await provider.CollectAsync(Request(FindingCategory.Security));

        Assert.Equal("custom-codex", fake.LastRequest!.Executable);
    }

    [Fact]
    public async Task Codex_uses_the_repository_root_as_the_working_directory()
    {
        var fake = new FakeCodexRunner();
        var provider = new CodexEvidenceProvider(EnabledOptions(), fake);

        await provider.CollectAsync(Request(FindingCategory.Security, root: "/repo"));

        Assert.Equal("/repo", fake.LastRequest!.WorkingDirectory);
    }

    // ── 3. The analysis instruction ───────────────────────────────────────────

    [Fact]
    public async Task Codex_prompt_contains_discipline_and_analyzer_instructions()
    {
        var fake = new FakeCodexRunner();
        var provider = new CodexEvidenceProvider(EnabledOptions(), fake);

        await provider.CollectAsync(Request(FindingCategory.Security));
        var instruction = fake.Instruction;

        Assert.Contains("Security", instruction);
        Assert.Contains("evidence acquisition agent", instruction);
        Assert.Contains("current working directory", instruction);
        Assert.Contains("authentication", instruction, StringComparison.OrdinalIgnoreCase); // the Security objective
    }

    [Fact]
    public async Task Codex_prompt_declares_the_read_only_boundary()
    {
        var fake = new FakeCodexRunner();
        var provider = new CodexEvidenceProvider(EnabledOptions(), fake);

        await provider.CollectAsync(Request(FindingCategory.Security));
        var instruction = fake.Instruction;

        Assert.Contains("READ ONLY", instruction);
        Assert.Contains("must NOT modify source files", instruction);
        Assert.Contains("must NOT", instruction);
    }

    [Fact]
    public async Task Codex_prompt_requests_the_existing_structured_schema()
    {
        var fake = new FakeCodexRunner();
        var provider = new CodexEvidenceProvider(EnabledOptions(), fake);

        await provider.CollectAsync(Request(FindingCategory.Security));
        var instruction = fake.Instruction;

        Assert.Contains("schemaVersion", instruction);
        Assert.Contains("observations", instruction);
        Assert.Contains("\"1.0\"", instruction);
        Assert.Contains("fileReferences", instruction);
    }

    // ── 4. Model selection ────────────────────────────────────────────────────

    [Fact]
    public async Task Codex_configured_model_reaches_the_process_invocation_safely()
    {
        var fake = new FakeCodexRunner();
        var provider = new CodexEvidenceProvider(
            new CodexOptions { Enabled = true, Model = "gpt-5.4-mini" }, fake);

        await provider.CollectAsync(Request(FindingCategory.Security));

        var arguments = fake.LastRequest!.Arguments;
        Assert.Equal("exec", arguments[0]);
        Assert.Equal("-s", arguments[1]);
        Assert.Equal("read-only", arguments[2]);
        Assert.Contains("--ephemeral", arguments);
        Assert.Contains("--skip-git-repo-check", arguments);
        Assert.Contains("-m", arguments);
        Assert.Contains("gpt-5.4-mini", arguments);
        Assert.Equal(fake.Instruction, arguments[^1]);   // the instruction is the last argv entry
    }

    [Fact]
    public async Task Codex_configured_model_appears_in_evidence_telemetry()
    {
        var fake = new FakeCodexRunner();
        var provider = new CodexEvidenceProvider(
            new CodexOptions { Enabled = true, Model = "gpt-5.4-mini" }, fake);

        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var evidence = Assert.Single(result.Evidence);
        Assert.True(evidence.Success);
        Assert.Equal("gpt-5.4-mini", evidence.Metadata["model"]);
        Assert.Equal(EvidenceProviderType.Agentic, evidence.ProviderType);
        Assert.Equal("codex", evidence.ProviderId);
    }

    [Fact]
    public async Task Codex_without_a_configured_model_keeps_codex_default_behavior()
    {
        var fake = new FakeCodexRunner();
        var provider = new CodexEvidenceProvider(
            new CodexOptions { Enabled = true, Executable = "codex" }, fake);   // no Model

        await provider.CollectAsync(Request(FindingCategory.Security));

        Assert.DoesNotContain("-m", fake.LastRequest!.Arguments);
        Assert.Equal(fake.Instruction, fake.LastRequest.Arguments[^1]);
    }

    [Fact]
    public void Codex_model_is_optional_and_never_hardcoded()
    {
        var options = new CodexOptions { Enabled = true };

        Assert.Null(options.Model);                    // no model ⇒ Codex's own default
        Assert.True(options.IsUsable);                 // Model is NOT mandatory
        Assert.Equal("Codex", options.ProviderName);   // the provider is Codex, never a specific model
    }

    // ── 5. Result → Evidence, with existing failure semantics ─────────────────

    [Fact]
    public async Task Codex_valid_process_output_becomes_evidence()
    {
        var fake = new FakeCodexRunner();
        var provider = new CodexEvidenceProvider(EnabledOptions(), fake);
        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var evidence = Assert.Single(result.Evidence);
        Assert.True(evidence.Success);
        Assert.Equal(EvidenceProviderType.Agentic, evidence.ProviderType);
        Assert.Equal("Codex", evidence.ProviderName);
        Assert.Equal("codex", evidence.ProviderId);
        Assert.Contains("\"observations\"", evidence.RawResponse);
        Assert.Equal(string.Empty, evidence.ContextFingerprint);
        Assert.Equal("agentic", evidence.ContextSelectionStrategy);

        var record = Assert.Single(result.Executions);
        Assert.Equal(EvidenceProviderType.Agentic, record.ProviderType);
        Assert.Equal(string.Empty, record.ContextFingerprint);
        Assert.Equal("agentic", record.ContextSelectionStrategy);
    }

    [Fact]
    public async Task Codex_malformed_output_is_a_schema_failure()
    {
        var fake = new FakeCodexRunner { Handler = (_, _) => Task.FromResult(Result(0, "this is not a structured result")) };
        var provider = new CodexEvidenceProvider(EnabledOptions(), fake);

        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var record = Assert.Single(result.Executions);
        Assert.False(record.Success);
        Assert.Equal("SchemaValidation", record.ErrorCategory);
        Assert.False(Assert.Single(result.Evidence).Success);
    }

    [Fact]
    public async Task Codex_nonzero_exit_code_is_a_process_failure()
    {
        var fake = new FakeCodexRunner { Handler = (_, _) => Task.FromResult(Result(1, "", "codex: boom")) };
        var provider = new CodexEvidenceProvider(EnabledOptions(), fake);

        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var record = Assert.Single(result.Executions);
        Assert.False(record.Success);
        Assert.Equal("ProcessError", record.ErrorCategory);
        Assert.Contains("boom", record.ErrorMessage);
        Assert.Equal(string.Empty, record.ContextFingerprint);
    }

    [Fact]
    public async Task Codex_timeout_uses_existing_timeout_failure_semantics()
    {
        var fake = new FakeCodexRunner
        {
            Handler = async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Result(0, "unreachable"); }
        };
        var provider = new CodexEvidenceProvider(EnabledOptions(), fake);

        var result = await Executor(TimeSpan.FromMilliseconds(200), provider)
            .ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var record = Assert.Single(result.Executions);
        Assert.False(record.Success);
        Assert.Equal("Timeout", record.ErrorCategory);
        Assert.Equal(string.Empty, record.ContextFingerprint);
    }

    [Fact]
    public async Task Codex_run_cancellation_propagates_and_is_not_an_ordinary_failure()
    {
        using var cts = new CancellationTokenSource();
        var fake = new FakeCodexRunner
        {
            Handler = (_, ct) =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                throw new OperationCanceledException(ct);
            }
        };
        var provider = new CodexEvidenceProvider(EnabledOptions(), fake);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Executor(TimeSpan.FromSeconds(30), provider)
                .ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot, cts.Token));

        Assert.NotNull(fake.LastRequest); // the runner was invoked before cancellation
    }

    [Fact]
    public async Task Codex_never_fabricates_a_context_fingerprint_when_unavailable()
    {
        var fake = new FakeCodexRunner();
        var provider = new CodexEvidenceProvider(new CodexOptions(), fake); // disabled

        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        Assert.False(provider.IsAvailable);
        Assert.Null(fake.LastRequest); // never invoked
        var record = Assert.Single(result.Executions);
        Assert.False(record.Success);
        Assert.Equal(EvidenceProviderType.Agentic, record.ProviderType);
        Assert.Equal(string.Empty, record.ContextFingerprint);
        Assert.Equal("agentic", record.ContextSelectionStrategy);
    }

    [Fact]
    public async Task Codex_no_api_key_or_credential_is_written_to_telemetry_or_artifacts()
    {
        // Credentials belong to Codex's own auth (`codex login` / ChatGPT account); the
        // provider never sees or carries the secret, so no key value can leak.
        var fake = new FakeCodexRunner();
        var provider = new CodexEvidenceProvider(
            new CodexOptions { Enabled = true, Model = "gpt-5.4-mini" }, fake);

        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var evidence = Assert.Single(result.Evidence);
        var serialized = System.Text.Json.JsonSerializer.Serialize(evidence, CouncilJson.Options);
        Assert.DoesNotContain("sk-", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_key", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("login", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("gpt-5.4-mini", evidence.Metadata["model"]);
    }

    // ── 6. End to end: agentic pipeline → consumer-compatible package ─────────

    [Fact]
    public async Task Codex_end_to_end_produces_observations_findings_and_a_consumer_compatible_package()
    {
        var repo = Path.Combine(Path.GetTempPath(), "ec-codex-repo-" + Guid.NewGuid().ToString("N"));
        var outputs = Path.Combine(Path.GetTempPath(), "ec-codex-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(repo, "src"));
            File.WriteAllText(Path.Combine(repo, "Sample.sln"), "solution\n");
            File.WriteAllText(Path.Combine(repo, "src", "Auth.cs"), "public class Auth { void Authorize() { } }\n");

            var fake = new FakeCodexRunner
            {
                Handler = (_, _) => Task.FromResult(Result(0, StructuredJson(FindingCategory.Security, "src/Auth.cs")))
            };
            var provider = new CodexEvidenceProvider(EnabledOptions(), fake);
            var pipeline = BuildPipeline([provider], [FindingCategory.Security], outputs);

            var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = repo, ProviderName = "Codex" });

            Assert.NotEmpty(result.Run.Observations);
            Assert.All(result.Run.Observations, o => Assert.Equal(EvidenceProviderType.Agentic, o.SourceProviderType));
            Assert.Contains(result.Run.Findings, f => f.Category == FindingCategory.Security);
            Assert.All(result.Run.ProviderExecution!.Records, r =>
            {
                Assert.Equal(EvidenceProviderType.Agentic, r.ProviderType);
                Assert.Equal(string.Empty, r.ContextFingerprint);
            });

            var packageJson = File.ReadAllText(Path.Combine(result.OutputDirectory, "engineering-review-package.json"));
            var dto = System.Text.Json.JsonSerializer.Deserialize<PackageContract>(packageJson, CouncilJson.Options)!;
            Assert.Equal("1.1", dto.SchemaVersion);
            // The external contract carries neither Codex- nor model-specific fields.
            Assert.DoesNotContain("providerComparison", packageJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"model\"", packageJson);
            Assert.Contains("Codex", packageJson);
        }
        finally
        {
            foreach (var dir in new[] { repo, outputs })
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Codex_consumer_dto_gate_stays_green_for_the_package_contract()
    {
        // The consumer DTO (PackageContract) shape is unchanged by M13.4 — Codex is
        // internal execution telemetry, never part of the external contract.
        var repoRoot = FindRepoRoot();
        Assert.NotNull(repoRoot); // must run from the repo tree so the sample is locatable
        var sample = File.ReadAllText(Path.Combine(repoRoot!, "artifacts", "samples", "engineering-review-package.sample.json"));
        var dto = System.Text.Json.JsonSerializer.Deserialize<PackageContract>(sample, CouncilJson.Options);

        Assert.NotNull(dto);
        Assert.Equal("1.1", dto.SchemaVersion);
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EngineeringCouncil.slnx")))
            dir = dir.Parent;
        return dir?.FullName;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static CodexOptions EnabledOptions() => new() { Enabled = true, Executable = "codex" };

    private static EvidenceRequest Request(FindingCategory discipline, string root = "/repo") => new()
    {
        RunId = "run1",
        RepositorySnapshot = Snapshot with { RootPath = root },
        Scope = EvidenceAcquisitionScope.Discipline,
        Discipline = discipline,
        Instructions = DisciplinePrompts.BuildInstructions(EvidenceAcquisitionScope.Discipline, discipline),
        ContextSelection = AgenticSelection(Snapshot),
        ProviderNames = ["Codex"],
        CorrelationId = "Codex:Security"
    };

    private static AnalysisContextSelection AgenticSelection(RepositorySnapshot repository) => new()
    {
        Strategy = "agentic",
        Files = [],
        TotalRepositoryFiles = repository.Files.Count,
        SelectedFileCount = 0,
        EstimatedContentSize = 0
    };

    private static CodexProcessResult Result(int exitCode, string stdout, string stderr = "")
        => new() { ExitCode = exitCode, StandardOutput = stdout, StandardError = stderr };

    private static string StructuredJson(FindingCategory discipline, string filePath)
        => $$"""
            {
              "schemaVersion": "1.0",
              "discipline": "{{discipline}}",
              "observations": [
                {
                  "type": "HardcodedSecret",
                  "discipline": "{{discipline}}",
                  "title": "Possible sensitive value in source/config",
                  "description": "The Codex agent found a likely credential while exploring.",
                  "severity": "Medium",
                  "confidence": "Medium",
                  "ruleId": "CX-001",
                  "fileReferences": [ { "path": "{{filePath}}" } ],
                  "symbolReferences": [],
                  "lineReferences": [],
                  "evidenceExcerpt": "Agent exploration.",
                  "recommendationHint": "Review and rotate.",
                  "tags": ["agentic", "codex"]
                }
              ]
            }
            """;

    private static AnalysisRunConfiguration Config() => new() { RunId = "run1" };

    private static EvidenceAcquisitionPlan Plan(
        IReadOnlyCollection<IEvidenceProvider> providers, IReadOnlyCollection<FindingCategory> disciplines)
        => new EvidenceAcquisitionPlanner().CreatePlan(Config(), Snapshot, providers, disciplines);

    private static EvidenceAcquisitionExecutor Executor(params IEvidenceProvider[] providers)
        => Executor(TimeSpan.FromSeconds(30), providers);

    private static EvidenceAcquisitionExecutor Executor(TimeSpan timeout, params IEvidenceProvider[] providers)
        => new(new EvidenceProviderFactory(providers), new RuleBasedAnalysisContextSelector(),
            new EvidenceOptions { ProviderTimeout = timeout });

    private static AnalysisPipeline BuildPipeline(
        IReadOnlyList<IEvidenceProvider> providers, IReadOnlyList<FindingCategory> disciplines, string outputs)
    {
        var options = new EvidenceOptions
        {
            Providers = providers.Select(p => p.Metadata.Name).ToList(),
            Disciplines = disciplines,
            ProviderFailureMode = ProviderFailureMode.Continue
        };
        var factory = new EvidenceProviderFactory(providers);

        return new AnalysisPipeline(
            new FileSystemRepositoryScanner(), factory,
            new EvidenceAcquisitionPlanner(),
            new EvidenceAcquisitionExecutor(factory, new RuleBasedAnalysisContextSelector(), options),
            new EvidenceInterpretationPipeline(new EvidenceInterpreterResolver(
                [new StructuredLlmEvidenceInterpreter()])),
            new AnalysisOrchestrator([new SecurityAnalyzer(), new TestingAnalyzer()]),
            new RuleBasedFindingReconciler(),
            new RuleBasedCouncilSummaryGenerator(),
            new EngineeringReviewPackageBuilder(),
            options,
            new FileSystemAnalysisRunRepository(
                new FileSystemRunRepositoryOptions { OutputsRoot = outputs },
                new EngineeringReviewMarkdownExporter(), new JsonReportGenerator()));
    }

    /// <summary>
    /// Offline fake runner: records the invocation and returns a scripted outcome.
    /// Normal tests never need a real Codex installation.
    /// </summary>
    private sealed class FakeCodexRunner : ICodexProcessRunner
    {
        public CodexProcessRequest? LastRequest { get; private set; }
        public int Calls { get; private set; }

        public Func<CodexProcessRequest, CancellationToken, Task<CodexProcessResult>> Handler { get; set; }
            = (_, _) => Task.FromResult(Result(0, StructuredJson(FindingCategory.Security, "src/Auth.cs")));

        /// <summary>The analysis instruction — always the final argument of <c>["exec", ...]</c>.</summary>
        public string Instruction => LastRequest!.Arguments[^1];

        public Task<CodexProcessResult> RunAsync(CodexProcessRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            return Handler(request, cancellationToken);
        }
    }
}
