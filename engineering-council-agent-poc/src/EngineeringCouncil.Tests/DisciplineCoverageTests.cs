using System.Text.Json;
using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Core.Serialization;
using EngineeringCouncil.Infrastructure.Reporting;
using EngineeringCouncil.Tests.Contracts;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 015.3A — Evidence Coverage Gates. Deterministic per-discipline
/// coverage computed from existing run facts: CoveredWithFindings /
/// CoveredNoFindings / NoEvidence + successful/attempted provider counts.
/// The defining invariant: a discipline with ZERO successful evidence acquisitions
/// (timeouts/failures do NOT count) can never be reported as "no issues". No
/// LLM, no providers, no network — pure offline facts + the Markdown/package
/// projections. The M15.2 real defect shape (Architecture 0/3, Testing 0/3) is
/// regression-tested.
/// </summary>
public sealed class DisciplineCoverageTests
{
    private static readonly FindingCategory[] M15_2_Disciplines =
    [
        FindingCategory.Architecture, FindingCategory.CodeQuality, FindingCategory.Reliability,
        FindingCategory.Security, FindingCategory.Testing, FindingCategory.Documentation,
        FindingCategory.Observability
    ];

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static ProviderExecutionRecord Rec(string provider, FindingCategory disc, bool success, string? errorCategory = null)
        => new()
        {
            ProviderName = provider,
            RequestedDiscipline = disc,
            Scope = EvidenceAcquisitionScope.Discipline,
            Success = success,
            ErrorCategory = success ? null : (errorCategory ?? "Failure"),
            EvidenceCount = success ? 1 : 0
        };

    private static Finding F(FindingCategory category) => new()
    {
        Id = Guid.NewGuid().ToString("N")[..6],
        Title = $"{category} issue",
        Category = category,
        Severity = FindingSeverity.Medium,
        Confidence = FindingConfidence.Medium,
        SourceAgent = $"{category}".ToLowerInvariant() + "-analyzer",
        SourceAgents = [$"{category}".ToLowerInvariant() + "-analyzer"],
        EvidenceProvider = "Mock"
    };

    /// <summary>The exact M15.2 real-world failure shape (run 20260813-230240-b3e36e,
    /// RedirectToService, 3 providers × 7 disciplines): Architecture and Testing are
    /// 0/3 (all Timeout), Security is 2/3, everything else 3/3 or 2/3.</summary>
    private static List<ProviderExecutionRecord> M15_2Records() =>
    [
        Rec("OpenCode", FindingCategory.Architecture, false, "Timeout"),
        Rec("OpenCode", FindingCategory.CodeQuality, true),
        Rec("OpenCode", FindingCategory.Reliability, true),
        Rec("OpenCode", FindingCategory.Security, true),
        Rec("OpenCode", FindingCategory.Testing, false, "Timeout"),
        Rec("OpenCode", FindingCategory.Documentation, true),
        Rec("OpenCode", FindingCategory.Observability, true),

        Rec("Codex", FindingCategory.Architecture, false, "Timeout"),
        Rec("Codex", FindingCategory.CodeQuality, true),
        Rec("Codex", FindingCategory.Reliability, true),
        Rec("Codex", FindingCategory.Security, true),
        Rec("Codex", FindingCategory.Testing, false, "Timeout"),
        Rec("Codex", FindingCategory.Documentation, true),
        Rec("Codex", FindingCategory.Observability, false, "Timeout"),

        Rec("ClaudeCode", FindingCategory.Architecture, false, "Timeout"),
        Rec("ClaudeCode", FindingCategory.CodeQuality, false, "Timeout"),
        Rec("ClaudeCode", FindingCategory.Reliability, true),
        Rec("ClaudeCode", FindingCategory.Security, false, "Timeout"),
        Rec("ClaudeCode", FindingCategory.Testing, false, "Timeout"),
        Rec("ClaudeCode", FindingCategory.Documentation, true),
        Rec("ClaudeCode", FindingCategory.Observability, false, "Timeout"),
    ];

