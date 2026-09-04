using System.Text.Json;
using EngineeringCouncil.Agent.Analyzers;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Analysis;
using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Core.Serialization;
using EngineeringCouncil.Infrastructure.Acquisition;
using EngineeringCouncil.Infrastructure.Evidence;
using EngineeringCouncil.Infrastructure.Interpretation;
using EngineeringCouncil.Infrastructure.Llm;
using EngineeringCouncil.Infrastructure.Persistence;
using EngineeringCouncil.Infrastructure.Reconciliation;
using EngineeringCouncil.Infrastructure.Reporting;
using EngineeringCouncil.Infrastructure.Scanning;
using EngineeringCouncil.Infrastructure.Summarizing;
using EngineeringCouncil.Tests.Contracts;
using EngineeringCouncil.Tests.Fakes;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 012.2 — real provider comparison. The comparison is a deterministic,
/// provider-neutral, descriptive projection of an <see cref="AnalysisRun"/>'s own
/// data: no provider calls, no rescan, no context rebuild, no re-interpretation,
/// and no ranking/scoring/weighting/calibration. It is an INTERNAL diagnostic
/// artifact — the external engineering-review-package.json contract is unchanged.
/// </summary>
public sealed class ProviderComparisonTests : IDisposable
{
    private const string KeyVariable = "EC_TEST_COMPARISON_KEY";
    private const string KeyValue = "comparison-test-key-not-a-real-secret";

    private readonly string _repo = Path.Combine(Path.GetTempPath(), "ec-comparison-repo-" + Guid.NewGuid().ToString("N"));
    private readonly string _outputs = Path.Combine(Path.GetTempPath(), "ec-comparison-out-" + Guid.NewGuid().ToString("N"));

    public ProviderComparisonTests()
    {
        Environment.SetEnvironmentVariable(KeyVariable, KeyValue);
        Directory.CreateDirectory(Path.Combine(_repo, "src"));
        File.WriteAllText(Path.Combine(_repo, "Sample.sln"), "solution\n");
        File.WriteAllText(Path.Combine(_repo, "src", "Payments.cs"), "public class PaymentClient { const string K = \"x\"; }\n");
        File.WriteAllText(Path.Combine(_repo, "src", "Query.cs"), "public class Query { }\n");
        File.WriteAllText(Path.Combine(_repo, "src", "Deser.cs"), "public class Deser { }\n");
    }

    // ── Context fingerprint: equivalence identity ─────────────────────────────

    [Fact]
    public void Same_effective_context_yields_the_same_fingerprint_regardless_of_provider()
    {
        var selection = Sel(
            Src("src/Payments.cs", "public class A { const string K = \"x\"; }"),
            Src("src/Auth.cs", "public class Auth { }"));

        // The fingerprint API takes no provider at all — provider identity can never
        // influence the value. Two providers given this selection both see the same id.
        var a = ContextFingerprint.Compute(selection);
        var b = ContextFingerprint.Compute(selection);

        Assert.Equal(a, b);
        Assert.StartsWith("CTX-", a);
        Assert.Equal(16, a.Length); // "CTX-" + 12 hex chars
    }

    [Fact]
    public void Fingerprint_changes_when_effective_file_content_changes()
    {
        var one = Sel(Src("src/Payments.cs", "const string K = \"x\";"));
        var two = Sel(Src("src/Payments.cs", "const string K = \"y\";"));

        Assert.NotEqual(ContextFingerprint.Compute(one), ContextFingerprint.Compute(two));
    }

    [Fact]
    public void Fingerprint_is_order_independent_and_excludes_absolute_paths()
    {
        // Files are canonicalized by relative path; the selection's file ORDER must
        // not matter. Only RelativePath + effective content are hashed (no absolute
        // path, provider, model, keys, timestamps, or RunId are ever in scope).
        var a = Sel(Src("src/Auth.cs", "class Auth { }"), Src("src/Payments.cs", "const string K = \"x\";"));
        var b = Sel(Src("src/Payments.cs", "const string K = \"x\";"), Src("src/Auth.cs", "class Auth { }"));

        Assert.Equal(ContextFingerprint.Compute(a), ContextFingerprint.Compute(b));
    }

