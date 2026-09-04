using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Application;

/// <summary>
/// Builds the INTERNAL calibration-diagnostics report (M12.3, extended additively
/// by M12.4) from data a completed run already produced: the
/// <see cref="ProviderComparisonReport"/> (M12.2) plus the reconciled
/// <see cref="AnalysisRun.Findings"/>/<see cref="AnalysisRun.RawFindings"/> and the
/// normalized <see cref="AnalysisRun.Observations"/>/<see cref="AnalysisRun.Evidence"/>.
/// It never calls a provider, never rescans, never reinterprets responses, never does
/// semantic matching, and never changes reconciliation or context. The existing
/// reconciliation is authoritative. Diagnostics are emitted ONLY for disciplines whose
/// comparison is <see cref="ProviderComparisonStatus.Comparable"/>; NonComparable and
/// Incomplete disciplines are recorded as limitations only. M12.4 adds two observation-
/// level signals (ObservationTypeDisagreement, LocationDisagreement) whose provider
/// attribution comes exclusively from existing provenance
/// (Finding → SupportingFindingIds → Raw Finding → ObservationIds →
/// EngineeringObservation → Evidence/provider); un-attributable observations are
/// skipped, never guessed. Descriptive — no ranking, scoring, or weighting of providers.
/// </summary>
public static class CalibrationDiagnosticsBuilder
{
    /// <summary>The reconciler's severity-range separator (also used in ADR-011 rationale).</summary>
    private const char SeverityRangeSeparator = '\u2013'; // en dash "–"

