using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Reporting;
using Xunit;

namespace EngineeringCouncil.Tests;

public sealed class EngineeringReviewPackageTests
{
    private static Finding F(FindingSeverity severity, FindingCategory category = FindingCategory.CodeQuality,
        FindingConfidence confidence = FindingConfidence.Medium, FindingStatus status = FindingStatus.New) => new()
    {
        Id = Guid.NewGuid().ToString("N")[..6],
        Title = $"{category} {severity} issue",
        Category = category,
        Severity = severity,
        Confidence = confidence,
        Status = status,
        SourceAgent = $"{category}".ToLowerInvariant() + "-analyzer",
        SourceAgents = [$"{category}".ToLowerInvariant() + "-analyzer"],
        EvidenceProvider = "Mock"
    };

    private static AnalysisRun RunWith(IReadOnlyList<Finding> findings, ProviderExecutionReport? pe = null) => new()
    {
        RunId = "20260707-000000-abc123",
        TargetPath = "/repo",
        SolutionName = "Repo",
        Branch = "main",
        Commit = "abc123def456",
        Projects = 3,
        FilesScanned = 42,
        RawFindings = findings,
        Findings = findings,
        ProviderExecution = pe,
        StartedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
        CompletedAt = DateTimeOffset.UtcNow,
        Status = AnalysisRunStatus.Completed
    };

    // ── Health scoring ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(FindingSeverity.Critical, EngineeringHealth.Critical)]
    [InlineData(FindingSeverity.High, EngineeringHealth.NeedsAttention)]
    [InlineData(FindingSeverity.Medium, EngineeringHealth.Fair)]
    [InlineData(FindingSeverity.Low, EngineeringHealth.Good)]
    [InlineData(FindingSeverity.Info, EngineeringHealth.Excellent)]
    public void Health_scoring_follows_documented_rules(FindingSeverity severity, EngineeringHealth expected)
    {
        var metrics = EngineeringMetrics.From(RunWith([F(severity)]));
        Assert.Equal(expected, HealthRiskScorer.ScoreHealth(metrics));
    }

    [Fact]
    public void Health_is_excellent_with_no_findings()
        => Assert.Equal(EngineeringHealth.Excellent,
            HealthRiskScorer.ScoreHealth(EngineeringMetrics.From(RunWith([]))));

    // ── Risk scoring ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(FindingSeverity.Critical, EngineeringRisk.Critical)]
    [InlineData(FindingSeverity.High, EngineeringRisk.High)]
    [InlineData(FindingSeverity.Medium, EngineeringRisk.Moderate)]
    [InlineData(FindingSeverity.Low, EngineeringRisk.Low)]
    public void Risk_scoring_follows_documented_rules(FindingSeverity severity, EngineeringRisk expected)
    {
        var metrics = EngineeringMetrics.From(RunWith([F(severity)]));
        Assert.Equal(expected, HealthRiskScorer.ScoreRisk(metrics));
    }

    // ── Metrics ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Metrics_count_findings_by_severity_and_discipline()
    {
        var findings = new[]
        {
            F(FindingSeverity.Critical, FindingCategory.Security),
            F(FindingSeverity.High, FindingCategory.Architecture),
            F(FindingSeverity.Low, FindingCategory.Testing, status: FindingStatus.Merged),
        };
        var pe = ProviderExecutionReport.FromRecords(
        [
            new ProviderExecutionRecord { ProviderName = "Mock", Success = true, EvidenceCount = 1 },
            new ProviderExecutionRecord { ProviderName = "Codex", Success = false, EvidenceCount = 0 },
        ]);

        var m = EngineeringMetrics.From(RunWith(findings, pe));

        Assert.Equal(3, m.TotalFindings);
        Assert.Equal(1, m.Critical);
        Assert.Equal(1, m.High);
        Assert.Equal(1, m.Low);
        Assert.Equal(1, m.MergedFindings);
        Assert.Equal(3, m.Projects);
        Assert.Equal(42, m.RepositoryFiles);
        Assert.Equal(1, m.EvidenceSources);   // only Mock produced evidence
        Assert.Equal(1, m.FailedProviders);   // Codex produced none
        Assert.Equal(3, m.CoverageByDiscipline.Values.Sum());
    }

    // ── Package builder + integrity ──────────────────────────────────────────────

    [Fact]
    public void Builder_produces_a_coherent_package()
    {
        var findings = new[] { F(FindingSeverity.High, FindingCategory.Security) };
        var run = RunWith(findings) with
        {
            Summary = new CouncilSummary
            {
                ExecutiveSummary = "Council reviewed Repo.",
                KeyRisks = ["[High] Security issue"],
                RecommendedNextActions = ["Fix the security issue."]
            }
        };

        var package = new EngineeringReviewPackageBuilder().Build(run);

        // Integrity: package faithfully reflects the run.
        Assert.Equal(run.RunId, package.AnalysisRunId);
        Assert.Equal("Repo", package.Repository);
        Assert.Equal("main", package.Branch);
        Assert.Equal(findings.Length, package.Findings.Count);
        Assert.Equal(findings.Length, package.Appendix.RawFindings.Count);
        Assert.Equal(EngineeringHealth.NeedsAttention, package.OverallEngineeringHealth);
        Assert.Equal(EngineeringRisk.High, package.OverallRisk);
        Assert.Contains(package.RecommendedNextActions, a => a.Contains("security", StringComparison.OrdinalIgnoreCase));
        // M15.3A: a discipline with NO successful evidence must never be surfaced as
        // a "no issues" strength (this run requests nothing and records no execution).
        Assert.DoesNotContain(package.KeyStrengths, s => s.Contains("Architecture", StringComparison.OrdinalIgnoreCase));
    }

    // ── Markdown export ──────────────────────────────────────────────────────────

    [Fact]
    public void Markdown_export_renders_the_review_document_structure()
    {
        var run = RunWith([F(FindingSeverity.High, FindingCategory.Security)]);
        var package = new EngineeringReviewPackageBuilder().Build(run);

        var md = new EngineeringReviewMarkdownExporter().Export(package);

        foreach (var heading in new[]
        {
            "# Engineering Review Package",
            "## Repository Information",
            "## Executive Summary",
            "## Engineering Health",
            "## Overall Risk",
            "## Engineering Metrics",
            "## Evidence Summary",
            "## Council Findings",
            "### Security Review",
            "## Recommended Backlog",
            "### Quick Wins",
            "### Strategic Improvements",
            "## Appendix",
        })
            Assert.Contains(heading, md);
    }
}