    [Fact]
    public void Fingerprint_respects_the_effective_context_policy_truncation()
    {
        var policy = new ContextContentPolicy { MaxCharactersPerFile = 4 };
        var longContent = "abcdefghijklmnop";
        var selection = Sel(Src("src/Payments.cs", longContent));

        var fingerprint = ContextFingerprint.Compute(selection, policy);
        var truncatedSelection = Sel(Src("src/Payments.cs", longContent[..4]));
        var truncatedFingerprint = ContextFingerprint.Compute(truncatedSelection, policy);

        Assert.Equal(truncatedFingerprint, fingerprint);
        Assert.NotEqual(ContextFingerprint.Compute(selection, new ContextContentPolicy { MaxCharactersPerFile = 8 }), fingerprint);
    }

    // ── Comparability boundaries ──────────────────────────────────────────────

    [Fact]
    public void Single_provider_run_produces_no_comparison()
    {
        var run = Run(
            [Rec("Claude", FindingCategory.Security), Rec("Claude", FindingCategory.Reliability)]);

        Assert.Null(ProviderComparisonBuilder.Build(run));
    }

    [Fact]
    public void Two_providers_same_discipline_same_context_are_comparable()
    {
        var run = Run(
            [Rec("Claude", FindingCategory.Security), Rec("OpenAI", FindingCategory.Security)]);

        var report = ProviderComparisonBuilder.Build(run)!;

        var discipline = Assert.Single(report.DisciplineComparisons);
        Assert.Equal(FindingCategory.Security, discipline.Discipline);
        Assert.Equal(ProviderComparisonStatus.Comparable, discipline.Status);
        Assert.True(discipline.ComparableContext);
        Assert.Equal("CTX-abc123", discipline.ContextFingerprint);
        Assert.Equal(["Claude", "OpenAI"], discipline.Providers);
        Assert.NotNull(discipline.Agreement);
    }

    [Fact]
    public void Different_context_fingerprints_mark_the_discipline_non_comparable()
    {
        var run = Run(
            [Rec("Claude", FindingCategory.Security, "CTX-aaaa"), Rec("OpenAI", FindingCategory.Security, "CTX-bbbb")]);

        var report = ProviderComparisonBuilder.Build(run)!;

        var discipline = Assert.Single(report.DisciplineComparisons);
        Assert.Equal(ProviderComparisonStatus.NonComparable, discipline.Status);
        Assert.False(discipline.ComparableContext);
        Assert.Equal(string.Empty, discipline.ContextFingerprint);
        Assert.Null(discipline.Agreement);
        Assert.NotEmpty(discipline.ExecutionMetrics); // execution metrics still reported
        Assert.Contains(report.Limitations, l => l.Contains("different effective contexts", StringComparison.Ordinal));
    }

    [Fact]
    public void Executions_for_different_disciplines_are_compared_separately()
    {
        var run = Run(
            [Rec("Claude", FindingCategory.Security), Rec("OpenAI", FindingCategory.Security),
             Rec("Claude", FindingCategory.Reliability), Rec("OpenAI", FindingCategory.Reliability)]);

        var report = ProviderComparisonBuilder.Build(run)!;

        Assert.Equal(2, report.DisciplineComparisons.Count);
        Assert.Equal([FindingCategory.Security, FindingCategory.Reliability],
            report.DisciplineComparisons.Select(d => d.Discipline).ToList());
        Assert.All(report.DisciplineComparisons, d => Assert.Equal(2, d.Providers.Count));
    }

    [Fact]
    public void Static_analysis_executions_are_excluded_from_the_comparison()
    {
        var run = Run(
            [Rec("Claude", FindingCategory.Security), Rec("OpenAI", FindingCategory.Security),
             Rec("SARIF", FindingCategory.Security, type: EvidenceProviderType.StaticAnalyzer)]);

        var report = ProviderComparisonBuilder.Build(run)!;

        var discipline = Assert.Single(report.DisciplineComparisons);
        Assert.Equal(["Claude", "OpenAI"], discipline.Providers);
        Assert.DoesNotContain(report.ComparedProviders, p => p == "SARIF");
        Assert.All(report.OverallExecutionMetrics, m => Assert.NotEqual("SARIF", m.ProviderName));
    }

    // ── Execution metrics: M12.1 telemetry reused verbatim ────────────────────

