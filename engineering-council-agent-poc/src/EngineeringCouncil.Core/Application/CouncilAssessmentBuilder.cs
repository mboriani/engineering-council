using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Application;

/// <summary>
/// Deterministic Council assessment classifier (Milestone 014.2). Assigns every
/// consolidated finding a <see cref="ReconciliationAssessmentType"/> using ONLY
/// existing authoritative data already produced by the run:
///
/// <list type="bullet">
///   <item>the reconciler's <see cref="Finding.SupportingProviders"/> (distinct-provider counting — never recomputed),</item>
///   <item>the reconciler's <see cref="Finding.SeverityRange"/> (severity disagreement = an en‑dash range),</item>
///   <item>the reconciler's <see cref="Finding.ContradictionReasons"/> (location split; explicit contradictions),</item>
///   <item>the M12.3/M12.4 <see cref="CalibrationDiagnosticsReport"/> (observed severity /
///         observation-type / location signals keyed by finding id, when present).</item>
/// </list>
///
/// No LLM, no new merge/heuristic, no voting, no provider ranking/weighting. The
/// classification NEVER infers a conflict from different severity, different location,
/// different observation type, or another provider not reporting the finding — only an
/// explicit, unexplained contradiction reason qualifies for
/// <see cref="ReconciliationAssessmentType.PotentialConflict"/> (so zero is a valid run outcome).
/// </summary>
public static class CouncilAssessmentBuilder
{
    /// <summary>The reconciler's severity-range separator (ADR-011, also used by M12.3/CalibrationDiagnosticsBuilder).</summary>
    private const char SeverityRangeSeparator = '\u2013'; // en dash "–"

    /// <summary>Reasons the reconciler emits that describe a DIFFERENCE, never a conflict.</summary>
    private static readonly string[] DifferenceReasonMarkers =
        ["severity", "file", "location"];

    /// <summary>
    /// Returns the findings with <see cref="Finding.CouncilAssessment"/> stamped on each
    /// consolidated finding, preserving the input order and the findings themselves
    /// (a pure additive projection). Raw findings carry no assessment.
    /// </summary>
    public static IReadOnlyList<Finding> Apply(
        IReadOnlyList<Finding> findings,
        CalibrationDiagnosticsReport? diagnostics)
    {
        var signalsByFindingId = IndexSignals(diagnostics);

        return findings
            .Select(f => f with { CouncilAssessment = Assess(f, signalsByFindingId) })
            .ToList();
    }

