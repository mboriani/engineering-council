using EngineeringCouncil.Agent.Analyzers;
using EngineeringCouncil.Core.Abstractions;
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
/// Milestone 014.1 — the first multi-agent Engineering Council run. OpenCode,
/// Codex and Claude Code — three real <see cref="EvidenceProviderType.Agentic"/>
/// runtimes — participate in ONE AnalysisRun against the SAME repository and the
/// SAME discipline (Security), producing ONE reconciled Engineering Review Package.
///
/// The point is that the EXISTING architecture already executes the Council: one
/// plan, one executor, one interpreter, the existing deterministic reconciler
/// (no voting, no weights, no LLM), and one package. These tests prove that
/// contract offline with fake process runners — no OpenCode/Codex/Claude Code
/// installation, no network, no keys, no paid calls.
/// </summary>
public sealed class CouncilTests
{
    private static readonly FindingCategory[] Security = [FindingCategory.Security];
    private static readonly string[] CouncilProviders = ["ClaudeCode", "Codex", "OpenCode"];

    private static RepositorySnapshot Snapshot => new()
    {
        RootPath = "/repo",
        SolutionName = "Repo",
        ProjectFiles = ["src/App.csproj"],
        Files =
        [
            new ScannedFile { RelativePath = "src/Auth.cs", Extension = ".cs", SizeBytes = 1, LineCount = 60, Content = "public class Auth { const string Pwd = \"P@ssw0rd!\"; }" },
            new ScannedFile { RelativePath = "src/Web.cs", Extension = ".cs", SizeBytes = 1, LineCount = 30, Content = "public class Web { void Render(){ } }" }
        ]
    };

    // ── 1. One acquisition plan ──────────────────────────────────────────────

    [Fact]
    public void Council_plan_creates_one_security_step_per_agentic_provider()
    {
        IEvidenceProvider[] providers =
        [
            new OpenCodeEvidenceProvider(new OpenCodeOptions { Enabled = true, Executable = "opencode" }, new FakeOpenCodeRunner()),
            new CodexEvidenceProvider(new CodexOptions { Enabled = true, Executable = "codex" }, new FakeCodexRunner()),
            new ClaudeCodeEvidenceProvider(new ClaudeCodeOptions { Enabled = true, Executable = "claude" }, new FakeClaudeCodeRunner())
        ];

        var plan = new EvidenceAcquisitionPlanner().CreatePlan(
            new AnalysisRunConfiguration { RunId = "run1" }, Snapshot, providers, Security);

        Assert.Equal("run1", plan.RunId); // ONE run
        Assert.Equal(3, plan.Steps.Count);
        Assert.Equal(["OpenCode#Security", "Codex#Security", "ClaudeCode#Security"],
            plan.Steps.Select(s => s.StepId));
        Assert.All(plan.Steps, s =>
        {
            Assert.Equal(FindingCategory.Security, s.Discipline);
            Assert.Equal(EvidenceAcquisitionScope.Discipline, s.Scope);
        });
        Assert.Empty(plan.UnsupportedCombinations);
    }

    // ── 2. One AnalysisRun ───────────────────────────────────────────────────

    [Fact]
    public async Task Council_three_providers_run_in_one_analysis_run()
    {
        using var h = await CouncilHarness.RunAsync();
        var run = h.Result.Run;

        Assert.Equal(3, run.ProviderExecution!.Records.Count);
        Assert.Equal(CouncilProviders, run.Evidence.Select(e => e.ProviderName).OrderBy(x => x));
        Assert.All(run.Evidence, e => Assert.Equal(EvidenceProviderType.Agentic, e.ProviderType));

        // Every execution belongs to the same run.
        Assert.Equal(3, run.AcquisitionPlan!.Steps.Count);
        Assert.All(run.ProviderExecution.Records, r => Assert.Equal(run.RunId, r.RunId));
        Assert.Equal(
            run.AcquisitionPlan.Steps.Select(s => s.StepId).OrderBy(x => x),
            run.Evidence.Select(e => e.AcquisitionStepId).OrderBy(x => x));
    }

    // ── 3. Same repository root ──────────────────────────────────────────────

