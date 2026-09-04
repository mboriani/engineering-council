using System.Text.Json;
using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Core.Serialization;
using EngineeringCouncil.Infrastructure.Reporting;
using EngineeringCouncil.Tests.Contracts;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 014.2 — deterministic Council assessment of each consolidated finding.
/// The classification (SingleSource / StrongAgreement / AgreementWithDifferences /
/// PotentialConflict) must be a pure, provider-neutral projection of EXISTING
/// authoritative data (reconciler fields + M12.3/M12.4 calibration diagnostics):
/// no LLM, no re-reconciliation, no voting, no provider weighting. It must never
/// infer a conflict from a severity/location/observation-type difference or from a
/// provider not reporting the finding. All tests are offline and deterministic.
/// </summary>
public sealed class CouncilAssessmentTests
{
    private const FindingSeverity DefaultSeverity = FindingSeverity.High;
    private const FindingConfidence DefaultConfidence = FindingConfidence.High;

    // ── Rule tests ────────────────────────────────────────────────────────────

    [Fact]
    public void A_single_supported_finding_is_single_source()
    {
        var f = Consolidated("SEC-1", ["Claude"], severityRange: "High");
        var a = CouncilAssessmentBuilder.Assess(f, null);

        Assert.Equal(ReconciliationAssessmentType.SingleSource, a.Type);
        Assert.Equal(1, a.AgreementCount);
        Assert.Equal(["Claude"], a.SupportingProviders);
        Assert.Empty(a.Differences);
        // Single-source is not discounted: severity/confidence are untouched by the assessment.
        Assert.Equal(DefaultSeverity, f.Severity);
        Assert.Equal(DefaultConfidence, f.Confidence);
    }

    [Fact]
    public void Two_providers_without_differences_is_strong_agreement()
    {
        var f = Consolidated("SEC-1", ["Claude", "Codex"], severityRange: "High");
        var a = CouncilAssessmentBuilder.Assess(f, null);

        Assert.Equal(ReconciliationAssessmentType.StrongAgreement, a.Type);
        Assert.Equal(2, a.AgreementCount);
        Assert.Equal(["Claude", "Codex"], a.SupportingProviders);
        Assert.Empty(a.Differences);
    }

    [Fact]
    public void Three_providers_without_differences_is_strong_agreement()
    {
        var f = Consolidated("SEC-1", ["OpenCode", "ClaudeCode", "Codex"], severityRange: "High");
        var a = CouncilAssessmentBuilder.Assess(f, null);

        Assert.Equal(ReconciliationAssessmentType.StrongAgreement, a.Type);
        Assert.Equal(3, a.AgreementCount);
        Assert.Equal(["ClaudeCode", "Codex", "OpenCode"], a.SupportingProviders);
        Assert.Empty(a.Differences);
    }

    [Fact]
    public void A_severity_disagreement_is_agreement_with_differences_not_a_conflict()
    {
        var f = Consolidated("SEC-1", ["Claude", "Codex"], severityRange: "Medium\u2013High"); // reconciler en-dash range
        var a = CouncilAssessmentBuilder.Assess(f, null);

        Assert.Equal(ReconciliationAssessmentType.AgreementWithDifferences, a.Type);
        Assert.Equal([ReconciliationAssessmentDisagreement.Severity], a.Differences);
        Assert.Empty(a.ContradictionReasons);
    }

    [Fact]
    public void An_observation_type_disagreement_is_agreement_with_differences()
    {
        var f = Consolidated("REL-1", ["Claude", "OpenAI"], severityRange: "High");
        var diagnostics = Diagnostics(("REL-1", CalibrationDiagnosticType.ObservationTypeDisagreement));
        var a = CouncilAssessmentBuilder.Assess(f, diagnostics);

        Assert.Equal(ReconciliationAssessmentType.AgreementWithDifferences, a.Type);
        Assert.Equal([ReconciliationAssessmentDisagreement.ObservationType], a.Differences);
    }