    [Fact]
    public void Execution_metrics_reuse_the_m121_telemetry_without_recollection()
    {
        var run = Run([
            Rec("Claude", FindingCategory.Security, input: 1200, output: 300, total: 1500,
                retries: 1, repairs: 1, repairSucceeded: true, model: "claude-4"),
            Rec("OpenAI", FindingCategory.Security, input: 90, output: 10, total: 100, model: "gpt-4o")
        ]);

        var discipline = ProviderComparisonBuilder.Build(run)!.DisciplineComparisons[0];

        var claude = Assert.Single(discipline.ExecutionMetrics, e => e.Provider == "Claude");
        Assert.Equal("claude-4", claude.Model);
        Assert.Equal(1200, claude.InputTokens);
        Assert.Equal(300, claude.OutputTokens);
        Assert.Equal(1500, claude.TotalTokens);
        Assert.Equal(1, claude.RetryCount);
        Assert.Equal(1, claude.RepairAttemptCount);
        Assert.True(claude.RepairSucceeded);
        Assert.True(claude.Success);

        var openai = Assert.Single(discipline.ExecutionMetrics, e => e.Provider == "OpenAI");
        Assert.Equal("gpt-4o", openai.Model);
        Assert.Equal(100, openai.TotalTokens);
        Assert.Equal(0, openai.RetryCount);
        Assert.Null(openai.RepairSucceeded);
    }

    [Fact]
    public void Unknown_token_usage_stays_unknown_never_zero_in_the_comparison()
    {
        var run = Run([
            Rec("Claude", FindingCategory.Security),
            Rec("OpenAI", FindingCategory.Security)
        ]);

        var discipline = ProviderComparisonBuilder.Build(run)!.DisciplineComparisons[0];

        Assert.All(discipline.ExecutionMetrics, e =>
        {
            Assert.Null(e.InputTokens);
            Assert.Null(e.OutputTokens);
            Assert.Null(e.TotalTokens);
        });

        var report = ProviderComparisonBuilder.Build(run)!;
        Assert.Contains(report.Limitations, l => l.Contains("token usage unknown", StringComparison.Ordinal));
    }

    [Fact]
    public void A_failed_provider_execution_marks_the_discipline_incomplete()
    {
        var run = Run([
            Rec("Claude", FindingCategory.Security),
            Rec("OpenAI", FindingCategory.Security, success: false, errorCategory: "Authentication")
        ]);

        var report = ProviderComparisonBuilder.Build(run)!;
        var discipline = Assert.Single(report.DisciplineComparisons);

        Assert.Equal(ProviderComparisonStatus.Incomplete, discipline.Status);
        Assert.Empty(discipline.ObservationMetrics); // output comparison unavailable
        Assert.Empty(discipline.FindingMetrics);
        Assert.Null(discipline.Agreement);

        var failed = Assert.Single(discipline.ExecutionMetrics, e => !e.Success);
        Assert.Equal("Authentication", failed.ErrorCategory);
        Assert.Equal(1, failed.Failures);
        Assert.Contains(report.Limitations, l =>
            l.Contains("output comparison unavailable", StringComparison.Ordinal)
            && l.Contains("OpenAI", StringComparison.Ordinal));
    }

    // ── Observation comparison ────────────────────────────────────────────────

    [Fact]
    public void Observation_counts_and_distributions_are_reported_per_provider()
    {
        var run = Run(
            [Rec("Claude", FindingCategory.Security, contextFiles: 2), Rec("OpenAI", FindingCategory.Security, contextFiles: 1)],
            observations:
            [
                Obs("o1", "Claude", FindingCategory.Security, type: "HardcodedSecret", severity: FindingSeverity.High, files: "src/Payments.cs"),
                Obs("o2", "Claude", FindingCategory.Security, type: "HardcodedSecret", severity: FindingSeverity.High, files: "src/Auth.cs"),
                Obs("o3", "Claude", FindingCategory.Security, type: "SqlInjection", severity: FindingSeverity.Medium),
                Obs("o4", "OpenAI", FindingCategory.Security, type: "HardcodedSecret", severity: FindingSeverity.High, files: "src/Payments.cs"),
                Obs("o5", "OpenAI", FindingCategory.Security, type: "WeakCrypto", severity: FindingSeverity.Low, confidence: FindingConfidence.Low)
            ]);

        var discipline = ProviderComparisonBuilder.Build(run)!.DisciplineComparisons[0];
        var claude = Assert.Single(discipline.ObservationMetrics, o => o.Provider == "Claude");
        var openai = Assert.Single(discipline.ObservationMetrics, o => o.Provider == "OpenAI");

        Assert.Equal(3, claude.ObservationCount);
        Assert.Equal(2, openai.ObservationCount);

        Assert.Equal(["HardcodedSecret", "SqlInjection"], claude.Types.Select(t => t.Name).ToList());
        Assert.Equal(2, claude.Types.Single(t => t.Name == "HardcodedSecret").Count);
        Assert.Equal(["High", "Medium"], claude.Severities.Select(s => s.Name).ToList());
        Assert.Equal(2, claude.FilesReferenced);
        Assert.Equal(2, claude.ObservationsWithLocation);
        Assert.Equal(1, claude.ObservationsWithoutLocation);

        Assert.Equal(1, openai.FilesReferenced);
        Assert.Equal(1.0, openai.ReferencedContextFileRate!.Value, 3);    }

