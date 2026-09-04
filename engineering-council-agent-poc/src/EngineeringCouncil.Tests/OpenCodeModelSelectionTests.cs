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
/// Milestone 013.3 — explicit OpenCode model selection (OpenCode + DeepSeek V4 Flash).
/// Model selection is CONFIGURATION, not domain logic: <c>Evidence:OpenCode:Model</c>
/// is passed through the existing safe process invocation as <c>--model
/// &lt;provider/model&gt;</c> via <c>ArgumentList</c> (never a shell string), is NOT
/// hardcoded to DeepSeek anywhere, and is preserved as telemetry metadata only when
/// explicitly configured. Credentials never reach the Council (OpenCode's own auth,
/// e.g. <c>DEEPSEEK_API_KEY</c>). Everything downstream is unchanged: structured
/// Evidence → observations → findings → a consumer-compatible
/// <c>engineering-review-package.json</c> (schemaVersion 1.1).
///
/// All normal tests use a fake <see cref="IOpenCodeProcessRunner"/> — no OpenCode
/// installation, no network, no DeepSeek key, no paid calls.
/// </summary>
public sealed class OpenCodeModelSelectionTests
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

    // ── 1. Configuration ───────────────────────────────────────────────────────

    [Fact]
    public void OpenCode_model_is_configurable_and_optional()
    {
        var configured = new OpenCodeOptions { Enabled = true, Model = "deepseek/deepseek-v4-flash" };
        var unset = new OpenCodeOptions { Enabled = true };

        Assert.Equal("deepseek/deepseek-v4-flash", configured.Model);
        Assert.Null(unset.Model);                    // no model ⇒ OpenCode's own default (M13.2 behavior)
        Assert.True(configured.IsUsable);
        Assert.True(unset.IsUsable);                 // Model is NOT mandatory
        Assert.DoesNotContain("deepseek", unset.ProviderName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpenCode_model_is_not_hardcoded_to_deepseek()
    {
        var anyModel = new OpenCodeOptions { Enabled = true, Model = "anthropic/claude-sonnet-4-5" };
        var provider = new OpenCodeEvidenceProvider(anyModel, new FakeOpenCodeRunner());

        Assert.Equal("OpenCode", provider.Metadata.Name);     // the provider is OpenCode, never DeepSeek
        Assert.DoesNotContain("deepseek", provider.Metadata.Name, StringComparison.OrdinalIgnoreCase);
    }

    // ── 2. Process invocation ──────────────────────────────────────────────────

    [Fact]
    public async Task OpenCode_configured_model_reaches_the_process_invocation()
    {
        var fake = new FakeOpenCodeRunner();
        var provider = new OpenCodeEvidenceProvider(
            new OpenCodeOptions { Enabled = true, Model = "deepseek/deepseek-v4-flash" }, fake);

        await provider.CollectAsync(Request(FindingCategory.Security));

        var arguments = fake.LastRequest!.Arguments;
        Assert.Equal("run", arguments[0]);
        Assert.Equal("--model", arguments[1]);
        Assert.Equal("deepseek/deepseek-v4-flash", arguments[2]);
        Assert.Equal(fake.Instruction, arguments[^1]);
    }

    [Fact]
    public async Task OpenCode_uses_safe_argument_list_handling_for_the_model()
    {
        // The model is a separate ArgumentList entry (argv), never embedded in a
        // shell string: the invocation is exactly [run, --model, <id>, <instruction>]
        // and the instruction itself stays one untouched argv entry.
        var fake = new FakeOpenCodeRunner();
        var provider = new OpenCodeEvidenceProvider(
            new OpenCodeOptions { Enabled = true, Model = "deepseek/deepseek-v4-flash" }, fake);

        await provider.CollectAsync(Request(FindingCategory.Security));

        Assert.Equal(4, fake.LastRequest!.Arguments.Count);
        Assert.Equal("run", fake.LastRequest.Arguments[0]);
        Assert.Equal("--model", fake.LastRequest.Arguments[1]);
        Assert.Equal("deepseek/deepseek-v4-flash", fake.LastRequest.Arguments[2]);
        Assert.Equal(fake.Instruction, fake.LastRequest.Arguments[3]);
    }

    [Fact]
    public async Task OpenCode_without_a_configured_model_keeps_existing_behavior()
    {
        var fake = new FakeOpenCodeRunner();
        var provider = new OpenCodeEvidenceProvider(
            new OpenCodeOptions { Enabled = true, Executable = "opencode" }, fake);   // no Model

        await provider.CollectAsync(Request(FindingCategory.Security));

        Assert.Equal(["run", fake.Instruction], fake.LastRequest!.Arguments);
    }

    // ── 2b. Server isolation (M14.1) ─────────────────────────────────────────

    [Fact]
    public async Task OpenCode_configured_port_reaches_the_process_invocation()
    {
        var fake = new FakeOpenCodeRunner();
        var provider = new OpenCodeEvidenceProvider(
            new OpenCodeOptions { Enabled = true, Port = 43111 }, fake);

        await provider.CollectAsync(Request(FindingCategory.Security));

        var arguments = fake.LastRequest!.Arguments;
        Assert.Equal("run", arguments[0]);
        Assert.Equal("--port", arguments[1]);
        Assert.Equal("43111", arguments[2]);
        Assert.Equal(fake.Instruction, arguments[^1]);
    }

    [Fact]
    public async Task OpenCode_zero_port_emits_a_concrete_random_port()
    {
        var fake = new FakeOpenCodeRunner();
        var provider = new OpenCodeEvidenceProvider(
            new OpenCodeOptions { Enabled = true, Port = 0 }, fake);

        await provider.CollectAsync(Request(FindingCategory.Security));

        var arguments = fake.LastRequest!.Arguments;
        Assert.Equal("run", arguments[0]);
        Assert.Equal("--port", arguments[1]);
        // A bare `--port` would swallow the instruction as its value ("You must provide
        // a message or a command"); a concrete free port number keeps the instruction a
        // standalone positional argument.
        Assert.True(int.TryParse(arguments[2], out var port) && port > 0);
        Assert.Equal(fake.Instruction, arguments[^1]);
    }

    [Fact]
    public async Task OpenCode_port_argument_list_stays_safe_and_ordered()
    {
        // Model (M13.3) and Port (M14.1) both travel as separate ArgumentList entries:
        // [run, --model, <id>, --port, <n>, <instruction>] — never a shell string, and
        // the instruction stays one untouched argv entry.
        var fake = new FakeOpenCodeRunner();
        var provider = new OpenCodeEvidenceProvider(
            new OpenCodeOptions { Enabled = true, Model = "deepseek/deepseek-v4-flash", Port = 43112 }, fake);

        await provider.CollectAsync(Request(FindingCategory.Security));

        Assert.Equal(6, fake.LastRequest!.Arguments.Count);
        Assert.Equal(["run", "--model", "deepseek/deepseek-v4-flash", "--port", "43112", fake.Instruction],
            fake.LastRequest.Arguments);
    }

    [Fact]
    public async Task OpenCode_without_a_configured_port_keeps_existing_behavior()
    {
        var fake = new FakeOpenCodeRunner();
        var provider = new OpenCodeEvidenceProvider(
            new OpenCodeOptions { Enabled = true, Model = "deepseek/deepseek-v4-flash" }, fake);   // no Port

        await provider.CollectAsync(Request(FindingCategory.Security));

        Assert.Equal(["run", "--model", "deepseek/deepseek-v4-flash", fake.Instruction], fake.LastRequest!.Arguments);
    }

    [Fact]
    public void Cli_appsettings_keeps_the_m141_isolated_port_and_explicit_model()
    {
        // Regression guard (M14.1 hang): the CLI loads ITS appsettings.json from
        // AppContext.BaseDirectory, so the shipped file MUST keep the isolated-port
        // mitigation. If Port is removed or set null, `opencode run` gets no `--port`
        // and falls back to the shared default server, where a busy interactive session
        // queues the run until the timeout — the exact hang that was fixed.
        var cliConfig = FindCliAppSettings();
        if (cliConfig is null) return; // repo tree not locatable (packaged test run)

        // The shipped file carries JSON comments (allowed by .NET's config provider),
        // so parse with the same leniency JsonDocument offers via JsonCommentHandling.Skip.
        var options = new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip };
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(cliConfig), options);
        var openCode = doc.RootElement.GetProperty("Evidence").GetProperty("OpenCode");

        Assert.True(openCode.TryGetProperty("Port", out var port) && port.GetInt32() == 0,
            "Evidence:OpenCode:Port must be 0 (bare --port ⇒ isolated local server, M14.1).");
        Assert.False(string.IsNullOrWhiteSpace(openCode.GetProperty("Model").GetString()),
            "Evidence:OpenCode:Model must name the explicit model (M13.3) that rides with --port.");
        Assert.False(openCode.GetProperty("Enabled").GetBoolean(),
            "OpenCode stays offline by default; a real provider is selected explicitly.");
    }

    private static string? FindCliAppSettings()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EngineeringCouncil.slnx")))
            dir = dir.Parent;
        var candidate = dir is null ? null : Path.Combine(dir.FullName, "src", "EngineeringCouncil.Cli", "appsettings.json");
        return candidate is not null && File.Exists(candidate) ? candidate : null;
    }

    // ── 3. Telemetry ───────────────────────────────────────────────────────────

    [Fact]
    public async Task OpenCode_configured_model_appears_in_evidence_telemetry()
    {
        var fake = new FakeOpenCodeRunner();
        var provider = new OpenCodeEvidenceProvider(
            new OpenCodeOptions { Enabled = true, Model = "deepseek/deepseek-v4-flash" }, fake);

        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var evidence = Assert.Single(result.Evidence);
        Assert.True(evidence.Success);
        Assert.Equal("deepseek/deepseek-v4-flash", evidence.Metadata["model"]);
        Assert.Equal(EvidenceProviderType.Agentic, evidence.ProviderType);
        Assert.Equal("opencode", evidence.ProviderId);
    }

    [Fact]
    public async Task OpenCode_no_api_key_or_credential_is_written_to_telemetry_or_artifacts()
    {
        // Credentials belong to OpenCode's own auth (DEEPSEEK_API_KEY); the provider
        // never sees or carries the secret, so no key value can leak into Evidence.
        var fake = new FakeOpenCodeRunner();
        var provider = new OpenCodeEvidenceProvider(
            new OpenCodeOptions { Enabled = true, Model = "deepseek/deepseek-v4-flash" }, fake);

        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var evidence = Assert.Single(result.Evidence);
        var serialized = System.Text.Json.JsonSerializer.Serialize(evidence, CouncilJson.Options);
        Assert.DoesNotContain("sk-", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_key", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", serialized, StringComparison.OrdinalIgnoreCase);
        // Only the model ID is carried; no provider options or secrets.
        Assert.Equal("deepseek/deepseek-v4-flash", evidence.Metadata["model"]);
    }

    // ── 4. Context & structured evidence semantics ─────────────────────────────

    [Fact]
    public async Task OpenCode_context_fingerprint_remains_unavailable_with_a_model_configured()
    {
        var fake = new FakeOpenCodeRunner();
        var provider = new OpenCodeEvidenceProvider(
            new OpenCodeOptions { Enabled = true, Model = "deepseek/deepseek-v4-flash" }, fake);

        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(string.Empty, evidence.ContextFingerprint);
        Assert.Equal("agentic", evidence.ContextSelectionStrategy);
        var record = Assert.Single(result.Executions);
        Assert.Equal(string.Empty, record.ContextFingerprint);
        Assert.Equal(EvidenceProviderType.Agentic, record.ProviderType);
    }

    [Fact]
    public async Task OpenCode_structured_result_still_becomes_existing_evidence()
    {
        var fake = new FakeOpenCodeRunner
        {
            Handler = (_, _) => Task.FromResult(Result(0, StructuredJson(FindingCategory.Security, "src/Auth.cs")))
        };
        var provider = new OpenCodeEvidenceProvider(
            new OpenCodeOptions { Enabled = true, Model = "deepseek/deepseek-v4-flash" }, fake);

        var result = await Executor(provider).ExecuteAsync(Plan([provider], [FindingCategory.Security]), Snapshot);

        var evidence = Assert.Single(result.Evidence);
        Assert.True(evidence.Success);
        Assert.Equal(EvidenceProviderType.Agentic, evidence.ProviderType);
        Assert.Contains("\"observations\"", evidence.RawResponse);
    }

    // ── 5. End to end: unchanged pipeline + consumer-compatible package ─────────

    [Fact]
    public async Task OpenCode_end_to_end_flows_through_observations_findings_and_a_consumer_compatible_package()
    {
        var repo = Path.Combine(Path.GetTempPath(), "ec-opencode-m13-3-repo-" + Guid.NewGuid().ToString("N"));
        var outputs = Path.Combine(Path.GetTempPath(), "ec-opencode-m13-3-out-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(repo, "src"));
            File.WriteAllText(Path.Combine(repo, "Sample.sln"), "solution\n");
            File.WriteAllText(Path.Combine(repo, "src", "Auth.cs"), "public class Auth { void Authorize() { } }\n");

            var fake = new FakeOpenCodeRunner
            {
                Handler = (_, _) => Task.FromResult(Result(0, StructuredJson(FindingCategory.Security, "src/Auth.cs")))
            };
            var provider = new OpenCodeEvidenceProvider(
                new OpenCodeOptions { Enabled = true, Model = "deepseek/deepseek-v4-flash" }, fake);
            var pipeline = BuildPipeline([provider], [FindingCategory.Security], outputs);

            var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = repo, ProviderName = "OpenCode" });

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
            // The external contract carries neither OpenCode- nor DeepSeek-specific fields.
            Assert.DoesNotContain("providerComparison", packageJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("deepseek", packageJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"model\"", packageJson);
            Assert.Contains("OpenCode", packageJson);
        }
        finally
        {
            foreach (var dir in new[] { repo, outputs })
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void OpenCode_consumer_dto_gate_stays_green_for_the_package_contract()
    {
        // The consumer DTO (PackageContract) shape is unchanged by M13.3 — model
        // selection is internal execution telemetry, never part of the external contract.
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

    private static EvidenceRequest Request(FindingCategory discipline, string root = "/repo") => new()
    {
        RunId = "run1",
        RepositorySnapshot = Snapshot with { RootPath = root },
        Scope = EvidenceAcquisitionScope.Discipline,
        Discipline = discipline,
        Instructions = DisciplinePrompts.BuildInstructions(EvidenceAcquisitionScope.Discipline, discipline),
        ContextSelection = AgenticSelection(Snapshot),
        ProviderNames = ["OpenCode"],
        CorrelationId = "OpenCode:Security"
    };

    private static AnalysisContextSelection AgenticSelection(RepositorySnapshot repository) => new()
    {
        Strategy = "agentic",
        Files = [],
        TotalRepositoryFiles = repository.Files.Count,
        SelectedFileCount = 0,
        EstimatedContentSize = 0
    };

    private static OpenCodeProcessResult Result(int exitCode, string stdout, string stderr = "")
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
                  "description": "The OpenCode agent found a likely credential while exploring.",
                  "severity": "Medium",
                  "confidence": "Medium",
                  "ruleId": "OC-001",
                  "fileReferences": [ { "path": "{{filePath}}" } ],
                  "symbolReferences": [],
                  "lineReferences": [],
                  "evidenceExcerpt": "Agent exploration.",
                  "recommendationHint": "Review and rotate.",
                  "tags": ["agentic", "opencode"]
                }
              ]
            }
            """;

    private static AnalysisRunConfiguration Config() => new() { RunId = "run1" };

    private static EvidenceAcquisitionPlan Plan(
        IReadOnlyCollection<IEvidenceProvider> providers, IReadOnlyCollection<FindingCategory> disciplines)
        => new EvidenceAcquisitionPlanner().CreatePlan(Config(), Snapshot, providers, disciplines);

    private static EvidenceAcquisitionExecutor Executor(params IEvidenceProvider[] providers)
        => new(new EvidenceProviderFactory(providers), new RuleBasedAnalysisContextSelector(),
            new EvidenceOptions { ProviderTimeout = TimeSpan.FromSeconds(30) });

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
    /// Normal tests never need a real OpenCode installation.
    /// </summary>
    private sealed class FakeOpenCodeRunner : IOpenCodeProcessRunner
    {
        public OpenCodeProcessRequest? LastRequest { get; private set; }

        public Func<OpenCodeProcessRequest, CancellationToken, Task<OpenCodeProcessResult>> Handler { get; set; }
            = (_, _) => Task.FromResult(Result(0, StructuredJson(FindingCategory.Security, "src/Auth.cs")));

        /// <summary>The analysis instruction — always the final argument of <c>["run", ...]</c>.</summary>
        public string Instruction => LastRequest!.Arguments[^1];

        public Task<OpenCodeProcessResult> RunAsync(OpenCodeProcessRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Handler(request, cancellationToken);
        }
    }
}