    [Fact]
    public void A_location_disagreement_is_agreement_with_differences_not_a_conflict()
    {
        // The reconciler's own contradiction reason for disjoint locations is a DIFFERENCE,
        // never a conflict.
        var f = Consolidated("SEC-1", ["Claude", "OpenAI"], severityRange: "High",
            contradictionReasons: ["Sources reference different files (no common location)."],
            hasContradiction: true);
        var a = CouncilAssessmentBuilder.Assess(f, null);

        Assert.Equal(ReconciliationAssessmentType.AgreementWithDifferences, a.Type);
        Assert.Equal([ReconciliationAssessmentDisagreement.Location], a.Differences);
        Assert.Empty(a.ContradictionReasons);
    }

    [Fact]
    public void A_provider_not_reporting_the_finding_is_not_a_conflict()
    {
        var f = Consolidated("SEC-1", ["Claude", "Codex"], severityRange: "High");
        var a = CouncilAssessmentBuilder.Assess(f, null);

        // "OpenCode" not reporting the finding changes nothing: no conflict, count preserved.
        Assert.Equal(ReconciliationAssessmentType.StrongAgreement, a.Type);
        Assert.Equal(2, a.AgreementCount);
        Assert.Equal(["Claude", "Codex"], a.SupportingProviders);
    }

    [Fact]
    public void Only_an_explicit_contradiction_is_potential_conflict()
    {
        // The severity reason is a difference; the unexplained retry-contradiction is explicit.
        var f = Consolidated("SEC-1", ["Claude", "Codex"], severityRange: "Low\u2013Critical",
            contradictionReasons:
            [
                "Material severity disagreement: Low\u2013Critical.",
                "Providers assert contradictory retry semantics for this call."
            ],
            hasContradiction: true);
        var a = CouncilAssessmentBuilder.Assess(f, null);

        Assert.Equal(ReconciliationAssessmentType.PotentialConflict, a.Type);
        Assert.Equal(2, a.AgreementCount);
        Assert.Contains(ReconciliationAssessmentDisagreement.Severity, a.Differences);
        Assert.Equal(["Providers assert contradictory retry semantics for this call."], a.ContradictionReasons);
    }

    [Fact]
    public void Distinct_provider_counting_is_preserved()
    {
        // Three RAW findings from ONE provider merge into a single-provider finding.
        var single = Consolidated("SEC-1", ["Claude"], severityRange: "High", supportingFindingIds: ["R1", "R2", "R3"]);
        var singleA = CouncilAssessmentBuilder.Assess(single, null);
        Assert.Equal(ReconciliationAssessmentType.SingleSource, singleA.Type);
        Assert.Equal(1, singleA.AgreementCount); // one provider, not three raw findings

        // Two independent providers stay two.
        var multi = Consolidated("SEC-2", ["Claude", "SARIF"], severityRange: "High");
        var multiA = CouncilAssessmentBuilder.Assess(multi, null);
        Assert.Equal(ReconciliationAssessmentType.StrongAgreement, multiA.Type);
        Assert.Equal(2, multiA.AgreementCount);
    }

    // ── Determinism ───────────────────────────────────────────────────────────

    [Fact]
    public void The_assessment_is_deterministic_across_input_order_and_serialization()
    {
        var diagnostics = Diagnostics(
            ("SEC-1", CalibrationDiagnosticType.LocationDisagreement),
            ("SEC-1", CalibrationDiagnosticType.SeverityDisagreement));

        string[] providersForward = ["Codex", "Claude"];
        var providersReverse = providersForward.Reverse().ToList();
        var a = CouncilAssessmentBuilder.Assess(
            Consolidated("SEC-1", providersForward, severityRange: "Medium\u2013High"), diagnostics);
        var b = CouncilAssessmentBuilder.Assess(
            Consolidated("SEC-1", providersReverse.ToList(), severityRange: "High"), diagnostics);

        Assert.Equal(a.Type, b.Type); // "High" single-value range still ruled by the diagnostic signal
        Assert.Equal(Serialize(a), Serialize(b));

        var again = CouncilAssessmentBuilder.Assess(
            Consolidated("SEC-1", providersForward, severityRange: "Medium\u2013High"), diagnostics);
        Assert.Equal(Serialize(a), Serialize(again));
    }