    /// <summary>
    /// The ONE authoritative aggregation of <see cref="Finding.CouncilAssessment"/>
    /// across a run's (already-assessed) findings (Milestone 014.3). Purely counts —
    /// no new classification, no recomputation of any per-finding assessment. A
    /// finding without an assessment (e.g. a raw finding, or one from a caller that
    /// skipped <see cref="Apply"/>) is not counted under any type.
    /// </summary>
    public static CouncilAssessmentSummary Summarize(IReadOnlyList<Finding> findings)
    {
        var counts = findings
            .Where(f => f.CouncilAssessment is not null)
            .GroupBy(f => f.CouncilAssessment!.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        return new CouncilAssessmentSummary
        {
            SingleSourceCount = counts.GetValueOrDefault(ReconciliationAssessmentType.SingleSource),
            StrongAgreementCount = counts.GetValueOrDefault(ReconciliationAssessmentType.StrongAgreement),
            AgreementWithDifferencesCount = counts.GetValueOrDefault(ReconciliationAssessmentType.AgreementWithDifferences),
            PotentialConflictCount = counts.GetValueOrDefault(ReconciliationAssessmentType.PotentialConflict)
        };
    }

    /// <summary>Deterministic classification of a single consolidated finding.</summary>
    public static ReconciliationAssessment Assess(
        Finding finding,
        CalibrationDiagnosticsReport? diagnostics)
    {
        var signalsByFindingId = IndexSignals(diagnostics);
        return Assess(finding, signalsByFindingId);
    }

    private static ReconciliationAssessment Assess(
        Finding finding,
        IReadOnlyDictionary<string, FindingSignals> signalsByFindingId)
    {
        var providers = finding.SupportingProviders
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var providerCount = providers.Count > 0 ? providers.Count : finding.AgreementCount;
        if (providerCount <= 1)
        {
            return new ReconciliationAssessment
            {
                Type = ReconciliationAssessmentType.SingleSource,
                SupportingProviders = providers,
                AgreementCount = Math.Max(1, providerCount)
            };
        }

        var signals = signalsByFindingId.TryGetValue(finding.Id, out var existing)
            ? existing
            : FindingSignals.None;

        var differences = new List<ReconciliationAssessmentDisagreement>();
        if (SeverityDisagreement(finding, signals)) differences.Add(ReconciliationAssessmentDisagreement.Severity);
        if (ObservationTypeDisagreement(finding, signals)) differences.Add(ReconciliationAssessmentDisagreement.ObservationType);
        if (LocationDisagreement(finding, signals)) differences.Add(ReconciliationAssessmentDisagreement.Location);

        // Explicit contradiction = a recorded contradiction reason that is NOT explainable
        // as a severity/location difference (those are already counted above as differences).
        var explicitContradictions = finding.ContradictionReasons
            .Where(r => !IsDifferenceReason(r))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

        var type = explicitContradictions.Count > 0
            ? ReconciliationAssessmentType.PotentialConflict
            : differences.Count > 0
                ? ReconciliationAssessmentType.AgreementWithDifferences
                : ReconciliationAssessmentType.StrongAgreement;

        return new ReconciliationAssessment
        {
            Type = type,
            SupportingProviders = providers,
            AgreementCount = providerCount,
            Differences = differences.OrderBy(d => d.ToString(), StringComparer.Ordinal).ToList(),
            ContradictionReasons = explicitContradictions
        };
    }

    // ── Existing-signal sources ─────────────────────────────────────────────

    private static bool SeverityDisagreement(Finding finding, FindingSignals signals)
        => signals.HasSeverityDisagreement
           || finding.SeverityRange.IndexOf(SeverityRangeSeparator) >= 0;

    private static bool ObservationTypeDisagreement(Finding finding, FindingSignals signals)
        => signals.HasObservationTypeDisagreement;

    private static bool LocationDisagreement(Finding finding, FindingSignals signals)
        => signals.HasLocationDisagreement
           || finding.ContradictionReasons.Any(r =>
               r.Contains("file", StringComparison.OrdinalIgnoreCase)
               || r.Contains("location", StringComparison.OrdinalIgnoreCase));

    /// <summary>True when a contradiction reason describes a severity/location DIFFERENCE rather than a conflict.</summary>
    private static bool IsDifferenceReason(string reason)
        => DifferenceReasonMarkers.Any(marker =>
            reason.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyDictionary<string, FindingSignals> IndexSignals(CalibrationDiagnosticsReport? diagnostics)
    {
        if (diagnostics is null) return EmptyLookup;

        return diagnostics.Diagnostics
            .Where(d => !string.IsNullOrWhiteSpace(d.FindingId))
            .GroupBy(d => d.FindingId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => new FindingSignals(
                    g.Any(d => d.Type == CalibrationDiagnosticType.SeverityDisagreement),
                    g.Any(d => d.Type == CalibrationDiagnosticType.ObservationTypeDisagreement),
                    g.Any(d => d.Type == CalibrationDiagnosticType.LocationDisagreement)),
                StringComparer.Ordinal);
    }

    private static readonly IReadOnlyDictionary<string, FindingSignals> EmptyLookup =
        new Dictionary<string, FindingSignals>(StringComparer.Ordinal);

    private readonly record struct FindingSignals(
        bool HasSeverityDisagreement,
        bool HasObservationTypeDisagreement,
        bool HasLocationDisagreement)
    {
        public static readonly FindingSignals None = new(false, false, false);
    }
}