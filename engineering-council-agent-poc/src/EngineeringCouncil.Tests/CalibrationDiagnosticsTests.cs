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
/// Milestone 012.3 + 012.4 — finding/observation calibration diagnostics. The report
/// is a deterministic, descriptive projection of a completed run's provider comparison
/// (M12.2), reconciled findings, raw findings, normalized observations and evidence.
/// Diagnostics are generated ONLY for Comparable disciplines; NonComparable/Incomplete
/// are limitations. No LLM calls, no rescan, no semantic matching, no ranking/scoring,
/// no reconciliation/context/prompt changes. M12.4 adds ObservationTypeDisagreement and
/// LocationDisagreement, both on shared findings with provider attribution from existing
/// provenance only. The external engineering-review-package.json contract is unchanged
/// (schemaVersion 1.1).
/// </summary>
public sealed class CalibrationDiagnosticsTests : IDisposable
{
    private const string KeyVariable = "EC_TEST_CALIBRATION_KEY";
    private const string KeyValue = "calibration-test-key-not-a-real-secret";

    private readonly string _repo = Path.Combine(Path.GetTempPath(), "ec-calibration-repo-" + Guid.NewGuid().ToString("N"));
    private readonly string _outputs = Path.Combine(Path.GetTempPath(), "ec-calibration-out-" + Guid.NewGuid().ToString("N"));

    public CalibrationDiagnosticsTests()
    {
        Environment.SetEnvironmentVariable(KeyVariable, KeyValue);
        Directory.CreateDirectory(Path.Combine(_repo, "src"));
        File.WriteAllText(Path.Combine(_repo, "Sample.sln"), "solution\n");
        File.WriteAllText(Path.Combine(_repo, "src", "Payments.cs"), "public class PaymentClient { const string K = \"x\"; }\n");
        File.WriteAllText(Path.Combine(_repo, "src", "Query.cs"), "public class Query { }\n");
        File.WriteAllText(Path.Combine(_repo, "src", "Deser.cs"), "public class Deser { }\n");
        File.WriteAllText(Path.Combine(_repo, "src", "Auth.cs"), "public class Auth { }\n");
    }

    // ── Exclusive findings ────────────────────────────────────────────────────