    private static IReadOnlyList<Finding> M15_2Findings() =>
    [
        F(FindingCategory.Security), F(FindingCategory.CodeQuality), F(FindingCategory.Reliability),
        F(FindingCategory.Documentation), F(FindingCategory.Observability)
    ];

    private static AnalysisRun Run(
        IReadOnlyList<Finding> findings,
        IReadOnlyList<ProviderExecutionRecord> records,
        IReadOnlyList<FindingCategory>? requested = null)
        => new()
        {
            RunId = "run-m15-3a",
            TargetPath = "/repo",
            SolutionName = "Repo",
            Branch = "main",
            Commit = "abc",
            Projects = 3,
            FilesScanned = 42,
            Status = AnalysisRunStatus.Completed,
            RequestedDisciplines = requested ?? M15_2_Disciplines,
            Findings = findings,
            RawFindings = findings,
            ProviderExecution = ProviderExecutionReport.FromRecords(records)
        };

    // ── Calculation semantics ────────────────────────────────────────────────

    [Fact]
    public void Successful_evidence_with_findings_is_CoveredWithFindings()
    {
        var coverage = DisciplineCoverage.From(
            ProviderExecutionReport.FromRecords(
            [
                Rec("A", FindingCategory.Security, true), Rec("B", FindingCategory.Security, true),
            ]),
            [F(FindingCategory.Security)], [FindingCategory.Security]);

        var entry = Assert.Single(coverage.Entries);
        Assert.Equal(DisciplineCoverageStatus.CoveredWithFindings, entry.Status);
        Assert.Equal(2, entry.SuccessfulProviders);
        Assert.Equal(2, entry.AttemptedProviders);
    }

    [Fact]
    public void Successful_evidence_without_findings_is_CoveredNoFindings()
    {
        var coverage = DisciplineCoverage.From(
            ProviderExecutionReport.FromRecords(
            [
                Rec("A", FindingCategory.Documentation, true), Rec("B", FindingCategory.Documentation, true),
            ]),
            [], [FindingCategory.Documentation]);

        var entry = Assert.Single(coverage.Entries);
        Assert.Equal(DisciplineCoverageStatus.CoveredNoFindings, entry.Status);
        Assert.Equal(2, entry.SuccessfulProviders);
    }

    [Fact]
    public void Zero_successful_evidence_is_NoEvidence()
    {
        var coverage = DisciplineCoverage.From(
            ProviderExecutionReport.FromRecords(
            [
                Rec("A", FindingCategory.Architecture, false), Rec("B", FindingCategory.Architecture, false),
            ]),
            [F(FindingCategory.Architecture)], [FindingCategory.Architecture]);

        var entry = Assert.Single(coverage.Entries);
        Assert.Equal(DisciplineCoverageStatus.NoEvidence, entry.Status);
        Assert.Equal(0, entry.SuccessfulProviders);
        Assert.Equal(2, entry.AttemptedProviders);
    }

    [Fact]
    public void Timeout_does_not_count_as_evidence()
    {
        var coverage = DisciplineCoverage.From(
            ProviderExecutionReport.FromRecords(
            [
                Rec("A", FindingCategory.Testing, false, "Timeout"),
                Rec("B", FindingCategory.Testing, false, "Timeout"),
                Rec("C", FindingCategory.Testing, false, "Timeout"),
            ]),
            [], [FindingCategory.Testing]);

        var entry = Assert.Single(coverage.Entries);
        Assert.Equal(DisciplineCoverageStatus.NoEvidence, entry.Status);
        Assert.Equal(0, entry.SuccessfulProviders);
    }

    [Fact]
    public void Failure_does_not_count_as_evidence()
    {
        var coverage = DisciplineCoverage.From(
            ProviderExecutionReport.FromRecords(
            [
                Rec("A", FindingCategory.Reliability, false, "SchemaValidation"),
            ]),
            [], [FindingCategory.Reliability]);

        Assert.Equal(DisciplineCoverageStatus.NoEvidence, Assert.Single(coverage.Entries).Status);
    }

