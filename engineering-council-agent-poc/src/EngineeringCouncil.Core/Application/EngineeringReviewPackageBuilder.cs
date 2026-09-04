using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Application;

/// <summary>
/// Default builder: derives metrics, health, risk, evidence summary, strengths
/// and the appendix from the run, then assembles the package. Deterministic and
/// rule-based (no LLM). No rendering happens here.
/// </summary>
public sealed class EngineeringReviewPackageBuilder : IEngineeringReviewPackageBuilder
{
    /// <summary>Disciplines the platform reviews — used to compute "strength" gaps.</summary>
    private static readonly FindingCategory[] ReviewedDisciplines =
    [
        FindingCategory.Architecture, FindingCategory.CodeQuality, FindingCategory.Reliability,
        FindingCategory.Security, FindingCategory.Testing, FindingCategory.Documentation,
        FindingCategory.Observability
    ];

    public EngineeringReviewPackage Build(AnalysisRun run)
    {
        var metrics = EngineeringMetrics.From(run);
        var health = HealthRiskScorer.ScoreHealth(metrics);
        var risk = HealthRiskScorer.ScoreRisk(metrics);
        var evidence = EvidenceSummary.From(run.ProviderExecution, run.ObservationInterpretationSummary);
        var council = run.Summary;

        // Authoritative per-discipline coverage (M15.3A) — computed ONCE here from
        // existing run facts; exporters and consumers must never recompute it.
        var coverage = DisciplineCoverage.From(
            run.ProviderExecution, run.Findings, run.RequestedDisciplines);

        // Consolidated findings with the deterministic Council assessment (M14.2)
        // stamped additively from existing run data — reuses reconciliation and the
        // M12.3/M12.4 diagnostics; never recomputes them. The M14.3 summary is a pure
        // aggregation of THESE SAME assessed findings — the one authoritative count.
        var assessedFindings = CouncilAssessmentBuilder.Apply(run.Findings, run.CalibrationDiagnostics);
        var assessmentSummary = CouncilAssessmentBuilder.Summarize(assessedFindings);

        return new EngineeringReviewPackage
        {
            Version = "1.0",
            SchemaVersion = "1.1",
            Repository = run.SolutionName,
            Branch = run.Branch,
            Commit = run.Commit,
            RepositorySnapshot = run.RepositoryIdentity,
            GeneratedAt = DateTimeOffset.UtcNow,
            AnalysisRunId = run.RunId,
            AnalysisDuration = metrics.AnalysisDuration,
            ExecutiveSummary = BuildExecutiveSummary(run, health, risk, council, coverage),
            OverallEngineeringHealth = health,
            OverallRisk = risk,
            KeyStrengths = BuildStrengths(run.Findings, health, coverage),
            KeyRisks = council?.KeyRisks ?? [],
            RecommendedNextActions = council?.RecommendedNextActions ?? [],
            Findings = assessedFindings,
            CouncilAssessmentSummary = assessmentSummary,
            CouncilSummary = council,
            AcquisitionPlan = run.AcquisitionPlan,
            ProviderExecution = PackageExecutionView(run.ProviderExecution),
            Reconciliation = run.ReconciliationSummary,
            AcquisitionCoverage = AcquisitionCoverage.From(
                run.AcquisitionPlan, run.ProviderExecution, run.RequestedDisciplines),
            StaticAnalysisSources = StaticAnalysisSource.From(run.Evidence, run.Observations),
            EvidenceSummary = evidence,
            Metrics = metrics,
            DisciplineCoverage = coverage.Entries.Count > 0 ? coverage : null,
            Appendix = new ReviewAppendix
            {
                RawFindings = run.RawFindings,
                MergeNotes = BuildMergeNotes(run.Findings),
                ReconciliationGroups = run.ReconciliationGroups
            }
        };
    }

    private static string BuildExecutiveSummary(
        AnalysisRun run, EngineeringHealth health, EngineeringRisk risk, CouncilSummary? council, DisciplineCoverage coverage)
    {
        var lead = $"Engineering health is {health} with {risk} overall risk.";
        var text = string.IsNullOrWhiteSpace(council?.ExecutiveSummary)
            ? lead
            : $"{lead} {council!.ExecutiveSummary}";

        // Coverage limitation (M15.3A): never let a NoEvidence discipline read as
        // "reviewed and clean". Scoring is intentionally unchanged.
        var noEvidence = coverage.Entries
            .Where(e => e.Status == DisciplineCoverageStatus.NoEvidence)
            .Select(e => e.Discipline)
            .ToList();
        if (noEvidence.Count > 0)
            text += $" Coverage limitation: no successful evidence was acquired for "
                + $"{string.Join(", ", noEvidence)}; absence of findings there must not be interpreted as assurance.";

        return text;
    }

    /// <summary>
    /// A package-safe copy of the acquisition report. Cache-token telemetry
    /// (Milestone 015.2C) and the DERIVED token-efficiency metrics (Milestone
    /// 015.2D — ContextTokenActivity, FreshContextTokens, CacheReuseRatio) are
    /// deliberately stripped here: they are operational telemetry that lives in
    /// <c>provider-execution.json</c>, and the EXTERNAL Engineering Review package
    /// must not gain them (its records keep the historical input/output/total
    /// tokens only). Null-valued fields are omitted by the serializer, so the
    /// package stays byte-compatible.
    /// </summary>
    private static ProviderExecutionReport? PackageExecutionView(ProviderExecutionReport? report)
    {
        if (report is null)
            return null;

        return report with
        {
            TotalCacheReadInputTokens = null,
            TotalCacheCreationInputTokens = null,
            TotalContextTokenActivity = null,
            TotalFreshContextTokens = null,
            CacheReuseRatio = null,
            Records = report.Records
                .Select(r => r with
                {
                    CacheReadInputTokens = null,
                    CacheCreationInputTokens = null,
                    ContextTokenActivity = null,
                    FreshContextTokens = null,
                    CacheReuseRatio = null
                })
                .ToList()
        };
    }

    private static IReadOnlyList<string> BuildStrengths(
        IReadOnlyList<Finding> findings, EngineeringHealth health, DisciplineCoverage coverage)
    {
        var strengths = new List<string>();

        // A discipline with no findings above Info is a strength — but ONLY when
        // evidence was actually acquired (M15.3A). A discipline with zero successful
        // acquisitions must never be reported as "no issues identified": absence of
        // evidence is not assurance.
        var flagged = findings
            .Where(f => f.Severity > FindingSeverity.Info)
            .Select(f => f.Category)
            .ToHashSet();

        foreach (var discipline in ReviewedDisciplines)
        {
            var entry = coverage.Entries.FirstOrDefault(e =>
                string.Equals(e.Discipline, discipline.ToString(), StringComparison.Ordinal));
            if (entry is not null
                && entry.Status != DisciplineCoverageStatus.NoEvidence
                && !flagged.Contains(discipline))
                strengths.Add($"No {discipline} issues identified.");
        }

        if (health <= EngineeringHealth.Good && findings.Count > 0)
            strengths.Insert(0, $"Overall engineering health is {health}.");

        return strengths;
    }

    private static IReadOnlyList<string> BuildMergeNotes(IReadOnlyList<Finding> findings)
        => findings
            .Where(f => f.Status == FindingStatus.Merged)
            .Select(f => $"{f.Title} ({f.Id}) merged from {string.Join(", ", f.MergedFromFindingIds)} "
                + $"across agents: {string.Join(", ", f.SourceAgents)}.")
            .ToList();
}