    [Fact]
    public void An_exclusive_Claude_finding_is_reported_as_a_diagnostic()
    {
        var run = ComparableRun(
            Raw("c1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "Claude", line: 3),
            Raw("o1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "OpenAI", line: 4),
            Raw("c2", "SQL injection", "CWE-89", "src/Query.cs", "Claude", line: 9));

        var report = CalibrationDiagnosticsBuilder.Build(run)!;

        var claudeExclusive = report.Diagnostics
            .Where(d => d.Type == CalibrationDiagnosticType.ExclusiveFinding && d.Provider == "Claude")
            .ToList();
        var exclusive = Assert.Single(claudeExclusive);
        Assert.Equal(FindingCategory.Security, exclusive.Discipline);
        Assert.Equal("SQL injection", exclusive.Title);
        Assert.Equal(FindingSeverity.High, exclusive.Severity);
        Assert.Contains("src/Query.cs", exclusive.Files);
    }

    [Fact]
    public void An_exclusive_OpenAI_finding_is_reported_as_a_diagnostic()
    {
        var run = ComparableRun(
            Raw("c1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "Claude", line: 3),
            Raw("o1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "OpenAI", line: 4),
            Raw("o2", "Unsafe deserialization", "CWE-502", "src/Deser.cs", "OpenAI", line: 5));

        var report = CalibrationDiagnosticsBuilder.Build(run)!;

        var openaiExclusive = report.Diagnostics
            .Where(d => d.Type == CalibrationDiagnosticType.ExclusiveFinding && d.Provider == "OpenAI")
            .ToList();
        var exclusive = Assert.Single(openaiExclusive);
        Assert.Equal("Unsafe deserialization", exclusive.Title);
        Assert.Contains("src/Deser.cs", exclusive.Files);
    }

    [Fact]
    public void A_shared_finding_is_never_reported_as_exclusive()
    {
        var run = ComparableRun(
            Raw("c1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "Claude", line: 3),
            Raw("o1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "OpenAI", line: 4));

        var report = CalibrationDiagnosticsBuilder.Build(run)!;

        Assert.DoesNotContain(report.Diagnostics, d => d.Type == CalibrationDiagnosticType.ExclusiveFinding);
        // The shared finding still reconciles as supported by both providers.
        var shared = Assert.Single(run.Findings);
        Assert.Equal(2, shared.AgreementCount);
    }

    // ── Severity disagreement ─────────────────────────────────────────────────

    [Fact]
    public void A_shared_finding_with_severity_disagreement_is_reported()
    {
        var run = ComparableRun(
            Raw("c1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "Claude", line: 3,
                severity: FindingSeverity.High),
            Raw("o1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "OpenAI", line: 4,
                severity: FindingSeverity.Medium));

        var report = CalibrationDiagnosticsBuilder.Build(run)!;

        var disagreement = Assert.Single(
            report.Diagnostics, d => d.Type == CalibrationDiagnosticType.SeverityDisagreement);
        Assert.Equal("Hardcoded API key", disagreement.Title);
        Assert.Contains("–", disagreement.SeverityRange); // reconciler range "Medium–High"
        Assert.Equal(FindingSeverity.High, disagreement.Severity); // resolved severity
        Assert.Equal("Claude", Assert.Single(disagreement.ProviderSeverities, p => p.Provider == "Claude").Provider);
        Assert.Equal("High", Assert.Single(disagreement.ProviderSeverities, p => p.Provider == "Claude").Severity);
        Assert.Equal("Medium", Assert.Single(disagreement.ProviderSeverities, p => p.Provider == "OpenAI").Severity);
    }

    [Fact]
    public void Same_severity_across_providers_produces_no_signal()
    {
        var run = ComparableRun(
            Raw("c1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "Claude", line: 3,
                severity: FindingSeverity.High),
            Raw("o1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "OpenAI", line: 4,
                severity: FindingSeverity.High));

        var report = CalibrationDiagnosticsBuilder.Build(run)!;

        Assert.DoesNotContain(report.Diagnostics, d => d.Type == CalibrationDiagnosticType.SeverityDisagreement);
        Assert.Equal("High", Assert.Single(run.Findings).SeverityRange);
    }

    // ── Low agreement ─────────────────────────────────────────────────────────

    [Fact]
    public void Low_agreement_with_a_sufficient_sample_is_reported()
    {
        var run = ComparableRun(
            Raw("c1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "Claude", line: 3),
            Raw("o1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "OpenAI", line: 4),
            Raw("c2", "SQL injection", "CWE-89", "src/Query.cs", "Claude", line: 9),
            Raw("c3", "Insecure deserialization", "CWE-502", "src/Deser.cs", "Claude", line: 9),
            Raw("c4", "Weak crypto", "CWE-327", "src/Auth.cs", "Claude", line: 9));

        var report = CalibrationDiagnosticsBuilder.Build(run)!;

        var low = Assert.Single(report.Diagnostics, d => d.Type == CalibrationDiagnosticType.LowAgreement);
        Assert.Equal(FindingCategory.Security, low.Discipline);
        Assert.Equal(4, low.ConsolidatedFindingCount);   // 1 shared + 3 exclusive
        Assert.Equal(1, low.SharedFindingCount);
        Assert.Equal(0.25, low.AgreementRate!.Value, 3); // 1/4 < 0.5 threshold
        Assert.Equal(3, low.ExclusiveFindingCountByProvider.Single(e => e.Name == "Claude").Count);
        Assert.Equal(0, low.ExclusiveFindingCountByProvider.Single(e => e.Name == "OpenAI").Count);
    }

    [Fact]
    public void A_small_sample_produces_no_low_agreement_signal()
    {
        // 2 consolidated findings, 0 shared ⇒ rate 0.0 — but the sample (2) is below
        // the MinimumSampleFindings (3), so no signal.
        var run = ComparableRun(
            Raw("c1", "SQL injection", "CWE-89", "src/Query.cs", "Claude", line: 9),
            Raw("o1", "Unsafe deserialization", "CWE-502", "src/Deser.cs", "OpenAI", line: 5));

        var report = CalibrationDiagnosticsBuilder.Build(run)!;

        Assert.DoesNotContain(report.Diagnostics, d => d.Type == CalibrationDiagnosticType.LowAgreement);
        Assert.Equal(2, run.Findings.Count);
        // Exclusive findings still surface; only the LowAgreement signal is suppressed for a small sample.
        Assert.Equal(2, report.Diagnostics.Count(d => d.Type == CalibrationDiagnosticType.ExclusiveFinding));
    }

    // ── M12.4 — observation-type disagreement ─────────────────────────────────

    [Fact]
    public void Different_observation_types_on_a_shared_finding_are_reported()
    {
        var run = ComparableObservationRun(
            observations:
            [
                Obs("obs-c1", ObservationTypes.MissingTimeout, "Claude", "src/Payments/PaymentClient.cs", 42),
                Obs("obs-o1", "ResilienceRisk", "OpenAI", "src/Payments/PaymentClient.cs", 42)
            ],
            rawFindings:
            [
                RawWithObs("c1", "Missing timeout", ["obs-c1"], "Claude"),
                RawWithObs("o1", "Missing timeout", ["obs-o1"], "OpenAI")
            ],
            findings: [SharedFinding("REL-abc123", "Missing timeout", ["c1", "o1"])]);

        var report = CalibrationDiagnosticsBuilder.Build(run)!;

        var disagreement = Assert.Single(
            report.Diagnostics, d => d.Type == CalibrationDiagnosticType.ObservationTypeDisagreement);
        Assert.Equal("REL-abc123", disagreement.FindingId);
        Assert.Equal(FindingCategory.Security, disagreement.Discipline);
        Assert.Equal(ObservationTypes.MissingTimeout,
            Assert.Single(disagreement.ObservationTypesByProvider, p => p.Provider == "Claude").ObservationTypes.Single());
        Assert.Equal("ResilienceRisk",
            Assert.Single(disagreement.ObservationTypesByProvider, p => p.Provider == "OpenAI").ObservationTypes.Single());
        Assert.Contains("obs-c1", disagreement.ObservationIds);
        Assert.Contains("obs-o1", disagreement.ObservationIds);
        // Same location ⇒ only the type signal fires.
        Assert.DoesNotContain(report.Diagnostics, d => d.Type == CalibrationDiagnosticType.LocationDisagreement);
    }

    [Fact]
    public void Same_observation_type_across_providers_produces_no_signal()
    {
        var run = ComparableObservationRun(
            observations:
            [
                Obs("obs-c1", ObservationTypes.HardcodedSecret, "Claude", "src/Payments.cs", 3),
                Obs("obs-o1", ObservationTypes.HardcodedSecret, "OpenAI", "src/Payments.cs", 3)
            ],
            rawFindings:
            [
                RawWithObs("c1", "Hardcoded secret", ["obs-c1"], "Claude"),
                RawWithObs("o1", "Hardcoded secret", ["obs-o1"], "OpenAI")
            ],
            findings: [SharedFinding("SEC-abc", "Hardcoded secret", ["c1", "o1"])]);

        var report = CalibrationDiagnosticsBuilder.Build(run)!;

        Assert.DoesNotContain(report.Diagnostics, d => d.Type == CalibrationDiagnosticType.ObservationTypeDisagreement);
    }

    [Fact]
    public void Provider_attribution_comes_from_provenance_not_text()
    {
        // obs-c1's SourceProvider is blank; its provider must resolve through
        // Observation → SourceEvidenceId → Evidence.ProviderName. obs-c2 names a
        // non-compared provider ("Mock") with no matching evidence ⇒ skipped.
        var run = ObservationRun(
            [Rec("Claude", FindingCategory.Security), Rec("OpenAI", FindingCategory.Security)],
            observations:
            [
                Obs("obs-c1", ObservationTypes.MissingTimeout, "Claude", "src/Payments.cs", 42, sourceProvider: ""),
                Obs("obs-o1", "ResilienceRisk", "OpenAI", "src/Payments.cs", 42),
                Obs("obs-c2", "SomethingElse", "Mock", "src/Other.cs", 1)
            ],
            rawFindings:
            [
                RawWithObs("c1", "Missing timeout", ["obs-c1", "obs-c2"], "Claude"),
                RawWithObs("o1", "Missing timeout", ["obs-o1"], "OpenAI")
            ],
            findings: [SharedFinding("REL-abc123", "Missing timeout", ["c1", "o1"])],
            evidence:
            [
                new Evidence { Id = "claude-evidence", ProviderName = "Claude", ProviderId = "claude", ProviderType = EvidenceProviderType.LLM },
                new Evidence { Id = "openai-evidence", ProviderName = "OpenAI", ProviderId = "openai", ProviderType = EvidenceProviderType.LLM }
            ]);

        var report = CalibrationDiagnosticsBuilder.Build(run)!;

        var disagreement = Assert.Single(
            report.Diagnostics, d => d.Type == CalibrationDiagnosticType.ObservationTypeDisagreement);
        Assert.Equal(ObservationTypes.MissingTimeout,
            Assert.Single(disagreement.ObservationTypesByProvider, p => p.Provider == "Claude").ObservationTypes.Single());
        Assert.Equal("ResilienceRisk",
            Assert.Single(disagreement.ObservationTypesByProvider, p => p.Provider == "OpenAI").ObservationTypes.Single());
        // The un-attributable observation is never guessed into a provider.
        Assert.DoesNotContain("obs-c2", disagreement.ObservationIds);
    }

    // ── M12.4 — location disagreement ────────────────────────────────────────

    [Fact]
    public void Different_files_produce_a_location_disagreement()
    {
        var run = ComparableObservationRun(
            observations:
            [
                Obs("obs-c1", ObservationTypes.HardcodedSecret, "Claude", "src/Payments/PaymentClient.cs", 42),
                Obs("obs-o1", ObservationTypes.HardcodedSecret, "OpenAI", "src/Payments/PaymentService.cs", 42)
            ],
            rawFindings:
            [
                RawWithObs("c1", "Hardcoded secret", ["obs-c1"], "Claude"),
                RawWithObs("o1", "Hardcoded secret", ["obs-o1"], "OpenAI")
            ],
            findings: [SharedFinding("SEC-abc", "Hardcoded secret", ["c1", "o1"])]);

        var report = CalibrationDiagnosticsBuilder.Build(run)!;

        var disagreement = Assert.Single(
            report.Diagnostics, d => d.Type == CalibrationDiagnosticType.LocationDisagreement);
        Assert.Equal("SEC-abc", disagreement.FindingId);
        Assert.Contains("src/payments/paymentclient.cs:42",
            disagreement.LocationsByProvider.Single(p => p.Provider == "Claude").NormalizedLocations);
        Assert.Contains("src/payments/paymentservice.cs:42",
            disagreement.LocationsByProvider.Single(p => p.Provider == "OpenAI").NormalizedLocations);
        // Same type ⇒ only the location signal fires.
        Assert.DoesNotContain(report.Diagnostics, d => d.Type == CalibrationDiagnosticType.ObservationTypeDisagreement);
    }

    [Fact]
    public void Same_file_with_different_explicit_lines_is_a_location_disagreement()
    {
        var run = ComparableObservationRun(
            observations:
            [
                Obs("obs-c1", ObservationTypes.HardcodedSecret, "Claude", "src/Payments/PaymentClient.cs", 42),
                Obs("obs-o1", ObservationTypes.HardcodedSecret, "OpenAI", "src/Payments/PaymentClient.cs", 51)
            ],
            rawFindings:
            [
                RawWithObs("c1", "Hardcoded secret", ["obs-c1"], "Claude"),
                RawWithObs("o1", "Hardcoded secret", ["obs-o1"], "OpenAI")
            ],
            findings: [SharedFinding("SEC-abc", "Hardcoded secret", ["c1", "o1"])]);

        var report = CalibrationDiagnosticsBuilder.Build(run)!;

        var disagreement = Assert.Single(
            report.Diagnostics, d => d.Type == CalibrationDiagnosticType.LocationDisagreement);
        Assert.Equal("SEC-abc", disagreement.FindingId);
        Assert.Contains("src/payments/paymentclient.cs:42",
            disagreement.LocationsByProvider.Single(p => p.Provider == "Claude").NormalizedLocations);
        Assert.Contains("src/payments/paymentclient.cs:51",
            disagreement.LocationsByProvider.Single(p => p.Provider == "OpenAI").NormalizedLocations);
    }

    [Fact]
    public void Same_normalized_location_produces_no_signal()
    {
        var run = ComparableObservationRun(
            observations:
            [
                Obs("obs-c1", ObservationTypes.HardcodedSecret, "Claude", "src/Payments/PaymentClient.cs", 42),
                Obs("obs-o1", ObservationTypes.HardcodedSecret, "OpenAI", "src/Payments/PaymentClient.cs", 42)
            ],
            rawFindings:
            [
                RawWithObs("c1", "Hardcoded secret", ["obs-c1"], "Claude"),
                RawWithObs("o1", "Hardcoded secret", ["obs-o1"], "OpenAI")
            ],
            findings: [SharedFinding("SEC-abc", "Hardcoded secret", ["c1", "o1"])]);

        var report = CalibrationDiagnosticsBuilder.Build(run)!;

        Assert.DoesNotContain(report.Diagnostics, d => d.Type == CalibrationDiagnosticType.LocationDisagreement);
    }

    [Fact]
    public void A_missing_location_does_not_fabricate_a_disagreement()
    {
        var run = ComparableObservationRun(
            observations:
            [
                Obs("obs-c1", ObservationTypes.HardcodedSecret, "Claude", "src/Payments/PaymentClient.cs", 42),
                Obs("obs-o1", ObservationTypes.HardcodedSecret, "OpenAI") // no location
            ],
            rawFindings:
            [
                RawWithObs("c1", "Hardcoded secret", ["obs-c1"], "Claude"),
                RawWithObs("o1", "Hardcoded secret", ["obs-o1"], "OpenAI")
            ],
            findings: [SharedFinding("SEC-abc", "Hardcoded secret", ["c1", "o1"])]);

        var report = CalibrationDiagnosticsBuilder.Build(run)!;

        Assert.DoesNotContain(report.Diagnostics, d => d.Type == CalibrationDiagnosticType.LocationDisagreement);
    }

    [Fact]
    public void A_provider_exclusive_finding_gets_neither_new_disagreement_diagnostic()
    {
        var run = ComparableObservationRun(
            observations: [Obs("obs-c1", ObservationTypes.MissingTimeout, "Claude", "src/Payments.cs", 42)],
            rawFindings: [RawWithObs("c1", "Claude only", ["obs-c1"], "Claude")],
            findings:
            [
                new Finding
                {
                    Id = "SEC-only", Title = "Claude only", Category = FindingCategory.Security,
                    Severity = FindingSeverity.High, Confidence = FindingConfidence.High,
                    EvidenceProvider = "Claude", SupportingProviders = ["Claude"],
                    AgreementCount = 1, SeverityRange = "High", Status = FindingStatus.New,
                    IsConsolidated = false, SupportingFindingIds = ["c1"]
                }
            ]);

        var report = CalibrationDiagnosticsBuilder.Build(run)!;

        Assert.DoesNotContain(report.Diagnostics, d => d.Type is CalibrationDiagnosticType.ObservationTypeDisagreement or CalibrationDiagnosticType.LocationDisagreement);
        // The M12.3 exclusive signal still surfaces.
        Assert.Contains(report.Diagnostics, d => d.Type == CalibrationDiagnosticType.ExclusiveFinding && d.FindingId == "SEC-only");
    }

    // ── M12.4 — Comparable only ──────────────────────────────────────────────

    [Fact]
    public void Non_comparable_disciplines_emit_no_observation_level_diagnostics()
    {
        var run = ObservationRun(
            [
                Rec("Claude", FindingCategory.Security, fingerprint: "CTX-aaaa"),
                Rec("OpenAI", FindingCategory.Security, fingerprint: "CTX-bbbb")
            ],
            observations:
            [
                Obs("obs-c1", ObservationTypes.MissingTimeout, "Claude", "src/Payments/PaymentClient.cs", 42),
                Obs("obs-o1", "ResilienceRisk", "OpenAI", "src/Payments/PaymentService.cs", 51)
            ],
            rawFindings:
            [
                RawWithObs("c1", "Missing timeout", ["obs-c1"], "Claude"),
                RawWithObs("o1", "Missing timeout", ["obs-o1"], "OpenAI")
            ],
            findings: [SharedFinding("REL-abc123", "Missing timeout", ["c1", "o1"])]);

        var report = CalibrationDiagnosticsBuilder.Build(run)!;

        Assert.DoesNotContain(report.Diagnostics,
            d => d.Type is CalibrationDiagnosticType.ObservationTypeDisagreement or CalibrationDiagnosticType.LocationDisagreement);
        Assert.Contains(report.Limitations, l => l.Contains("NonComparable", StringComparison.Ordinal));
    }

    [Fact]
    public void Incomplete_disciplines_emit_no_observation_level_diagnostics()
    {
        var run = ObservationRun(
            [
                Rec("Claude", FindingCategory.Security, success: true),
                Rec("OpenAI", FindingCategory.Security, success: false, errorCategory: "Authentication")
            ],
            observations:
            [
                Obs("obs-c1", ObservationTypes.MissingTimeout, "Claude", "src/Payments/PaymentClient.cs", 42),
                Obs("obs-o1", "ResilienceRisk", "OpenAI", "src/Payments/PaymentService.cs", 51)
            ],
            rawFindings:
            [
                RawWithObs("c1", "Missing timeout", ["obs-c1"], "Claude"),
                RawWithObs("o1", "Missing timeout", ["obs-o1"], "OpenAI")
            ],
            findings: [SharedFinding("REL-abc123", "Missing timeout", ["c1", "o1"])]);

        var report = CalibrationDiagnosticsBuilder.Build(run)!;

        Assert.DoesNotContain(report.Diagnostics,
            d => d.Type is CalibrationDiagnosticType.ObservationTypeDisagreement or CalibrationDiagnosticType.LocationDisagreement);
        Assert.Contains(report.Limitations, l => l.Contains("Incomplete", StringComparison.Ordinal));
    }

    // ── M12.4 — determinism ──────────────────────────────────────────────────

    [Fact]
    public void The_report_stays_deterministic_regardless_of_observation_ordering()
    {
        var observationsA = new[]
        {
            Obs("obs-c1", ObservationTypes.MissingTimeout, "Claude", "src/Payments.cs", 42),
            Obs("obs-o1", "ResilienceRisk", "OpenAI", "src/Payments/PaymentService.cs", 51)
        };
        var raw = new[]
        {
            RawWithObs("c1", "Missing timeout", ["obs-c1"], "Claude"),
            RawWithObs("o1", "Missing timeout", ["obs-o1"], "OpenAI")
        };

        var runA = ComparableObservationRun(
            observationsA, raw, [SharedFinding("REL-abc123", "Missing timeout", ["c1", "o1"])]);
        var runB = ComparableObservationRun(
            observationsA.Reverse().ToArray(), raw.Reverse().ToArray(),
            [SharedFinding("REL-abc123", "Missing timeout", ["o1", "c1"])]);

        Assert.Equal(
            Serialize(CalibrationDiagnosticsBuilder.Build(runA)!),
            Serialize(CalibrationDiagnosticsBuilder.Build(runB)!));
    }

    // ── M12.4 — artifact + external boundary ─────────────────────────────────

    [Fact]
    public async Task Calibration_diagnostics_json_serializes_the_new_signals()
    {
        var run = ComparableObservationRun(
            observations:
            [
                Obs("obs-c1", ObservationTypes.MissingTimeout, "Claude", "src/Payments/PaymentClient.cs", 42),
                Obs("obs-o1", "ResilienceRisk", "OpenAI", "src/Payments/PaymentService.cs", 51)
            ],
            rawFindings:
            [
                RawWithObs("c1", "Missing timeout", ["obs-c1"], "Claude"),
                RawWithObs("o1", "Missing timeout", ["obs-o1"], "OpenAI")
            ],
            findings: [SharedFinding("REL-abc123", "Missing timeout", ["c1", "o1"])]);
        run = run with { CalibrationDiagnostics = CalibrationDiagnosticsBuilder.Build(run) };

        var json = new JsonReportGenerator().RenderCalibrationDiagnostics(run);

        Assert.Contains("observationTypeDisagreement", json);
        Assert.Contains("locationDisagreement", json);
        Assert.Contains("REL-abc123", json);
        Assert.Contains("obs-c1", json);
        Assert.Contains("ResilienceRisk", json);
        Assert.Contains("src/payments/paymentclient.cs:42", json);
    }

    [Fact]
    public async Task The_package_never_leaks_the_new_observation_diagnostics()
    {
        var run = ComparableObservationRun(
            observations:
            [
                Obs("obs-c1", ObservationTypes.MissingTimeout, "Claude", "src/Payments/PaymentClient.cs", 42),
                Obs("obs-o1", "ResilienceRisk", "OpenAI", "src/Payments/PaymentService.cs", 51)
            ],
            rawFindings:
            [
                RawWithObs("c1", "Missing timeout", ["obs-c1"], "Claude"),
                RawWithObs("o1", "Missing timeout", ["obs-o1"], "OpenAI")
            ],
            findings: [SharedFinding("REL-abc123", "Missing timeout", ["c1", "o1"])]);
        run = run with { CalibrationDiagnostics = CalibrationDiagnosticsBuilder.Build(run) };

        var package = new EngineeringReviewPackageBuilder().Build(run);
        var packageJson = JsonSerializer.Serialize(package, CouncilJson.Options);

        Assert.DoesNotContain("observationTypeDisagreement", packageJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("locationDisagreement", packageJson, StringComparison.OrdinalIgnoreCase);

        var dto = JsonSerializer.Deserialize<PackageContract>(packageJson, CouncilJson.Options)!;
        Assert.Equal("1.1", dto.SchemaVersion);
    }

    // ── M12.4 — acceptance fixture ───────────────────────────────────────────

    [Fact]
    public void Acceptance_fixture_produces_exactly_the_two_new_signals()
    {
        // Providers Claude + OpenAI · Discipline Security · Status Comparable.
        // Finding A (REL-abc123): shared, different ObservationTypes, same location.
        // Finding B (SEC-xyz789): shared, different locations, same ObservationType.
        // Finding C (REL-def456): shared, same type and location.
        var run = ComparableObservationRun(
            observations:
            [
                Obs("obs-a-c", ObservationTypes.MissingTimeout, "Claude", "src/Payments/PaymentClient.cs", 42),
                Obs("obs-a-o", "ResilienceRisk", "OpenAI", "src/Payments/PaymentClient.cs", 42),
                Obs("obs-b-c", ObservationTypes.HardcodedSecret, "Claude", "src/Payments/PaymentClient.cs", 42),
                Obs("obs-b-o", ObservationTypes.HardcodedSecret, "OpenAI", "src/Payments/PaymentService.cs", 42),
                Obs("obs-c-c", ObservationTypes.MissingRetryPolicy, "Claude", "src/Query.cs", 9),
                Obs("obs-c-o", ObservationTypes.MissingRetryPolicy, "OpenAI", "src/Query.cs", 9)
            ],
            rawFindings:
            [
                RawWithObs("a-c", "Missing timeout", ["obs-a-c"], "Claude"),
                RawWithObs("a-o", "Missing timeout", ["obs-a-o"], "OpenAI"),
                RawWithObs("b-c", "Hardcoded secret", ["obs-b-c"], "Claude"),
                RawWithObs("b-o", "Hardcoded secret", ["obs-b-o"], "OpenAI"),
                RawWithObs("c-c", "Missing cancellation", ["obs-c-c"], "Claude"),
                RawWithObs("c-o", "Missing cancellation", ["obs-c-o"], "OpenAI")
            ],
            findings:
            [
                SharedFinding("REL-abc123", "Missing timeout", ["a-c", "a-o"]),
                SharedFinding("SEC-xyz789", "Hardcoded secret", ["b-c", "b-o"]),
                SharedFinding("REL-def456", "Missing cancellation", ["c-c", "c-o"])
            ]);

        var report = CalibrationDiagnosticsBuilder.Build(run)!;

        Assert.Equal(2, report.Diagnostics.Count(
            d => d.Type is CalibrationDiagnosticType.ObservationTypeDisagreement or CalibrationDiagnosticType.LocationDisagreement));

        Assert.Equal("REL-abc123", Assert.Single(
            report.Diagnostics, d => d.Type == CalibrationDiagnosticType.ObservationTypeDisagreement).FindingId);

        Assert.Equal("SEC-xyz789", Assert.Single(
            report.Diagnostics, d => d.Type == CalibrationDiagnosticType.LocationDisagreement).FindingId);

        Assert.DoesNotContain(report.Diagnostics,
            d => d.FindingId == "REL-def456"
                && d.Type is CalibrationDiagnosticType.ObservationTypeDisagreement or CalibrationDiagnosticType.LocationDisagreement);
    }

    // ── NonComparable / Incomplete = limitations only ─────────────────────────

    [Fact]
    public void Non_comparable_disciplines_record_a_limitation_and_no_diagnostics()
    {
        var run = Run(
            [Rec("Claude", FindingCategory.Security, "CTX-aaaa"), Rec("OpenAI", FindingCategory.Security, "CTX-bbbb")],
            findings: [
                Consolidated("F-1", "Only Claude", ["Claude"]),
                Consolidated("F-2", "Only OpenAI", ["OpenAI"])
            ]);

        var report = CalibrationDiagnosticsBuilder.Build(run)!;

        Assert.Empty(report.Diagnostics);
        Assert.Contains(report.Limitations, l => l.Contains("NonComparable", StringComparison.Ordinal));
        Assert.Contains(report.Limitations, l => l.Contains("no finding disagreement diagnostics generated", StringComparison.Ordinal));
    }

    [Fact]
    public void Incomplete_disciplines_record_a_limitation_and_no_diagnostics()
    {
        var run = Run(
            [
                Rec("Claude", FindingCategory.Security, success: true),
                Rec("OpenAI", FindingCategory.Security, success: false, errorCategory: "Authentication")
            ],
            findings: [Consolidated("F-1", "Claude only", ["Claude"])]);

        var report = CalibrationDiagnosticsBuilder.Build(run)!;

        Assert.Empty(report.Diagnostics);
        Assert.Contains(report.Limitations, l => l.Contains("Incomplete", StringComparison.Ordinal));
        Assert.Contains(report.Limitations, l => l.Contains("provider executions failed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_single_provider_run_produces_no_calibration_report()
    {
        var run = Run([Rec("Claude", FindingCategory.Security)]);

        Assert.Null(CalibrationDiagnosticsBuilder.Build(run));
    }

    // ── Determinism ───────────────────────────────────────────────────────────

    [Fact]
    public void The_report_is_deterministic_regardless_of_enumeration_order()
    {
        var raw = new[]
        {
            Raw("c1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "Claude", line: 3, severity: FindingSeverity.High),
            Raw("o1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "OpenAI", line: 4, severity: FindingSeverity.Medium),
            Raw("c2", "SQL injection", "CWE-89", "src/Query.cs", "Claude", line: 9),
            Raw("o2", "Unsafe deserialization", "CWE-502", "src/Deser.cs", "OpenAI", line: 5)
        };

        var runA = Run(
            [Rec("OpenAI", FindingCategory.Security), Rec("Claude", FindingCategory.Security)],
            rawFindings: raw, findings: Reconcile(raw));
        runA = runA with { ProviderComparison = ProviderComparisonBuilder.Build(runA) };

        var runB = Run(
            [Rec("Claude", FindingCategory.Security), Rec("OpenAI", FindingCategory.Security)],
            rawFindings: raw.Reverse().ToArray(), findings: Reconcile(raw.Reverse().ToArray()));
        runB = runB with { ProviderComparison = ProviderComparisonBuilder.Build(runB) };

        var jsonA = Serialize(CalibrationDiagnosticsBuilder.Build(runA)!);
        var jsonB = Serialize(CalibrationDiagnosticsBuilder.Build(runB)!);

        Assert.Equal(jsonA, jsonB);
    }

    // ── Artifact boundary + no extra provider calls ───────────────────────────

    [Fact]
    public async Task Dual_provider_run_writes_calibration_diagnostics_without_extra_provider_calls()
    {
        var claudeClient = new ScriptedLlmClient("claude")
            .Returns(SecurityJson())
            .Returns(ReliabilityJson());
        var openaiClient = new ScriptedLlmClient("openai")
            .Returns(SecurityJson())
            .Returns(ReliabilityJson());
        var pipeline = BuildPipelineAsync([Claude(claudeClient), OpenAi(openaiClient)]);

        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = _repo, ProviderName = "Claude,OpenAI" });

        Assert.Equal(2, claudeClient.CallCount); // acquisition only (Security + Reliability)
        Assert.Equal(2, openaiClient.CallCount);

        // Building and rendering the calibration report must never invoke a provider.
        var diagnosticsPath = Path.Combine(result.OutputDirectory, "calibration-diagnostics.json");
        Assert.True(File.Exists(diagnosticsPath), "calibration-diagnostics.json should be written for a dual-provider run.");
        var report = JsonSerializer.Deserialize<CalibrationDiagnosticsReport>(
            await File.ReadAllTextAsync(diagnosticsPath), CouncilJson.Options)!;
        Assert.Equal(result.Run.RunId, report.RunId);
        Assert.Equal(2, claudeClient.CallCount);
        Assert.Equal(2, openaiClient.CallCount);
        Assert.DoesNotContain(KeyValue, await File.ReadAllTextAsync(diagnosticsPath));
    }

    [Fact]
    public async Task The_external_consumer_contract_remains_green()
    {
        var run = ComparableRun(
            Raw("c1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "Claude", line: 3),
            Raw("o1", "Hardcoded API key", "CWE-798", "src/Payments.cs", "OpenAI", line: 4));
        run = run with { CalibrationDiagnostics = CalibrationDiagnosticsBuilder.Build(run) };

        var package = new EngineeringReviewPackageBuilder().Build(run);
        var packageJson = JsonSerializer.Serialize(package, CouncilJson.Options);

        Assert.DoesNotContain("calibration", packageJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exclusiveFinding", packageJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("severityDisagreement", packageJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("observationTypeDisagreement", packageJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("locationDisagreement", packageJson, StringComparison.OrdinalIgnoreCase);

        var dto = JsonSerializer.Deserialize<PackageContract>(packageJson, CouncilJson.Options)!;
        Assert.Equal("1.1", dto.SchemaVersion);
    }

    [Fact]
    public async Task A_single_provider_run_writes_no_calibration_artifact()
    {
        var client = new ScriptedLlmClient("claude")
            .Returns(SecurityJson())
            .Returns(ReliabilityJson());
        var pipeline = BuildPipelineAsync([Claude(client)]);

        var result = await pipeline.RunAsync(new AnalysisRequest { TargetPath = _repo, ProviderName = "Claude" });

        Assert.False(File.Exists(Path.Combine(result.OutputDirectory, "calibration-diagnostics.json")));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ProviderExecutionRecord Rec(
        string provider,
        FindingCategory? discipline = FindingCategory.Security,
        string fingerprint = "CTX-abc123",
        bool success = true,
        string? errorCategory = null)
        => new()
        {
            RunId = "run-test",
            ProviderName = provider,
            ProviderId = provider,
            ProviderVersion = "test-model",
            ProviderType = EvidenceProviderType.LLM,
            RequestedDiscipline = discipline,
            Success = success,
            ErrorCategory = errorCategory,
            ContextFileCount = 1,
            ContextCharacterCount = 100,
            ContextFingerprint = fingerprint,
            Duration = TimeSpan.FromMilliseconds(100)
        };

    private static Finding Raw(
        string id, string title, string rule, string file, string provider,
        FindingCategory category = FindingCategory.Security, int line = 1,
        FindingSeverity severity = FindingSeverity.High,
        FindingConfidence confidence = FindingConfidence.High)
        => new()
        {
            Id = id,
            Title = title,
            Category = category,
            Severity = severity,
            Confidence = confidence,
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
            SeverityRange = "High",
            Status = FindingStatus.Merged
        };

    private static IReadOnlyList<Finding> Reconcile(params Finding[] raw)
        => new RuleBasedFindingReconciler().Reconcile(raw, []).ConsolidatedFindings;

    private static AnalysisRun Run(
        IReadOnlyList<ProviderExecutionRecord> records,
        IReadOnlyList<Finding>? findings = null,
        IReadOnlyList<Finding>? rawFindings = null)
    {
        var run = new AnalysisRun
        {
            RunId = "run-test",
            TargetPath = "/repo",
            SolutionName = "Repo",
            Branch = "main",
            Commit = "abc123",
            ProviderExecution = new ProviderExecutionReport { Records = records },
            Findings = findings ?? [],
            RawFindings = rawFindings ?? []
        };
        return run with { ProviderComparison = ProviderComparisonBuilder.Build(run) };
    }

    private static AnalysisRun ComparableRun(params Finding[] raw)
    {
        var run = new AnalysisRun
        {
            RunId = "run-test",
            TargetPath = "/repo",
            SolutionName = "Repo",
            Branch = "main",
            Commit = "abc123",
            ProviderExecution = new ProviderExecutionReport
            {
                Records =
                [
                    Rec("Claude", FindingCategory.Security),
                    Rec("OpenAI", FindingCategory.Security)
                ]
            },
            Findings = Reconcile(raw),
            RawFindings = raw
        };
        return run with { ProviderComparison = ProviderComparisonBuilder.Build(run) };
    }

    // ── M12.4 helpers — observation-level runs ───────────────────────────────

    private static AnalysisRun ComparableObservationRun(
        IReadOnlyList<EngineeringObservation> observations,
        IReadOnlyList<Finding> rawFindings,
        IReadOnlyList<Finding> findings)
        => ObservationRun(
            [Rec("Claude", FindingCategory.Security), Rec("OpenAI", FindingCategory.Security)],
            observations, rawFindings, findings);

    private static AnalysisRun ObservationRun(
        IReadOnlyList<ProviderExecutionRecord> records,
        IReadOnlyList<EngineeringObservation> observations,
        IReadOnlyList<Finding> rawFindings,
        IReadOnlyList<Finding> findings,
        IReadOnlyList<Evidence>? evidence = null)
    {
        var run = new AnalysisRun
        {
            RunId = "run-test",
            TargetPath = "/repo",
            SolutionName = "Repo",
            Branch = "main",
            Commit = "abc123",
            Evidence = evidence ?? BuildEvidence(observations),
            Observations = observations,
            RawFindings = rawFindings,
            Findings = findings,
            ProviderExecution = new ProviderExecutionReport { Records = records.ToList() }
        };
        return run with { ProviderComparison = ProviderComparisonBuilder.Build(run) };
    }

    private static IReadOnlyList<Evidence> BuildEvidence(IReadOnlyList<EngineeringObservation> observations)
        => observations
            .Where(o => !string.IsNullOrWhiteSpace(o.SourceProvider))
            .Select(o => new Evidence
            {
                Id = o.SourceEvidenceId,
                ProviderName = o.SourceProvider,
                ProviderId = o.SourceProvider.ToLowerInvariant(),
                ProviderType = EvidenceProviderType.LLM,
                RequestedDiscipline = FindingCategory.Security,
                AcquisitionScope = EvidenceAcquisitionScope.Discipline,
                ContextFingerprint = "CTX-abc123",
                RawResponse = "{}"
            })
            .GroupBy(e => e.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

    private static EngineeringObservation Obs(
        string id,
        string type,
        string provider,
        string? file = null,
        int? line = null,
        string? sourceProvider = null,
        string? evidenceId = null)
        => new()
        {
            Id = id,
            ObservationType = type,
            Discipline = FindingCategory.Security,
            SourceProvider = sourceProvider ?? provider,
            SourceEvidenceId = evidenceId ?? provider.ToLowerInvariant() + "-evidence",
            FileReferences = file is null
                ? []
                : [new FileReference { Path = file, StartLine = line }],
            RequestedDiscipline = FindingCategory.Security
        };

    private static Finding RawWithObs(
        string id, string title, IReadOnlyList<string> observationIds, string provider,
        FindingCategory category = FindingCategory.Security)
        => new()
        {
            Id = id,
            Title = title,
            Category = category,
            Severity = FindingSeverity.High,
            Confidence = FindingConfidence.High,
            EvidenceProvider = provider,
            SupportingProviders = [provider],
            ObservationIds = observationIds,
            SourceRules = [id],
            Status = FindingStatus.New
        };

    private static Finding SharedFinding(string id, string title, IReadOnlyList<string> supportingIds)
        => new()
        {
            Id = id,
            Title = title,
            Category = FindingCategory.Security,
            Severity = FindingSeverity.High,
            Confidence = FindingConfidence.High,
            EvidenceProvider = "Claude",
            SupportingProviders = ["Claude", "OpenAI"],
            AgreementCount = 2,
            SeverityRange = "High",
            Status = FindingStatus.Merged,
            IsConsolidated = true,
            SupportingFindingIds = supportingIds
        };

    private static string Serialize(CalibrationDiagnosticsReport report)
        => JsonSerializer.Serialize(report with { GeneratedAt = DateTimeOffset.MinValue }, CouncilJson.Options);

    private static string SecurityJson() => """
        {
          "schemaVersion": "1.0",
          "discipline": "Security",
          "observations": [
            { "type": "HardcodedSecret", "discipline": "Security", "title": "Hardcoded secret",
              "description": "A credential is embedded in source.", "severity": "High", "confidence": "High",
              "ruleId": "CWE-798", "fileReferences": [ { "path": "src/Payments.cs", "startLine": 3 } ] }
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

    private ClaudeEvidenceProvider Claude(ScriptedLlmClient client)
    {
        Environment.SetEnvironmentVariable(KeyVariable, KeyValue);
        return new ClaudeEvidenceProvider(client, new ClaudeProviderOptions
        {
            Enabled = true, ApiKeyEnvironmentVariable = KeyVariable, Model = "claude-test-model",
            MaxRetries = 0, TimeoutSeconds = 5, EnableStructuredRepair = false
        }, new DisciplineEvidencePromptBuilder());
    }

    private OpenAiEvidenceProvider OpenAi(ScriptedLlmClient client)
    {
        Environment.SetEnvironmentVariable(KeyVariable, KeyValue);
        return new OpenAiEvidenceProvider(client, new OpenAiProviderOptions
        {
            Enabled = true, ApiKeyEnvironmentVariable = KeyVariable, Model = "openai-test-model",
            MaxRetries = 0, TimeoutSeconds = 5, EnableStructuredRepair = false
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