    [Fact]
    public void Partial_success_counts_as_evidence()
    {
        var coverage = DisciplineCoverage.From(
            ProviderExecutionReport.FromRecords(
            [
                Rec("OpenCode", FindingCategory.Security, true),
                Rec("Codex", FindingCategory.Security, true),
                Rec("ClaudeCode", FindingCategory.Security, false, "Timeout"),
            ]),
            [F(FindingCategory.Security)], [FindingCategory.Security]);

        var entry = Assert.Single(coverage.Entries);
        Assert.Equal(DisciplineCoverageStatus.CoveredWithFindings, entry.Status);
        Assert.Equal(2, entry.SuccessfulProviders);
        Assert.Equal(3, entry.AttemptedProviders);
    }

    [Fact]
    public void Provider_counts_are_successful_over_attempted()
    {
        var coverage = DisciplineCoverage.From(
            ProviderExecutionReport.FromRecords(
            [
                Rec("OpenCode", FindingCategory.Security, true),
                Rec("Codex", FindingCategory.Security, false, "Timeout"),
                Rec("ClaudeCode", FindingCategory.Security, false, "Timeout"),
            ]),
            [F(FindingCategory.Security)], [FindingCategory.Security]);

        var entry = Assert.Single(coverage.Entries);
        Assert.Equal(1, entry.SuccessfulProviders);
        Assert.Equal(3, entry.AttemptedProviders);
    }

    [Fact]
    public void Calculation_is_order_independent()
    {
        var records = M15_2Records();
        var requested = new[] { FindingCategory.Testing, FindingCategory.Security, FindingCategory.Architecture };
        var findings = M15_2Findings();

        var forward = DisciplineCoverage.From(ProviderExecutionReport.FromRecords(records), findings, requested);
        var reverse = DisciplineCoverage.From(
            ProviderExecutionReport.FromRecords(records.AsEnumerable().Reverse().ToList()),
            findings, requested.Reverse().ToArray());

        Assert.Equal(forward.Entries, reverse.Entries);
        // Entries are emitted in deterministic enum order regardless of input order.
        Assert.Equal(
            requested.OrderBy(d => d).Select(d => d.ToString()).ToList(),
            forward.Entries.Select(e => e.Discipline).ToList());
    }

    // ── M15.2 real-world regression (deterministic fixture) ──────────────────

    [Fact]
    public void M15_2_architecture_regression_is_NoEvidence()
    {
        var coverage = DisciplineCoverage.From(
            ProviderExecutionReport.FromRecords(M15_2Records()), M15_2Findings(), M15_2_Disciplines);

        var arch = Assert.Single(coverage.Entries, e => e.Discipline == "Architecture");
        Assert.Equal(DisciplineCoverageStatus.NoEvidence, arch.Status);
        Assert.Equal(0, arch.SuccessfulProviders);
        Assert.Equal(3, arch.AttemptedProviders);
    }

    [Fact]
    public void M15_2_testing_regression_is_NoEvidence()
    {
        var coverage = DisciplineCoverage.From(
            ProviderExecutionReport.FromRecords(M15_2Records()), M15_2Findings(), M15_2_Disciplines);

        var testing = Assert.Single(coverage.Entries, e => e.Discipline == "Testing");
        Assert.Equal(DisciplineCoverageStatus.NoEvidence, testing.Status);
        Assert.Equal(0, testing.SuccessfulProviders);
        Assert.Equal(3, testing.AttemptedProviders);
    }

    [Fact]
    public void M15_2_security_partial_coverage_remains_valid_evidence()
    {
        var coverage = DisciplineCoverage.From(
            ProviderExecutionReport.FromRecords(M15_2Records()), M15_2Findings(), M15_2_Disciplines);

        var security = Assert.Single(coverage.Entries, e => e.Discipline == "Security");
        Assert.Equal(DisciplineCoverageStatus.CoveredWithFindings, security.Status);
        Assert.Equal(2, security.SuccessfulProviders);
        Assert.Equal(3, security.AttemptedProviders);
    }