    [Fact]
    public async Task Council_all_providers_analyze_the_same_repository_root()
    {
        using var h = await CouncilHarness.RunAsync();

        Assert.Equal(h.RepoPath, h.OpenCode.LastRequest!.WorkingDirectory);
        Assert.Equal(h.RepoPath, h.Codex.LastRequest!.WorkingDirectory);
        Assert.Equal(h.RepoPath, h.ClaudeCode.LastRequest!.WorkingDirectory);
        Assert.Equal(
            h.OpenCode.LastRequest.WorkingDirectory,
            h.Codex.LastRequest.WorkingDirectory);
        Assert.Equal(
            h.OpenCode.LastRequest.WorkingDirectory,
            h.ClaudeCode.LastRequest.WorkingDirectory);
    }

    // ── 4. Evidence attribution ──────────────────────────────────────────────

    [Fact]
    public async Task Council_provider_evidence_remains_independently_attributable()
    {
        using var h = await CouncilHarness.RunAsync();
        var run = h.Result.Run;

        Assert.Equal(
            ["ClaudeCode", "Codex", "OpenCode"],
            run.Evidence.Select(e => e.ProviderName).OrderBy(x => x));
        Assert.Equal(
            ["claudecode", "codex", "opencode"],
            run.Evidence.Select(e => e.ProviderId).OrderBy(x => x));

        // Every observation traces back to exactly the evidence + provider that produced it.
        var observations = run.Observations;
        Assert.Equal(
            ["ClaudeCode", "Codex", "OpenCode"],
            observations.Select(o => o.SourceProvider).Distinct().OrderBy(x => x));
        Assert.All(observations, o =>
        {
            Assert.Equal(EvidenceProviderType.Agentic, o.SourceProviderType);
            Assert.Contains(run.Evidence, e =>
                e.Id == o.SourceEvidenceId && e.ProviderName == o.SourceProvider);
        });
    }

    // ── 5. Raw finding provenance ────────────────────────────────────────────

    [Fact]
    public async Task Council_raw_findings_preserve_provider_provenance()
    {
        using var h = await CouncilHarness.RunAsync();
        var raws = h.Result.Run.RawFindings;

        // Each provider's independent conclusion is a separate raw finding.
        Assert.Equal(4, raws.Count);
        Assert.All(raws, f =>
        {
            Assert.NotEmpty(f.EvidenceProvider);
            Assert.Single(f.SupportingProviders);
        });

        // The exclusive XSS issue is attributed to OpenCode only.
        var xss = raws.Single(f => f.SourceRules.Contains("CWE-79"));
        Assert.Equal("OpenCode", xss.EvidenceProvider);
    }

    // ── 6-8. Reconciliation consolidates across providers ───────────────────

    [Fact]
    public async Task Council_deterministic_reconciliation_consolidates_multi_provider_findings()
    {
        using var h = await CouncilHarness.RunAsync();
        var credential = h.Result.Run.Findings.Single(f => f.SourceRules.Contains("CWE-798"));

        Assert.True(credential.IsConsolidated);
        Assert.Equal("exact-rule-location", credential.ReconciliationStrategy);
        Assert.Equal(3, credential.SupportingFindingIds.Count);
        Assert.Equal(3, credential.SupportingObservationCount);
        Assert.Contains("3 independent providers agree", credential.ConfidenceRationale);
        Assert.Equal(FindingConfidence.High, credential.Confidence); // corroboration, not averaging
    }