    public static CalibrationDiagnosticsReport? Build(AnalysisRun run)
    {
        if (run.ProviderComparison is not { } comparison)
            return null;

        var criteria = new CalibrationDiagnosticCriteria();
        var diagnostics = new List<FindingDiagnostic>();
        var limitations = new List<string>();

        // Indexes over the run's OWN provenance — never re-acquired, never re-interpreted.
        var observationsById = run.Observations
            .GroupBy(o => o.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var evidenceById = run.Evidence
            .GroupBy(e => e.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var discipline in comparison.DisciplineComparisons.OrderBy(d => d.Discipline))
        {
            if (discipline.Status != ProviderComparisonStatus.Comparable)
            {
                limitations.Add(BuildLimitation(discipline));
                continue;
            }

            var providers = discipline.Providers;

            // Shared consolidated findings (supported by ≥2 compared providers) — the
            // only findings M12.4's observation-level signals can describe.
            var sharedFindings = run.Findings
                .Where(f => f.Category == discipline.Discipline && SupportingCompared(f, providers).Count >= 2)
                .OrderBy(f => f.Id, StringComparer.Ordinal)
                .ToList();

            // 1. Exclusive findings — only findings supported by exactly one compared
            //    provider (reuses the M12.2 FindingMetrics exclusivity definition, so the
            //    two artifacts can never disagree).
            foreach (var findingMetric in discipline.FindingMetrics.OrderBy(f => f.Provider, StringComparer.Ordinal))
            {
                foreach (var id in findingMetric.ExclusiveFindingIds.OrderBy(id => id, StringComparer.Ordinal))
                {
                    var finding = run.Findings.FirstOrDefault(f => f.Id == id);
                    if (finding is null) continue; // defensive; reconciler ids always resolve

                    diagnostics.Add(new FindingDiagnostic
                    {
                        Type = CalibrationDiagnosticType.ExclusiveFinding,
                        Discipline = discipline.Discipline,
                        Provider = findingMetric.Provider,
                        FindingId = finding.Id,
                        Title = finding.Title,
                        Severity = finding.Severity,
                        Confidence = finding.Confidence,
                        Files = SortedDistinctFiles(finding)
                    });
                }
            }

            // 2. Severity disagreement — a shared (≥2 compared providers) consolidated
            //    finding whose reconciled severity range shows the sources disagreed.
            //    Attribution comes from the run's own raw findings (existing provenance).
            foreach (var finding in sharedFindings)
            {
                if (finding.SeverityRange.IndexOf(SeverityRangeSeparator) < 0)
                    continue; // single value ⇒ all sources agreed ⇒ no signal

                diagnostics.Add(new FindingDiagnostic
                {
                    Type = CalibrationDiagnosticType.SeverityDisagreement,
                    Discipline = discipline.Discipline,
                    FindingId = finding.Id,
                    Title = finding.Title,
                    Severity = finding.Severity,
                    Confidence = finding.Confidence,
                    Files = SortedDistinctFiles(finding),
                    SeverityRange = finding.SeverityRange,
                    SeverityRationale = finding.SeverityRationale,
                    ProviderSeverities = ResolveProviderSeverities(run, finding, providers)
                });
            }

            // 3. Low agreement — a Comparable discipline with enough consolidated
            //    findings whose agreement rate is below the threshold.
            if (discipline.Agreement is { } agreement
                && agreement.ConsolidatedFindingCount >= criteria.MinimumSampleFindings
                && agreement.AgreementRate is { } rate
                && rate < criteria.LowAgreementThreshold)
            {
                diagnostics.Add(new FindingDiagnostic
                {
                    Type = CalibrationDiagnosticType.LowAgreement,
                    Discipline = discipline.Discipline,
                    ConsolidatedFindingCount = agreement.ConsolidatedFindingCount,
                    SharedFindingCount = agreement.SharedFindingCount,
                    AgreementRate = rate,
                    ExclusiveFindingCountByProvider = agreement.ExclusiveFindingCountByProvider
                });
            }

            // 4. Observation-type disagreement (M12.4) — a shared finding whose
            //    supporting observations, attributed to compared providers through
            //    existing provenance (SupportingFindingIds → raw findings →
            //    ObservationIds → EngineeringObservations → Evidence/provider), expose
            //    different normalized ObservationTypes per provider. Exact normalized
            //    values are compared; no semantic equivalence, no judgement.
            foreach (var finding in sharedFindings)
            {
                var attributed = AttributeObservations(run, finding, providers, observationsById, evidenceById);
                var typesByProvider = attributed
                    .Where(a => !string.IsNullOrWhiteSpace(a.Observation.ObservationType))
                    .GroupBy(a => a.Provider, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => (Provider: g.Key,
                        Types: g.Select(a => a.Observation.ObservationType.Trim())
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(t => t, StringComparer.Ordinal)
                            .ToList()))
                    .Where(g => g.Types.Count > 0)
                    .ToList();

                if (typesByProvider.Count < 2)
                    continue; // fewer than two providers have attributable observations

                var reference = typesByProvider[0].Types;
                if (typesByProvider.All(g => g.Types.SequenceEqual(reference, StringComparer.Ordinal)))
                    continue; // all providers agree on the observation types ⇒ no signal

                diagnostics.Add(new FindingDiagnostic
                {
                    Type = CalibrationDiagnosticType.ObservationTypeDisagreement,
                    Discipline = discipline.Discipline,
                    FindingId = finding.Id,
                    Title = finding.Title,
                    Providers = typesByProvider.Select(g => g.Provider).ToList(),
                    ObservationIds = AttributedObservationIds(attributed),
                    ObservationTypesByProvider = typesByProvider
                        .Select(g => new ProviderObservationType { Provider = g.Provider, ObservationTypes = g.Types })
                        .ToList()
                });
            }

            // 5. Location disagreement (M12.4) — a shared finding whose supporting
            //    observations, attributed through the same provenance, reference
            //    meaningfully different normalized source locations. Exact, existing
            //    normalized location semantics (reconciler-style path normalization);
            //    no fuzzy matching, no line-distance tolerance, no semantic inference.
            //    A provider without comparable location information contributes nothing,
            //    so missing locations never fabricate a disagreement.
            foreach (var finding in sharedFindings)
            {
                var attributed = AttributeObservations(run, finding, providers, observationsById, evidenceById);
                var locationsByProvider = attributed
                    .GroupBy(a => a.Provider, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => (Provider: g.Key, Locations: LocationsOf(g.Select(a => a.Observation))))
                    .Where(g => g.Locations.Count > 0)
                    .ToList();

                if (locationsByProvider.Count < 2)
                    continue; // fewer than two providers supplied comparable location info

                if (AllLocationSetsEquivalent(locationsByProvider.Select(g => g.Locations)))
                    continue; // normalized locations equivalent ⇒ no signal

                diagnostics.Add(new FindingDiagnostic
                {
                    Type = CalibrationDiagnosticType.LocationDisagreement,
                    Discipline = discipline.Discipline,
                    FindingId = finding.Id,
                    Title = finding.Title,
                    Providers = locationsByProvider.Select(g => g.Provider).ToList(),
                    ObservationIds = AttributedObservationIds(attributed),
                    LocationsByProvider = locationsByProvider
                        .Select(g => new ProviderLocation { Provider = g.Provider, NormalizedLocations = g.Locations })
                        .ToList()
                });
            }
        }

        limitations.Add(
            "Diagnostics are generated only for Comparable disciplines; the existing reconciliation is authoritative (no semantic matching, no ranking/scoring).");

        return new CalibrationDiagnosticsReport
        {
            RunId = run.RunId,
            Repository = run.SolutionName,
            RepositoryBranch = run.Branch,
            RepositoryCommit = run.Commit,
            Diagnostics = diagnostics
                .OrderBy(d => d.Type)
                .ThenBy(d => d.Discipline)
                .ThenBy(d => d.Provider, StringComparer.Ordinal)
                .ThenBy(d => d.FindingId, StringComparer.Ordinal)
                .ThenBy(d => d.Title, StringComparer.Ordinal)
                .ToList(),
            Limitations = limitations
                .OrderBy(l => l, StringComparer.Ordinal)
                .ToList(),
            Criteria = criteria,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static string BuildLimitation(DisciplineComparison discipline)
    {
        var reason = discipline.Status == ProviderComparisonStatus.Incomplete
            ? "one or more provider executions failed"
            : "providers received different effective contexts";
        return $"Discipline {discipline.Discipline}: {discipline.Status} — {reason}; no finding disagreement diagnostics generated.";
    }

    private static IReadOnlyList<ProviderSeverity> ResolveProviderSeverities(
        AnalysisRun run, Finding finding, IReadOnlyList<string> comparedProviders)
    {
        var result = new List<ProviderSeverity>();
        foreach (var rawId in finding.SupportingFindingIds)
        {
            var raw = run.RawFindings.FirstOrDefault(r => r.Id == rawId);
            if (raw is null) continue;

            var provider = RawProvider(raw, comparedProviders);
            if (provider is null || result.Any(p => string.Equals(p.Provider, provider, StringComparison.OrdinalIgnoreCase)))
                continue;

            result.Add(new ProviderSeverity { Provider = provider, Severity = raw.Severity.ToString() });
        }

        return result.OrderBy(p => p.Provider, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Attributes a raw finding to a single compared provider. A raw finding whose
    /// provenance names exactly one compared provider (its EvidenceProvider or its
    /// single SupportingProvider) is attributed; a raw finding that merged several
    /// providers upstream cannot be attributed to a single severity source.
    /// </summary>
    private static string? RawProvider(Finding raw, IReadOnlyList<string> comparedProviders)
    {
        if (!string.IsNullOrWhiteSpace(raw.EvidenceProvider)
            && comparedProviders.Contains(raw.EvidenceProvider, StringComparer.OrdinalIgnoreCase))
            return raw.EvidenceProvider;

        var supporting = raw.SupportingProviders
            .Where(p => comparedProviders.Contains(p, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return supporting.Count == 1 ? supporting[0] : null;
    }

    private static IReadOnlyList<string> SupportingCompared(Finding f, IReadOnlyList<string> comparedProviders)
        => f.SupportingProviders
            .Where(p => comparedProviders.Contains(p, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

    // ── M12.4 observation-level attribution ───────────────────────────────────

    /// <summary>
    /// Resolves a finding's supporting observations through the run's own provenance
    /// (Consolidated Finding → SupportingFindingIds → Raw Findings → ObservationIds →
    /// EngineeringObservations) and attributes each to a SINGLE compared provider via
    /// the observation's existing provider stamp or its evidence. An observation whose
    /// provider cannot be reliably attributed to a compared provider is skipped — never
    /// guessed from text, titles, ids, or ordering. Sorted for determinism.
    /// </summary>
    private static IReadOnlyList<(string Provider, EngineeringObservation Observation)> AttributeObservations(
        AnalysisRun run, Finding finding, IReadOnlyList<string> comparedProviders,
        IReadOnlyDictionary<string, EngineeringObservation> observationsById,
        IReadOnlyDictionary<string, Evidence> evidenceById)
    {
        var result = new List<(string Provider, EngineeringObservation Observation)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rawId in finding.SupportingFindingIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            var raw = run.RawFindings.FirstOrDefault(r => r.Id == rawId);
            if (raw is null) continue; // defensive; reconciler ids always resolve

            foreach (var obsId in raw.ObservationIds.OrderBy(id => id, StringComparer.Ordinal))
            {
                if (!seen.Add(obsId)) continue; // an observation supports a finding once

                if (!observationsById.TryGetValue(obsId, out var observation))
                    continue; // defensive; observation ids always resolve

                var provider = ObservedProvider(observation, evidenceById, comparedProviders);
                if (provider is null) continue; // cannot reliably attribute ⇒ skip, never guess

                result.Add((provider, observation));
            }
        }

        return result;
    }

    /// <summary>
    /// Attributes an observation to a compared provider using existing provenance only:
    /// the observation's own <see cref="EngineeringObservation.SourceProvider"/> (stamped
    /// from the evidence provider at interpretation), else its
    /// <see cref="EngineeringObservation.SourceEvidenceId"/> → <see cref="Evidence.ProviderName"/>.
    /// Null when neither resolves to one of the compared providers.
    /// </summary>
    private static string? ObservedProvider(
        EngineeringObservation observation, IReadOnlyDictionary<string, Evidence> evidenceById,
        IReadOnlyList<string> comparedProviders)
    {
        if (!string.IsNullOrWhiteSpace(observation.SourceProvider)
            && comparedProviders.Contains(observation.SourceProvider, StringComparer.OrdinalIgnoreCase))
            return observation.SourceProvider;

        if (!string.IsNullOrWhiteSpace(observation.SourceEvidenceId)
            && evidenceById.TryGetValue(observation.SourceEvidenceId, out var evidence)
            && !string.IsNullOrWhiteSpace(evidence.ProviderName)
            && comparedProviders.Contains(evidence.ProviderName, StringComparer.OrdinalIgnoreCase))
            return evidence.ProviderName;

        return null;
    }

    private static IReadOnlyList<string> AttributedObservationIds(
        IReadOnlyList<(string Provider, EngineeringObservation Observation)> attributed)
        => attributed
            .Select(a => a.Observation.Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Deterministic normalized locations of an observation's existing file references:
    /// the repository-relative path normalized exactly like the reconciler (forward
    /// slashes, leading "./" trimmed, lower-cased), with an explicit 1-based line when
    /// one is known. A file reference without an explicit line contributes the bare path.
    /// </summary>
    private static IReadOnlyList<string> LocationsOf(IEnumerable<EngineeringObservation> observations)
        => observations
            .SelectMany(o => o.FileReferences)
            .Where(r => !string.IsNullOrWhiteSpace(r.Path))
            .Select(r => NormalizeLocationPath(r.Path) + (r.StartLine is > 0 ? ":" + r.StartLine.Value : string.Empty))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(l => l, StringComparer.Ordinal)
            .ToList();

    private static string NormalizeLocationPath(string path)
        => path.Replace('\\', '/').TrimStart('.', '/').ToLowerInvariant();

    /// <summary>
    /// All provider location sets mutually equivalent? Location equivalence is exact on
    /// the normalized path and exact on the explicit line when BOTH sides name one; a
    /// location without an explicit line is line-compatible with any line on the same
    /// path (mirrors the reconciler's no-line-is-close rule; no line-distance tolerance).
    /// </summary>
    private static bool AllLocationSetsEquivalent(IEnumerable<IReadOnlyList<string>> locationSets)
    {
        var sets = locationSets.ToList();
        if (sets.Count == 0) return true;
        return sets.All(a => sets.All(b => LocationSetsEquivalent(a, b)));
    }

    private static bool LocationSetsEquivalent(IReadOnlyList<string> a, IReadOnlyList<string> b)
        => a.All(la => b.Any(lb => LocationsCompatible(la, lb)))
           && b.All(lb => a.Any(la => LocationsCompatible(la, lb)));

    private static bool LocationsCompatible(string a, string b)
    {
        var (pathA, lineA) = SplitLocation(a);
        var (pathB, lineB) = SplitLocation(b);
        return string.Equals(pathA, pathB, StringComparison.Ordinal)
            && (lineA is null || lineB is null || lineA == lineB);
    }

    private static (string Path, int? Line) SplitLocation(string location)
    {
        var separator = location.LastIndexOf(':');
        if (separator <= 0 || !int.TryParse(location[(separator + 1)..], out var line))
            return (location, null);
        return (location[..separator], line);
    }

    private static IReadOnlyList<string> SortedDistinctFiles(Finding finding)
        => finding.FileReferences
            .Select(r => r.Path)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
}