    [Fact]
    public void M15_2_control_discipline_with_evidence_and_no_findings_is_CoveredNoFindings()
    {
        // Documentation: evidence acquired (3/3) but zero consolidated findings.
        var coverage = DisciplineCoverage.From(
            ProviderExecutionReport.FromRecords(
            [
                Rec("A", FindingCategory.Documentation, true),
                Rec("B", FindingCategory.Documentation, true),
                Rec("C", FindingCategory.Documentation, true),
            ]),
            [], [FindingCategory.Documentation]);

        var entry = Assert.Single(coverage.Entries);
        Assert.Equal(DisciplineCoverageStatus.CoveredNoFindings, entry.Status);
        Assert.Equal(3, entry.SuccessfulProviders);
    }

    // ── Markdown behavior ────────────────────────────────────────────────────

    [Fact]
    public void Markdown_never_says_no_issues_for_a_NoEvidence_discipline()
    {
        var package = new EngineeringReviewPackageBuilder().Build(Run(M15_2Findings(), M15_2Records()));
        var md = new EngineeringReviewMarkdownExporter().Export(package);

        Assert.DoesNotContain("No Architecture issues identified.", md);
        Assert.DoesNotContain("No Testing issues identified.", md);
        Assert.DoesNotContain("_No findings were produced for this run._", md);
    }

    [Fact]
    public void Markdown_clearly_warns_absence_of_evidence_is_not_assurance()
    {
        var package = new EngineeringReviewPackageBuilder().Build(Run(M15_2Findings(), M15_2Records()));
        var md = new EngineeringReviewMarkdownExporter().Export(package);

        // Per-discipline NoEvidence prose.
        Assert.Contains("No successful evidence was acquired for this discipline; absence of findings must not be interpreted as assurance.", md);
        // Executive-summary coverage limitation.
        Assert.Contains("absence of findings there must not be interpreted as assurance", md);
        // Health/risk caveat.
        Assert.Contains("**Coverage limitation:**", md);
        Assert.Contains("Architecture", md);
        Assert.Contains("Testing", md);
    }

    [Fact]
    public void Markdown_keeps_CoveredNoFindings_wording_distinct_from_NoEvidence()
    {
        // A discipline with evidence and no findings ONLY.
        IReadOnlyList<ProviderExecutionRecord> records =
        [
            Rec("OpenCode", FindingCategory.Documentation, true),
            Rec("Codex", FindingCategory.Documentation, true),
        ];
        var package = new EngineeringReviewPackageBuilder().Build(
            Run([], records, requested: [FindingCategory.Documentation]));
        var md = new EngineeringReviewMarkdownExporter().Export(package);

        Assert.Contains("No findings were identified from the available evidence.", md);
        Assert.DoesNotContain("No successful evidence was acquired for this discipline", md);
        Assert.Contains("Evidence coverage: 2/2 providers", md);

        // And with the full M15.2 shape BOTH distinct wordings coexist.
        var full = new EngineeringReviewPackageBuilder().Build(Run(M15_2Findings(), M15_2Records()));
        var fullMd = new EngineeringReviewMarkdownExporter().Export(full);
        Assert.Contains("No successful evidence was acquired for this discipline; absence of findings must not be interpreted as assurance.", fullMd);
        Assert.Contains("Evidence coverage: 2/3 providers", fullMd);  // Security partial coverage visible
    }

    [Fact]
    public void Markdown_shows_evidence_coverage_counts()
    {
        var package = new EngineeringReviewPackageBuilder().Build(Run(M15_2Findings(), M15_2Records()));
        var md = new EngineeringReviewMarkdownExporter().Export(package);

        Assert.Contains("Evidence coverage: 0/3 providers", md);  // Architecture
        Assert.Contains("Evidence coverage: 2/3 providers", md);  // Security
    }

    // ── Package / consumer contract ──────────────────────────────────────────