    [Fact]
    public async Task Council_supporting_providers_are_distinct_provider_names()
    {
        using var h = await CouncilHarness.RunAsync();
        var credential = h.Result.Run.Findings.Single(f => f.SourceRules.Contains("CWE-798"));

        Assert.Equal(["ClaudeCode", "Codex", "OpenCode"], credential.SupportingProviders);
        Assert.Equal(
            credential.SupportingProviders.Count,
            credential.SupportingProviders.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("OpenCode", credential.SupportingProviders);
        Assert.Contains("Codex", credential.SupportingProviders);
        Assert.Contains("ClaudeCode", credential.SupportingProviders);
    }

    [Fact]
    public async Task Council_agreement_count_counts_distinct_providers_not_findings()
    {
        using var h = await CouncilHarness.RunAsync();
        var credential = h.Result.Run.Findings.Single(f => f.SourceRules.Contains("CWE-798"));

        Assert.Equal(3, credential.SupportingProviders.Count);
        Assert.Equal(credential.SupportingProviders.Count, credential.AgreementCount);
        Assert.True(credential.AgreementCount >= 2);
    }

    // ── 9. Exclusive finding ─────────────────────────────────────────────────

    [Fact]
    public async Task Council_exclusive_single_provider_finding_is_preserved()
    {
        using var h = await CouncilHarness.RunAsync();
        var xss = h.Result.Run.Findings.Single(f => f.SourceRules.Contains("CWE-79"));

        Assert.False(xss.IsConsolidated);
        Assert.Equal("standalone", xss.ReconciliationStrategy);
        Assert.Equal(1, xss.AgreementCount);
        Assert.Equal(["OpenCode"], xss.SupportingProviders);
        Assert.Equal("Standalone finding — no equivalent from another source.", xss.ReconciliationReason);
    }

    // ── 10. Failure isolation ────────────────────────────────────────────────

    [Fact]
    public async Task Council_one_failed_provider_does_not_stop_successful_ones_under_continue()
    {
        using var h = await CouncilHarness.RunAsync(h => h.ClaudeCode.Handler =
            (_, _) => Task.FromResult(new ClaudeCodeProcessResult { ExitCode = 1, StandardOutput = "", StandardError = "claude: boom" }));

        var run = h.Result.Run;
        Assert.Equal(AnalysisRunStatus.Completed, run.Status);
        Assert.Null(run.Error);

        Assert.Equal(3, run.ProviderExecution!.Records.Count);
        var failed = run.ProviderExecution.Records.Single(r => !r.Success);
        Assert.Equal("ClaudeCode", failed.ProviderName);
        Assert.Equal("ProcessError", failed.ErrorCategory);
        Assert.Equal(2, run.ProviderExecution.SuccessfulExecutionCount);
        Assert.Equal(1, run.ProviderExecution.Failures);

        // The successful providers still produce findings and the package is written.
        Assert.Contains(run.Findings, f => f.SupportingProviders.Contains("OpenCode"));
        Assert.True(File.Exists(Path.Combine(h.Result.OutputDirectory, "engineering-review-package.json")));
    }

    // ── 11. ContextFingerprint semantics ─────────────────────────────────────

    [Fact]
    public async Task Council_agentic_context_fingerprint_stays_unavailable()
    {
        using var h = await CouncilHarness.RunAsync();

        Assert.All(h.Result.Run.Evidence, e => Assert.Equal(string.Empty, e.ContextFingerprint));
        Assert.All(h.Result.Run.ProviderExecution!.Records, r => Assert.Equal(string.Empty, r.ContextFingerprint));
    }

    // ── 12. M12 controlled-context comparison stays off for agentic ──────────

    [Fact]
    public async Task Council_m12_controlled_context_comparison_is_not_activated()
    {
        using var h = await CouncilHarness.RunAsync();

        Assert.Null(h.Result.Run.ProviderComparison);
        Assert.Null(h.Result.Run.CalibrationDiagnostics);
        Assert.False(File.Exists(Path.Combine(h.Result.OutputDirectory, "provider-comparison.json")));
        Assert.False(File.Exists(Path.Combine(h.Result.OutputDirectory, "calibration-diagnostics.json")));
    }

    // ── 13-14. One external package + consumer DTO gate ──────────────────────

    [Fact]
    public async Task Council_generates_one_consumer_compatible_package()
    {
        using var h = await CouncilHarness.RunAsync();

        var pkgPath = Path.Combine(h.Result.OutputDirectory, "engineering-review-package.json");
        Assert.True(File.Exists(pkgPath));
        Assert.Single(Directory.GetFiles(h.Result.OutputDirectory, "engineering-review-package.json"));

        var json = File.ReadAllText(pkgPath);
        var dto = System.Text.Json.JsonSerializer.Deserialize<PackageContract>(json, CouncilJson.Options);
        Assert.NotNull(dto);
        Assert.Equal("1.1", dto.SchemaVersion);
        Assert.DoesNotContain("providerComparison", json, StringComparison.OrdinalIgnoreCase);

        // The reconciled council result, not three provider-specific packages.
        Assert.Equal(2, dto.Findings.Count);
        Assert.NotNull(dto.Reconciliation);
        Assert.Equal(1, dto.Reconciliation.MultiProviderFindingCount);
        Assert.Single(dto.Findings, f => f.AgreementCount == 3);
        Assert.Single(dto.Findings, f => f.AgreementCount == 1);
    }

    [Fact]
    public async Task Council_markdown_represents_supporting_providers()
    {
        using var h = await CouncilHarness.RunAsync();

        var mdPath = Path.Combine(h.Result.OutputDirectory, "engineering-review.md");
        Assert.True(File.Exists(mdPath));
        var md = File.ReadAllText(mdPath);

        // The multi-provider finding is visible in the human-readable projection.
        Assert.Contains("_Providers: ClaudeCode, Codex, OpenCode", md);
        Assert.Contains("agreement 3", md);
        Assert.Contains("## Multi-Source Reconciliation", md);
        Assert.Contains("**Providers represented:**", md);
    }

    // ── Harness ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds ONE real Council-style AnalysisRun over a tiny temporary repository
    /// with the three Agentic providers driven by fake process runners. Default
    /// agents return the credential issue (same CWE-798 rule + same file, each with
    /// its own observation type) and OpenCode additionally returns an exclusive XSS
    /// issue — so the default run yields 4 raw findings that reconcile into one
    /// 3-provider finding plus one single-provider finding.
    /// </summary>
    private sealed class CouncilHarness : IDisposable
    {
        public string RepoPath { get; }
        public string OutputsPath { get; }
        public FakeOpenCodeRunner OpenCode { get; } = new();
        public FakeCodexRunner Codex { get; } = new();
        public FakeClaudeCodeRunner ClaudeCode { get; } = new();
        public AnalysisResult Result { get; private set; } = null!;

        private CouncilHarness(string repoPath, string outputsPath)
        {
            RepoPath = repoPath;
            OutputsPath = outputsPath;
        }

        public static async Task<CouncilHarness> RunAsync(Action<CouncilHarness>? configure = null)
        {
            var repo = Path.Combine(Path.GetTempPath(), "ec-council-repo-" + Guid.NewGuid().ToString("N"));
            var outputs = Path.Combine(Path.GetTempPath(), "ec-council-out-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(repo, "src"));
            File.WriteAllText(Path.Combine(repo, "Sample.sln"), "solution\n");
            File.WriteAllText(Path.Combine(repo, "src", "Auth.cs"), "public class Auth { const string Pwd = \"P@ssw0rd!\"; }\n");
            File.WriteAllText(Path.Combine(repo, "src", "Web.cs"), "public class Web { void Render() { } }\n");

            var harness = new CouncilHarness(repo, outputs);
            configure?.Invoke(harness);

            IEvidenceProvider[] providers =
            [
                new OpenCodeEvidenceProvider(new OpenCodeOptions { Enabled = true, Executable = "opencode" }, harness.OpenCode),
                new CodexEvidenceProvider(new CodexOptions { Enabled = true, Executable = "codex" }, harness.Codex),
                new ClaudeCodeEvidenceProvider(new ClaudeCodeOptions { Enabled = true, Executable = "claude" }, harness.ClaudeCode)
            ];

            harness.Result = await BuildPipeline(providers, Security, outputs)
                .RunAsync(new AnalysisRequest
                {
                    TargetPath = repo,
                    ProviderName = "OpenCode, Codex, ClaudeCode"
                });

            return harness;
        }

        public void Dispose()
        {
            foreach (var dir in new[] { RepoPath, OutputsPath })
                try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

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
            new AnalysisOrchestrator([new SecurityAnalyzer()]),
            new RuleBasedFindingReconciler(),
            new RuleBasedCouncilSummaryGenerator(),
            new EngineeringReviewPackageBuilder(),
            options,
            new FileSystemAnalysisRunRepository(
                new FileSystemRunRepositoryOptions { OutputsRoot = outputs },
                new EngineeringReviewMarkdownExporter(), new JsonReportGenerator()));
    }

    private static OpenCodeProcessResult Result(int exitCode, string stdout, string stderr = "")
        => new() { ExitCode = exitCode, StandardOutput = stdout, StandardError = stderr };

    private static string StructuredJson(FindingCategory discipline, params string[] observations)
        => $$"""{"schemaVersion":"1.0","discipline":"{{discipline}}","observations":[{{string.Join(",", observations)}}]}""";

    private static string Obs(string type, string title, string rule, string file, int line, string excerpt)
        => $$"""
            {
              "type": "{{type}}",
              "discipline": "Security",
              "title": "{{title}}",
              "description": "{{excerpt}}",
              "severity": "Medium",
              "confidence": "Medium",
              "ruleId": "{{rule}}",
              "fileReferences": [ { "path": "{{file}}", "startLine": {{line}} } ],
              "symbolReferences": [],
              "lineReferences": [ {{line}} ],
              "evidenceExcerpt": "{{excerpt}}",
              "recommendationHint": "Review and remediate.",
              "tags": ["agentic"]
            }
            """;

    // The three agents describe the SAME underlying credential issue with different
    // normalized types (so the analyzer produces three independent raw findings) but
    // the same rule + file + line, so the deterministic reconciler's stage-1
    // exact-rule-location consolidates them into one 3-provider finding.
    private static string OpenCodeJson()
        => StructuredJson(FindingCategory.Security,
            Obs("HardcodedSecret", "Possible sensitive value in source/config", "CWE-798", "src/Auth.cs", 42, "OpenCode found a hardcoded secret in Auth.cs."),
            Obs("CrossSiteScripting", "Potential cross-site scripting", "CWE-79", "src/Web.cs", 10, "OpenCode found unsanitized output in Web.cs."));

    private static string CodexJson()
        => StructuredJson(FindingCategory.Security,
            Obs("PlainTextPassword", "Credential stored in plain text", "CWE-798", "src/Auth.cs", 42, "Codex found a plain text password in Auth.cs."));

    private static string ClaudeCodeJson()
        => StructuredJson(FindingCategory.Security,
            Obs("InsecureCredentials", "Hardcoded credential discovered", "CWE-798", "src/Auth.cs", 42, "Claude Code found a credential in Auth.cs."));

    private static string Envelope(string resultText)
        => $$"""{"type":"result","subtype":"success","is_error":false,"result":{{System.Text.Json.JsonSerializer.Serialize(resultText)}}}""";

    private sealed class FakeOpenCodeRunner : IOpenCodeProcessRunner
    {
        public OpenCodeProcessRequest? LastRequest { get; private set; }
        public Func<OpenCodeProcessRequest, CancellationToken, Task<OpenCodeProcessResult>> Handler { get; set; }
            = (_, _) => Task.FromResult(Result(0, OpenCodeJson()));

        public Task<OpenCodeProcessResult> RunAsync(OpenCodeProcessRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Handler(request, cancellationToken);
        }
    }

    private sealed class FakeCodexRunner : ICodexProcessRunner
    {
        public CodexProcessRequest? LastRequest { get; private set; }
        public Func<CodexProcessRequest, CancellationToken, Task<CodexProcessResult>> Handler { get; set; }
            = (_, _) => Task.FromResult(new CodexProcessResult { ExitCode = 0, StandardOutput = CodexJson(), StandardError = "" });

        public Task<CodexProcessResult> RunAsync(CodexProcessRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Handler(request, cancellationToken);
        }
    }

    private sealed class FakeClaudeCodeRunner : IClaudeCodeProcessRunner
    {
        public ClaudeCodeProcessRequest? LastRequest { get; private set; }
        public Func<ClaudeCodeProcessRequest, CancellationToken, Task<ClaudeCodeProcessResult>> Handler { get; set; }
            = (_, _) => Task.FromResult(new ClaudeCodeProcessResult { ExitCode = 0, StandardOutput = Envelope(ClaudeCodeJson()), StandardError = "" });

        public Task<ClaudeCodeProcessResult> RunAsync(ClaudeCodeProcessRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Handler(request, cancellationToken);
        }
    }
}