    [Fact]
    public void Referenced_context_file_rate_is_null_when_no_context_files_were_supplied()
    {
        var run = Run(
            [Rec("Claude", FindingCategory.Security, contextFiles: 0), Rec("OpenAI", FindingCategory.Security, contextFiles: 0)],
            observations:
            [
                Obs("o1", "Claude", FindingCategory.Security, type: "HardcodedSecret", files: "src/Payments.cs"),
                Obs("o2", "OpenAI", FindingCategory.Security, type: "HardcodedSecret", files: "src/Payments.cs")
            ]);

        var discipline = ProviderComparisonBuilder.Build(run)!.DisciplineComparisons[0];
        Assert.All(discipline.ObservationMetrics, o => Assert.Null(o.ReferencedContextFileRate));
    }

    // ── Finding agreement: derived ONLY from the existing reconciler ──────────

    [Fact]
    public void Shared_findings_come_only_from_existing_reconciliation()
    {
        // Two providers report the same rule at the same file → the deterministic
        // reconciler merges them into one consolidated finding supported by both.
        var raw = new[]
        {
            Raw("c1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "Claude", line: 3),
            Raw("o1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "OpenAI", line: 4)
        };
        var run = Run(
            [Rec("Claude", FindingCategory.Security), Rec("OpenAI", FindingCategory.Security)],
            rawFindings: raw, findings: Reconcile(raw));

        var discipline = ProviderComparisonBuilder.Build(run)!.DisciplineComparisons[0];
        var agreement = discipline.Agreement!;

        Assert.Equal(1, agreement.ConsolidatedFindingCount);
        Assert.Equal(1, agreement.SharedFindingCount);
        Assert.Equal(1.0, agreement.AgreementRate!.Value, 3);
        Assert.Equal(1, agreement.MultiProviderFindingCount);
    }

    [Fact]
    public void Single_provider_consolidated_findings_are_exclusive_to_that_provider()
    {
        var raw = new[]
        {
            Raw("c1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "Claude", line: 3),
            Raw("o1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "OpenAI", line: 4),
            Raw("c2", "SQL injection", "CWE-89", "src/Query.cs", "Claude", line: 9),
            Raw("o2", "Unsafe deserialization", "CWE-502", "src/Deser.cs", "OpenAI", line: 5)
        };
        var run = Run(
            [Rec("Claude", FindingCategory.Security), Rec("OpenAI", FindingCategory.Security)],
            rawFindings: raw, findings: Reconcile(raw));

        var discipline = ProviderComparisonBuilder.Build(run)!.DisciplineComparisons[0];
        var agreement = discipline.Agreement!;

        Assert.Equal(3, agreement.ConsolidatedFindingCount);
        Assert.Equal(1, agreement.SharedFindingCount);
        Assert.Equal(1.0 / 3.0, agreement.AgreementRate!.Value, 3);

        Assert.Equal(1, agreement.ExclusiveFindingCountByProvider.Single(e => e.Name == "Claude").Count);
        Assert.Equal(1, agreement.ExclusiveFindingCountByProvider.Single(e => e.Name == "OpenAI").Count);

        var claude = Assert.Single(discipline.FindingMetrics, f => f.Provider == "Claude");
        var openai = Assert.Single(discipline.FindingMetrics, f => f.Provider == "OpenAI");
        Assert.Equal(2, claude.RawFindingCount);
        Assert.Equal(2, openai.RawFindingCount);
        Assert.Equal(2, claude.ConsolidatedFindingCount);
        Assert.Equal(2, openai.ConsolidatedFindingCount);
        Assert.Equal(1, claude.MultiProviderFindingCount);
        Assert.Equal(1, claude.ExclusiveFindingCount);
        Assert.Single(claude.ExclusiveFindingIds);
        Assert.Single(openai.ExclusiveFindingIds);
        Assert.NotEqual(claude.ExclusiveFindingIds[0], openai.ExclusiveFindingIds[0]);
    }

    [Fact]
    public void Similar_but_unreconciled_findings_remain_exclusive()
    {
        // Identical titles but different rules AND different files — the reconciler's
        // stages all fail → keep-separate. The comparison must not invent agreement.
        var raw = new[]
        {
            Raw("c1", "Potential denial of service", "CWE-400", "src/A.cs", "Claude", line: 1),
            Raw("o1", "Potential denial of service", "CWE-770", "src/B.cs", "OpenAI", line: 1)
        };
        var run = Run(
            [Rec("Claude", FindingCategory.Security), Rec("OpenAI", FindingCategory.Security)],
            rawFindings: raw, findings: Reconcile(raw));

        var discipline = ProviderComparisonBuilder.Build(run)!.DisciplineComparisons[0];
        var agreement = discipline.Agreement!;

        Assert.Equal(2, agreement.ConsolidatedFindingCount);
        Assert.Equal(0, agreement.SharedFindingCount);
        Assert.Equal(0.0, agreement.AgreementRate!.Value, 3);

        var claude = Assert.Single(discipline.FindingMetrics, f => f.Provider == "Claude");
        var openai = Assert.Single(discipline.FindingMetrics, f => f.Provider == "OpenAI");
        Assert.Equal(1, claude.ExclusiveFindingCount);
        Assert.Equal(1, openai.ExclusiveFindingCount);
        Assert.Single(claude.ExclusiveFindingIds);
        Assert.Single(openai.ExclusiveFindingIds);
        Assert.NotEqual(claude.ExclusiveFindingIds[0], openai.ExclusiveFindingIds[0]);
    }

    [Fact]
    public void Agreement_rate_is_null_when_there_are_no_consolidated_findings()
    {
        var run = Run(
            [Rec("Claude", FindingCategory.Security), Rec("OpenAI", FindingCategory.Security)]);

        var agreement = ProviderComparisonBuilder.Build(run)!.DisciplineComparisons[0].Agreement!;

        Assert.Equal(0, agreement.ConsolidatedFindingCount);
        Assert.Equal(0, agreement.SharedFindingCount);
        Assert.Null(agreement.AgreementRate);
    }

    [Fact]
    public void Agreement_is_not_computed_when_contexts_differ_even_if_findings_exist()
    {
        var raw = new[]
        {
            Raw("c1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "Claude", line: 3),
            Raw("o1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "OpenAI", line: 4)
        };
        var run = Run(
            [Rec("Claude", FindingCategory.Security, "CTX-aaaa"), Rec("OpenAI", FindingCategory.Security, "CTX-bbbb")],
            rawFindings: raw, findings: Reconcile(raw));

        var discipline = ProviderComparisonBuilder.Build(run)!.DisciplineComparisons[0];

        Assert.Equal(ProviderComparisonStatus.NonComparable, discipline.Status);
        Assert.Null(discipline.Agreement);
        Assert.NotEmpty(discipline.FindingMetrics); // output present, agreement withheld
    }

    // ── Determinism & artifact safety ─────────────────────────────────────────

    [Fact]
    public void The_comparison_is_deterministic_regardless_of_enumeration_order()
    {
        var records = new[]
        {
            Rec("OpenAI", FindingCategory.Security, contextFiles: 2),
            Rec("Claude", FindingCategory.Security, contextFiles: 2),
            Rec("OpenAI", FindingCategory.Reliability),
            Rec("Claude", FindingCategory.Reliability)
        };
        var observations = new[]
        {
            Obs("o1", "OpenAI", FindingCategory.Security, type: "HardcodedSecret", files: "src/Payments.cs"),
            Obs("o2", "Claude", FindingCategory.Security, type: "HardcodedSecret", files: "src/Payments.cs")
        };
        var shuffled = observations.Reverse().ToArray();

        var runA = Run(records, observations);
        var runB = Run(records.Reverse().ToArray(), shuffled);

        var jsonA = Serialize(ProviderComparisonBuilder.Build(runA)!);
        var jsonB = Serialize(ProviderComparisonBuilder.Build(runB)!);

        Assert.Equal(jsonA, jsonB);
    }

    [Fact]
    public void Comparison_json_is_clean_json_with_no_provider_sdk_types_or_secrets()
    {
        var run = Run(
            [Rec("Claude", FindingCategory.Security, errorMessage: KeyValue + " trace"), Rec("OpenAI", FindingCategory.Security)],
            observations: [Obs("o1", "Claude", FindingCategory.Security, type: "HardcodedSecret")],
            rawFindings: [Raw("c1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "Claude")],
            findings: [Consolidated("F-1", "Hardcoded API key", ["Claude"])]);
        run = run with { ProviderComparison = ProviderComparisonBuilder.Build(run) };

        var json = new JsonReportGenerator().RenderProviderComparison(run);

        Assert.Contains("\"disciplineComparisons\"", json);
        Assert.Contains("\"comparedProviders\"", json);
        Assert.DoesNotContain("\"$type\"", json);
        Assert.DoesNotContain("OpenAI.Chat", json);
        Assert.DoesNotContain("Anthropic.SDK", json);
        Assert.DoesNotContain(KeyValue, json);
        Assert.DoesNotContain(KeyVariable, json);
        Assert.DoesNotContain("\"errorMessage\"", json); // raw error text is never surfaced
    }

    [Fact]
    public void The_external_package_contract_never_carries_provider_comparison()
    {
        var run = Run(
            [Rec("Claude", FindingCategory.Security), Rec("OpenAI", FindingCategory.Security)]);

        var package = new EngineeringReviewPackageBuilder().Build(run);
        var packageJson = JsonSerializer.Serialize(package, CouncilJson.Options);

        Assert.DoesNotContain("providerComparison", packageJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("agreementRate", packageJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sharedFindingCount", packageJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_single_provider_review_writes_no_provider_comparison_artifacts()
    {
        var client = new ScriptedLlmClient("claude")
            .Returns(SecurityJson("HardcodedSecret", "CWE-798", "src/Payments.cs"))
            .Returns(ReliabilityJson());
        var pipeline = BuildPipelineAsync([Claude(client)]);

        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = _repo, ProviderName = "Claude" });

        Assert.False(File.Exists(Path.Combine(result.OutputDirectory, "provider-comparison.json")));
        Assert.False(File.Exists(Path.Combine(result.OutputDirectory, "provider-comparison.md")));

        var packageJson = File.ReadAllText(Path.Combine(result.OutputDirectory, "engineering-review-package.json"));
        var dto = JsonSerializer.Deserialize<PackageContract>(packageJson, CouncilJson.Options)!;
        Assert.Equal("1.1", dto.SchemaVersion);
    }

    // ── End-to-end: real pipeline, two providers ──────────────────────────────

    [Fact]
    public async Task End_to_end_dual_provider_run_produces_comparable_artifacts_and_a_green_package()
    {
        var claudeClient = new ScriptedLlmClient("claude")
            .Returns(SecurityJson("HardcodedSecret", "CWE-798", "src/Payments.cs"))
            .Returns(ReliabilityJson());
        var openaiClient = new ScriptedLlmClient("openai")
            .Returns(SecurityJson("HardcodedSecret", "CWE-798", "src/Payments.cs"))
            .Returns(ReliabilityJson());

        var pipeline = BuildPipelineAsync([Claude(claudeClient), OpenAi(openaiClient)]);
        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = _repo, ProviderName = "Claude,OpenAI" });

        var comparisonPath = Path.Combine(result.OutputDirectory, "provider-comparison.json");
        Assert.True(File.Exists(comparisonPath), "provider-comparison.json should be written for a dual-provider run.");

        var json = File.ReadAllText(comparisonPath);
        Assert.DoesNotContain(KeyValue, json);
        var report = JsonSerializer.Deserialize<ProviderComparisonReport>(json, CouncilJson.Options)!;

        Assert.Equal(result.Run.RunId, report.RunId);
        Assert.Equal(["Claude", "OpenAI"], report.ComparedProviders);
        Assert.Equal(2, report.DisciplineComparisons.Count);
        Assert.All(report.DisciplineComparisons, d => Assert.Equal(ProviderComparisonStatus.Comparable, d.Status));

        var security = report.DisciplineComparisons.Single(d => d.Discipline == FindingCategory.Security);
        Assert.NotNull(security.Agreement);
        Assert.Equal(1, security.Agreement!.SharedFindingCount); // both providers agree on the hardcoded secret

        // The external consumer contract is unchanged: still 1.1, no comparison leak.
        var packageJson = File.ReadAllText(Path.Combine(result.OutputDirectory, "engineering-review-package.json"));
        var dto = JsonSerializer.Deserialize<PackageContract>(packageJson, CouncilJson.Options)!;
        Assert.Equal("1.1", dto.SchemaVersion);
        Assert.DoesNotContain("providerComparison", packageJson, StringComparison.OrdinalIgnoreCase);

        // run.json round-trips the comparison (additive to the reload artifact).
        var runJson = File.ReadAllText(Path.Combine(result.OutputDirectory, "run.json"));
        Assert.Contains("\"providerComparison\"", runJson);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ScannedFile Src(string path, string content) => new()
    {
        RelativePath = path,
        Extension = ".cs",
        SizeBytes = content.Length,
        LineCount = 1,
        Content = content
    };

    private static AnalysisContextSelection Sel(params ScannedFile[] files) => new()
    {
        Strategy = "test",
        Files = files,
        TotalRepositoryFiles = files.Length,
        SelectedFileCount = files.Length,
        EstimatedContentSize = files.Sum(f => (f.Content ?? string.Empty).Length)
    };

    private static ProviderExecutionRecord Rec(
        string provider,
        FindingCategory? discipline = FindingCategory.Security,
        string fingerprint = "CTX-abc123",
        bool success = true,
        int? input = null,
        int? output = null,
        int? total = null,
        int retries = 0,
        int repairs = 0,
        bool? repairSucceeded = null,
        string? errorCategory = null,
        string? errorMessage = null,
        string model = "test-model",
        int contextFiles = 1,
        int contextChars = 100,
        int observations = 0,
        EvidenceProviderType type = EvidenceProviderType.LLM)
        => new()
        {
            RunId = "run-test",
            ProviderName = provider,
            ProviderId = provider,
            ProviderVersion = model,
            ProviderType = type,
            RequestedDiscipline = discipline,
            Success = success,
            InputTokens = input,
            OutputTokens = output,
            TokensUsed = total,
            RetryCount = retries,
            RepairAttemptCount = repairs,
            RepairSucceeded = repairSucceeded,
            ErrorCategory = errorCategory,
            ErrorMessage = errorMessage,
            ContextFileCount = contextFiles,
            ContextCharacterCount = contextChars,
            ContextFingerprint = fingerprint,
            ObservationsProduced = observations,
            Duration = TimeSpan.FromMilliseconds(100)
        };

    private static EngineeringObservation Obs(
        string id, string provider, FindingCategory discipline,
        string type = "GeneralObservation",
        FindingSeverity severity = FindingSeverity.Medium,
        FindingConfidence confidence = FindingConfidence.Medium,
        params string[] files)
        => new()
        {
            Id = id,
            SourceProvider = provider,
            SourceProviderType = EvidenceProviderType.LLM,
            RequestedDiscipline = discipline,
            Discipline = discipline,
            ObservationType = type,
            Severity = severity,
            Confidence = confidence,
            Title = "observation " + id,
            FileReferences = files.Select(f => new FileReference { Path = f, StartLine = 1 }).ToList()
        };

    private static Finding Raw(
        string id, string title, string rule, string file, string provider,
        FindingCategory category = FindingCategory.Security, int line = 1)
        => new()
        {
            Id = id,
            Title = title,
            Category = category,
            Severity = FindingSeverity.High,
            Confidence = FindingConfidence.High,
            EvidenceProvider = provider,
            SupportingProviders = [provider],
            SourceRules = [rule],
            FileReferences = [new FileReference { Path = file, StartLine = line }],
            Evidence = "evidence for " + id,
            Status = FindingStatus.New
        };

    private static Finding Consolidated(string id, string title, IReadOnlyList<string> providers)
        => new()
        {
            Id = id,
            Title = title,
            Category = FindingCategory.Security,
            Severity = FindingSeverity.High,
            Confidence = FindingConfidence.High,
            EvidenceProvider = providers[0],
            SupportingProviders = providers,
            AgreementCount = providers.Count,
            Status = FindingStatus.Merged
        };

    private static IReadOnlyList<Finding> Reconcile(params Finding[] raw)
        => new RuleBasedFindingReconciler().Reconcile(raw, []).ConsolidatedFindings;

    private static AnalysisRun Run(
        IReadOnlyList<ProviderExecutionRecord> records,
        IReadOnlyList<EngineeringObservation>? observations = null,
        IReadOnlyList<Finding>? rawFindings = null,
        IReadOnlyList<Finding>? findings = null)
        => new()
        {
            RunId = "run-test",
            TargetPath = "/repo",
            SolutionName = "Repo",
            Branch = "main",
            Commit = "abc123",
            ProviderExecution = new ProviderExecutionReport { Records = records },
            Observations = observations ?? [],
            RawFindings = rawFindings ?? [],
            Findings = findings ?? []
        };

    private static string Serialize(ProviderComparisonReport report)
        => JsonSerializer.Serialize(report with { GeneratedAt = DateTimeOffset.MinValue }, CouncilJson.Options);

    private static string SecurityJson(string type, string ruleId, string filePath) => $$"""
        {
          "schemaVersion": "1.0",
          "discipline": "Security",
          "observations": [
            { "type": "{{type}}", "discipline": "Security", "title": "Hardcoded secret",
              "description": "A credential is embedded in source.", "severity": "High", "confidence": "High",
              "ruleId": "{{ruleId}}",
              "fileReferences": [ { "path": "{{filePath}}", "startLine": 3 } ] }
          ]
        }
        """;

    private static string ReliabilityJson() => """
        {
          "schemaVersion": "1.0",
          "discipline": "Reliability",
          "observations": [
            { "type": "MissingCancellation", "discipline": "Reliability", "title": "Missing cancellation token",
              "description": "A long-running operation does not accept a cancellation token.",
              "severity": "Medium", "confidence": "Medium", "ruleId": "CWE-835" }
          ]
        }
        """;

    private ClaudeEvidenceProvider Claude(ScriptedLlmClient client, int maxRetries = 0)
    {
        Environment.SetEnvironmentVariable(KeyVariable, KeyValue);
        return new ClaudeEvidenceProvider(client, new ClaudeProviderOptions
        {
            Enabled = true, ApiKeyEnvironmentVariable = KeyVariable, Model = "claude-test-model",
            MaxRetries = maxRetries, TimeoutSeconds = 5, EnableStructuredRepair = false
        }, new DisciplineEvidencePromptBuilder());
    }

    private OpenAiEvidenceProvider OpenAi(ScriptedLlmClient client, int maxRetries = 0)
    {
        Environment.SetEnvironmentVariable(KeyVariable, KeyValue);
        return new OpenAiEvidenceProvider(client, new OpenAiProviderOptions
        {
            Enabled = true, ApiKeyEnvironmentVariable = KeyVariable, Model = "openai-test-model",
            MaxRetries = maxRetries, TimeoutSeconds = 5, EnableStructuredRepair = false
        }, new DisciplineEvidencePromptBuilder());
    }

    private AnalysisPipeline BuildPipelineAsync(IReadOnlyList<IEvidenceProvider> providers)
    {
        var evidenceOptions = new EvidenceOptions
        {
            Providers = providers.Select(p => p.Metadata.Name).ToList(),
            Disciplines = [FindingCategory.Security, FindingCategory.Reliability],
            ProviderFailureMode = ProviderFailureMode.Continue
        };
        var factory = new EvidenceProviderFactory(providers);

        return new AnalysisPipeline(
            new FileSystemRepositoryScanner(), factory,
            new EvidenceAcquisitionPlanner(),
            new EvidenceAcquisitionExecutor(factory, new RuleBasedAnalysisContextSelector(), evidenceOptions),
            new EvidenceInterpretationPipeline(new EvidenceInterpreterResolver(
                [new StructuredLlmEvidenceInterpreter()])),
            new AnalysisOrchestrator([new SecurityAnalyzer(), new ReliabilityAnalyzer()]),
            new RuleBasedFindingReconciler(),
            new RuleBasedCouncilSummaryGenerator(),
            new EngineeringReviewPackageBuilder(),
            evidenceOptions,
            new FileSystemAnalysisRunRepository(
                new FileSystemRunRepositoryOptions { OutputsRoot = _outputs },
                new EngineeringReviewMarkdownExporter(), new JsonReportGenerator()));
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _repo, _outputs })
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }
}