    [Fact]
    public void Package_serializes_discipline_coverage_and_consumer_dto_round_trips()
    {
        var package = new EngineeringReviewPackageBuilder().Build(Run(M15_2Findings(), M15_2Records()));
        var json = JsonSerializer.Serialize(package, CouncilJson.Options);

        Assert.Equal("1.1", package.SchemaVersion); // additive — unchanged
        Assert.Contains("\"disciplineCoverage\"", json);

        var dto = JsonSerializer.Deserialize<PackageContract>(json, CouncilJson.Options)!;
        Assert.Equal("1.1", dto.SchemaVersion);
        Assert.NotNull(dto.DisciplineCoverage);

        var arch = Assert.Single(dto.DisciplineCoverage!.Entries, e => e.Discipline == "Architecture");
        Assert.Equal("noEvidence", arch.Status);
        Assert.Equal(0, arch.SuccessfulProviders);
        Assert.Equal(3, arch.AttemptedProviders);

        var security = Assert.Single(dto.DisciplineCoverage.Entries, e => e.Discipline == "Security");
        Assert.Equal("coveredWithFindings", security.Status);
        Assert.Equal(2, security.SuccessfulProviders);
        Assert.Equal(3, security.AttemptedProviders);
    }

    [Fact]
    public void Package_has_no_discipline_coverage_when_nothing_requested()
    {
        var package = new EngineeringReviewPackageBuilder().Build(Run([], [], requested: []));
        var json = JsonSerializer.Serialize(package, CouncilJson.Options);
        Assert.DoesNotContain("\"disciplineCoverage\"", json);
    }

    // ── Finding / reconciliation behavior unchanged ──────────────────────────

    [Fact]
    public void Findings_and_reconciliation_are_unchanged_by_coverage_gates()
    {
        var run = Run(M15_2Findings(), M15_2Records());
        var package = new EngineeringReviewPackageBuilder().Build(run);

        // Findings pass through untouched (same set, same order semantics).
        Assert.Equal(run.Findings.Count, package.Findings.Count);
        Assert.Equal(run.Findings.Select(f => f.Id).OrderBy(x => x), package.Findings.Select(f => f.Id).OrderBy(x => x));
        Assert.Equal(run.RawFindings.Count, package.Appendix.RawFindings.Count);

        // Reconciliation not altered by coverage computation.
        Assert.Null(package.Reconciliation); // no reconciliation recorded in this fixture

        // The misleading strength is gone while the executive summary survives.
        Assert.DoesNotContain(package.KeyStrengths, s => s.Contains("Architecture", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(package.KeyStrengths, s => s.Contains("Testing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Coverage limitation", package.ExecutiveSummary);
    }

    // ── Real persisted M15.2 artifacts (best-effort, offline) ─────────────────

    [Fact]
    public void Persisted_M15_2_run_reproduces_the_NoEvidence_defect_shape()
    {
        var dir = Path.Combine(FindRepoRoot(), "outputs", "20260813-230240-b3e36e");
        if (!Directory.Exists(dir)) return; // artifacts not persisted → deterministic fixture covers this

        var report = JsonSerializer.Deserialize<ProviderExecutionReport>(
            File.ReadAllText(Path.Combine(dir, "provider-execution.json")), CouncilJson.Options)!;
        var package = JsonSerializer.Deserialize<EngineeringReviewPackage>(
            File.ReadAllText(Path.Combine(dir, "engineering-review-package.json")), CouncilJson.Options)!;
        var requested = package.AcquisitionCoverage.DisciplinesRequested
            .Select(s => Enum.Parse<FindingCategory>(s)).ToList();

        var coverage = DisciplineCoverage.From(report, package.Findings, requested);

        var arch = Assert.Single(coverage.Entries, e => e.Discipline == "Architecture");
        Assert.Equal(DisciplineCoverageStatus.NoEvidence, arch.Status);
        Assert.Equal(0, arch.SuccessfulProviders);
        Assert.Equal(3, arch.AttemptedProviders);

        var testing = Assert.Single(coverage.Entries, e => e.Discipline == "Testing");
        Assert.Equal(DisciplineCoverageStatus.NoEvidence, testing.Status);
        Assert.Equal(0, testing.SuccessfulProviders);
        Assert.Equal(3, testing.AttemptedProviders);

        var security = Assert.Single(coverage.Entries, e => e.Discipline == "Security");
        Assert.NotEqual(DisciplineCoverageStatus.NoEvidence, security.Status);
        Assert.Equal(2, security.SuccessfulProviders);
        Assert.Equal(3, security.AttemptedProviders);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EngineeringCouncil.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}