    [Fact]
    public void The_package_serializes_assessments_deterministically_without_leaking_diagnostics()
    {
        var run = BuildM141ShapedRun();
        var package = new EngineeringReviewPackageBuilder().Build(run) with { GeneratedAt = DateTimeOffset.UnixEpoch };
        var json = JsonSerializer.Serialize(package, CouncilJson.Options);

        Assert.Equal(JsonSerializer.Serialize(package, CouncilJson.Options), json);

        Assert.Contains("\"councilAssessment\":", json);
        Assert.Contains("\"agreementWithDifferences\"", json);
        // Internal M12.3/M12.4 diagnostic names never leak into the package.
        foreach (var leak in new[] { "severityDisagreement", "observationTypeDisagreement", "locationDisagreement", "calibration" })
            Assert.DoesNotContain(leak, json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("1.1", package.SchemaVersion);
    }

    // ── Consumer DTO + Markdown projection ────────────────────────────────────

    [Fact]
    public void Consumer_dto_can_deserialize_the_council_assessment()
    {
        var run = BuildM141ShapedRun();
        var json = JsonSerializer.Serialize(new EngineeringReviewPackageBuilder().Build(run), CouncilJson.Options);
        var dto = JsonSerializer.Deserialize<PackageContract>(json, CouncilJson.Options)!;

        Assert.Equal("1.1", dto.SchemaVersion);
        Assert.Equal(13, dto.Findings.Count);

        var clean = Assert.Single(dto.Findings, f => f.Id == "S-01");
        Assert.Equal("strongAgreement", clean.CouncilAssessment!.Type);
        Assert.Equal(3, clean.CouncilAssessment.AgreementCount);

        var differed = Assert.Single(dto.Findings, f => f.Id == "S-03");
        Assert.Equal("agreementWithDifferences", differed.CouncilAssessment!.Type);
        Assert.Contains("severity", differed.CouncilAssessment.Differences);
        Assert.Contains("location", differed.CouncilAssessment.Differences);
    }

    [Fact]
    public void Markdown_projects_the_council_assessment()
    {
        var package = new EngineeringReviewPackageBuilder().Build(BuildM141ShapedRun());
        var md = new EngineeringReviewMarkdownExporter().Export(package);

        Assert.Contains("Council Assessment: StrongAgreement", md);
        Assert.Contains("Council Assessment: AgreementWithDifferences", md);
        Assert.Contains("Supporting Providers:", md);
        Assert.Contains("Differences: Location", md);
        Assert.Contains("Differences: None", md); // clean finding
    }

    // ── M14.1-shaped replay fixture ───────────────────────────────────────────

    [Fact]
    public void M141_shaped_fixture_classifies_exactly_the_expected_counts()
    {
        var run = BuildM141ShapedRun();
        var package = new EngineeringReviewPackageBuilder().Build(run);

        Assert.Equal(13, package.Findings.Count);
        Assert.Equal(0, package.Findings.Count(f => f.CouncilAssessment is null));

        var byType = package.Findings
            .GroupBy(f => f.CouncilAssessment!.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(7, byType[ReconciliationAssessmentType.SingleSource]);
        Assert.Equal(1, byType[ReconciliationAssessmentType.StrongAgreement]);
        Assert.Equal(5, byType[ReconciliationAssessmentType.AgreementWithDifferences]);
        Assert.Equal(0, byType.GetValueOrDefault(ReconciliationAssessmentType.PotentialConflict));

        // Provider-support distribution mirrors M14.1: 2 shared × 3 providers, 4 shared × 2.
        Assert.Equal(2, package.Findings.Count(f => f.CouncilAssessment!.AgreementCount == 3));
        Assert.Equal(4, package.Findings.Count(f => f.CouncilAssessment!.AgreementCount == 2));

        // Disagreement-type distribution mirrors M14.1: severity 2 · observation-type 2 · location 5.
        var withDisagreement = package.Findings
            .SelectMany(f => f.CouncilAssessment!.Differences);
        Assert.Equal(2, withDisagreement.Count(d => d == ReconciliationAssessmentDisagreement.Severity));
        Assert.Equal(2, withDisagreement.Count(d => d == ReconciliationAssessmentDisagreement.ObservationType));
        Assert.Equal(5, withDisagreement.Count(d => d == ReconciliationAssessmentDisagreement.Location));

        // schemaVersion 1.1 unchanged; no calibration internals leak.
        Assert.Equal("1.1", package.SchemaVersion);
    }

    // ── Summary (Milestone 014.3) ────────────────────────────────────────────

    [Fact]
    public void Summary_counts_each_assessment_type()
    {
        var findings = new[]
        {
            CouncilAssessmentBuilder.Assess(Consolidated("A", ["Claude"], severityRange: "High"), null),
            CouncilAssessmentBuilder.Assess(Consolidated("B", ["Claude", "Codex"], severityRange: "High"), null),
            CouncilAssessmentBuilder.Assess(Consolidated("C", ["Claude", "Codex"], severityRange: "Medium–High"), null),
            CouncilAssessmentBuilder.Assess(
                Consolidated("D", ["Claude", "Codex"], severityRange: "Low–Critical",
                    contradictionReasons: ["Providers assert contradictory retry semantics for this call."],
                    hasContradiction: true), null)
        };
        // Stamp each pre-computed assessment onto a finding, as the package builder does.
        var stamped = findings.Select((a, i) => Consolidated($"F{i}", a.SupportingProviders, "High") with { CouncilAssessment = a }).ToList();

        var summary = CouncilAssessmentBuilder.Summarize(stamped);

        Assert.Equal(1, summary.SingleSourceCount);
        Assert.Equal(1, summary.StrongAgreementCount);
        Assert.Equal(1, summary.AgreementWithDifferencesCount);
        Assert.Equal(1, summary.PotentialConflictCount);
        Assert.Equal(4, summary.TotalAssessed);
    }

    [Fact]
    public void Summary_supports_zero_counts()
    {
        var summary = CouncilAssessmentBuilder.Summarize([]);

        Assert.Equal(0, summary.SingleSourceCount);
        Assert.Equal(0, summary.StrongAgreementCount);
        Assert.Equal(0, summary.AgreementWithDifferencesCount);
        Assert.Equal(0, summary.PotentialConflictCount);
        Assert.Equal(0, summary.TotalAssessed);
    }

    [Fact]
    public void Summary_ignores_findings_without_an_assessment()
    {
        // A raw/unassessed finding (CouncilAssessment == null, e.g. a raw finding
        // that never went through CouncilAssessmentBuilder.Apply) must not be counted
        // under any type — it is simply excluded, never guessed.
        var assessed = Consolidated("A", ["Claude"], severityRange: "High") with
        {
            CouncilAssessment = CouncilAssessmentBuilder.Assess(Consolidated("A", ["Claude"], severityRange: "High"), null)
        };
        var unassessed = Consolidated("RAW-1", ["Claude"], severityRange: "High"); // CouncilAssessment left null

        var summary = CouncilAssessmentBuilder.Summarize([assessed, unassessed]);

        Assert.Equal(1, summary.TotalAssessed);
        Assert.Equal(1, summary.SingleSourceCount);
    }

    [Fact]
    public void Summary_is_deterministic_across_finding_order()
    {
        var run = BuildM141ShapedRun();
        var assessed = CouncilAssessmentBuilder.Apply(run.Findings, run.CalibrationDiagnostics);

        var forward = CouncilAssessmentBuilder.Summarize(assessed);
        var reversed = CouncilAssessmentBuilder.Summarize(assessed.Reverse().ToList());

        Assert.Equal(forward, reversed);
    }

    [Fact]
    public void Package_serializes_the_summary_additively()
    {
        var package = new EngineeringReviewPackageBuilder().Build(BuildM141ShapedRun());
        var json = JsonSerializer.Serialize(package, CouncilJson.Options);

        Assert.Contains("\"councilAssessmentSummary\":", json);
        Assert.Contains("\"strongAgreementCount\": 1", json);
        Assert.Contains("\"agreementWithDifferencesCount\": 5", json);
        Assert.Contains("\"singleSourceCount\": 7", json);
        Assert.Contains("\"potentialConflictCount\": 0", json);
        Assert.Equal("1.1", package.SchemaVersion); // additive — schema unchanged
    }

    [Fact]
    public void Consumer_dto_can_deserialize_the_summary()
    {
        var json = JsonSerializer.Serialize(new EngineeringReviewPackageBuilder().Build(BuildM141ShapedRun()), CouncilJson.Options);
        var dto = JsonSerializer.Deserialize<PackageContract>(json, CouncilJson.Options)!;

        Assert.NotNull(dto.CouncilAssessmentSummary);
        Assert.Equal(7, dto.CouncilAssessmentSummary!.SingleSourceCount);
        Assert.Equal(1, dto.CouncilAssessmentSummary.StrongAgreementCount);
        Assert.Equal(5, dto.CouncilAssessmentSummary.AgreementWithDifferencesCount);
        Assert.Equal(0, dto.CouncilAssessmentSummary.PotentialConflictCount);
    }

    [Fact]
    public void Markdown_renders_the_council_assessment_summary()
    {
        var package = new EngineeringReviewPackageBuilder().Build(BuildM141ShapedRun());
        var md = new EngineeringReviewMarkdownExporter().Export(package);

        Assert.Contains("## Council Assessment", md);
        Assert.Contains("Strong agreement: 1", md);
        Assert.Contains("Agreement with differences: 5", md);
        Assert.Contains("Single source: 7", md);
        Assert.Contains("Potential conflicts: 0", md);
    }

    [Fact]
    public void M141_shaped_fixture_produces_the_expected_summary()
    {
        var package = new EngineeringReviewPackageBuilder().Build(BuildM141ShapedRun());

        Assert.NotNull(package.CouncilAssessmentSummary);
        var s = package.CouncilAssessmentSummary!;
        Assert.Equal(7, s.SingleSourceCount);
        Assert.Equal(1, s.StrongAgreementCount);
        Assert.Equal(5, s.AgreementWithDifferencesCount);
        Assert.Equal(0, s.PotentialConflictCount);
        Assert.Equal(13, s.TotalAssessed);

        // Single authoritative calculation: the summary must equal an independent
        // Summarize() call over the SAME package findings (never a second code path).
        Assert.Equal(CouncilAssessmentBuilder.Summarize(package.Findings), s);
    }

    // ── Fixture / helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// A deterministic fixture representing the M14.1 Council shapes: 13 consolidated
    /// Security findings (7 exclusive, 2 supported by all 3 providers, 4 by exactly 2)
    /// with severity disagreements 2, observation-type 2, location 5, contradictions 0.
    /// </summary>
    private static AnalysisRun BuildM141ShapedRun()
    {
        var findings = new List<Finding>
        {
            // 7 exclusive (single provider) — SingleSource.
            Exclusive("EX-01", ["OpenCode"]),
            Exclusive("EX-02", ["OpenCode"]),
            Exclusive("EX-03", ["OpenCode"]),
            Exclusive("EX-04", ["ClaudeCode"]),
            Exclusive("EX-05", ["ClaudeCode"]),
            Exclusive("EX-06", ["ClaudeCode"]),
            Exclusive("EX-07", ["Codex"]),
            // S-01: all 3 providers, no disagreements — StrongAgreement.
            Consolidated("S-01", ["OpenCode", "Codex", "ClaudeCode"], severityRange: "High"),
            // S-02: all 3 providers, severity + location — AgreementWithDifferences.
            Consolidated("S-02", ["OpenCode", "Codex", "ClaudeCode"], severityRange: "Medium\u2013High"),
            // S-03: 2 providers, severity + location — AgreementWithDifferences.
            Consolidated("S-03", ["Codex", "ClaudeCode"], severityRange: "Low\u2013Medium"),
            // S-04: 2 providers, observation-type + location — AgreementWithDifferences.
            Consolidated("S-04", ["OpenCode", "ClaudeCode"], severityRange: "Medium"),
            // S-05: 2 providers, observation-type + location — AgreementWithDifferences.
            Consolidated("S-05", ["OpenCode", "Codex"], severityRange: "High"),
            // S-06: 2 providers, location only — AgreementWithDifferences.
            Consolidated("S-06", ["ClaudeCode", "Codex"], severityRange: "High")
        };

        var diagnostics = Diagnostics(
            ("S-02", CalibrationDiagnosticType.LocationDisagreement),
            ("S-03", CalibrationDiagnosticType.LocationDisagreement),
            ("S-04", CalibrationDiagnosticType.LocationDisagreement),
            ("S-04", CalibrationDiagnosticType.ObservationTypeDisagreement),
            ("S-05", CalibrationDiagnosticType.LocationDisagreement),
            ("S-05", CalibrationDiagnosticType.ObservationTypeDisagreement),
            ("S-06", CalibrationDiagnosticType.LocationDisagreement));

        return new AnalysisRun
        {
            RunId = "m14-1-shapes",
            TargetPath = "/repo",
            SolutionName = "m14-1-council-smoke-repo",
            Branch = "main",
            Commit = "abc1234",
            Status = AnalysisRunStatus.Completed,
            RequestedDisciplines = [FindingCategory.Security],
            Findings = findings,
            CalibrationDiagnostics = diagnostics
        };
    }

    private static Finding Exclusive(string id, IReadOnlyList<string> providers)
        => Consolidated(id, providers, severityRange: "High");

    private static Finding Consolidated(
        string id,
        IReadOnlyList<string> providers,
        string severityRange,
        IReadOnlyList<string>? contradictionReasons = null,
        bool hasContradiction = false,
        IReadOnlyList<string>? supportingFindingIds = null,
        FindingSeverity severity = DefaultSeverity,
        FindingConfidence confidence = DefaultConfidence)
        => new()
        {
            Id = id,
            Title = "Finding " + id,
            Category = FindingCategory.Security,
            Severity = severity,
            Confidence = confidence,
            EvidenceProvider = providers[0],
            SupportingProviders = providers,
            AgreementCount = providers.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            SeverityRange = severityRange,
            IsConsolidated = providers.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1 || (supportingFindingIds?.Count ?? 1) > 1,
            Status = FindingStatus.New,
            HasContradiction = hasContradiction,
            ContradictionReasons = contradictionReasons ?? [],
            SupportingFindingIds = supportingFindingIds ?? [id + "-raw"]
        };

    /// <summary>Builds a minimal M12.3/M12.4 diagnostic report with the given <c>(FindingId, Type)</c> signals.</summary>
    private static CalibrationDiagnosticsReport Diagnostics(params (string FindingId, CalibrationDiagnosticType Type)[] signals)
    {
        var diagnostics = signals
            .Select(s => new FindingDiagnostic { FindingId = s.FindingId, Type = s.Type, Discipline = FindingCategory.Security })
            .ToList();

        return new CalibrationDiagnosticsReport
        {
            RunId = "m14-1-shapes",
            Diagnostics = diagnostics
                .OrderBy(d => d.FindingId, StringComparer.Ordinal)
                .ThenBy(d => d.Type)
                .ToList()
        };
    }

    private static string Serialize(ReconciliationAssessment assessment)
        => JsonSerializer.Serialize(assessment, CouncilJson.Options);
}