using System.Globalization;
using System.Text;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Infrastructure.Reporting;

/// <summary>
/// Projects an <see cref="EngineeringReviewPackage"/> into the Markdown review
/// document consumed by an Engineering Review Board. This is one representation
/// of the package — the package itself is the primary artifact.
/// </summary>
public sealed class EngineeringReviewMarkdownExporter : IEngineeringReviewMarkdownExporter
{
    /// <summary>Canonical order of discipline sections in the review.</summary>
    private static readonly FindingCategory[] DisciplineOrder =
    [
        FindingCategory.Architecture, FindingCategory.Security, FindingCategory.Reliability,
        FindingCategory.CodeQuality, FindingCategory.Testing, FindingCategory.Documentation,
        FindingCategory.Observability, FindingCategory.Performance, FindingCategory.Dependencies,
        FindingCategory.Maintainability, FindingCategory.DeveloperExperience
    ];

    public string Export(EngineeringReviewPackage p)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Engineering Review Package");
        sb.AppendLine();
        sb.AppendLine($"> Version {p.Version} · generated {Iso(p.GeneratedAt)}");
        sb.AppendLine();

        RepositoryInformation(sb, p);
        RepositoryState(sb, p);
        RepositoryStatistics(sb, p);
        ExecutiveSummary(sb, p);
        HealthAndRisk(sb, p);
        Metrics(sb, p);
        EvidenceSummary(sb, p);
        CouncilFindings(sb, p);
        CouncilAssessment(sb, p);
        MultiSourceReconciliation(sb, p);
        RecommendedBacklog(sb, p);
        Appendix(sb, p);
        AcquisitionCoverage(sb, p);
        StaticAnalysisSources(sb, p);
        ObservationTraceability(sb, p);

        return sb.ToString();
    }

    private static void MultiSourceReconciliation(StringBuilder sb, EngineeringReviewPackage p)
    {
        var r = p.Reconciliation;
        if (r is null) return;

        sb.AppendLine("## Multi-Source Reconciliation");
        sb.AppendLine();
        sb.AppendLine($"- **Raw findings:** {r.RawFindingCount}");
        sb.AppendLine($"- **Consolidated findings:** {r.ConsolidatedFindingCount}");
        sb.AppendLine($"- **Multi-provider findings:** {r.MultiProviderFindingCount}");
        sb.AppendLine($"- **Contradictions flagged:** {r.ContradictionCount}");
        if (r.DeduplicatedFindingCount > 0)
            sb.AppendLine($"- **Deduplicated findings:** {r.DeduplicatedFindingCount} "
                + $"(pre-dedup {r.PreDedupFindingCount} → post-dedup {r.PostDedupFindingCount})");
        if (r.FindingsByProvider.Count > 0)
            sb.AppendLine($"- **Providers represented:** {string.Join(", ", r.FindingsByProvider.OrderByDescending(k => k.Value).Select(k => $"{Escape(k.Key)} ({k.Value})"))}");
        sb.AppendLine();

        var multi = p.Findings.Where(f => f.IsConsolidated).ToList();
        if (multi.Count > 0)
        {
            sb.AppendLine("**Consolidated findings**");
            sb.AppendLine();
            foreach (var f in Ordered(multi))
            {
                var range = string.IsNullOrEmpty(f.SeverityRange) || f.SeverityRange == f.Severity.ToString()
                    ? f.Severity.ToString() : f.SeverityRange;
                sb.AppendLine($"- **{Escape(f.Title)}** ({f.Category}) — {f.AgreementCount} provider(s): "
                    + $"{Escape(string.Join(", ", f.SupportingProviders))}; severity {range}; {Escape(f.ReconciliationReason)}"
                    + (f.HasContradiction ? $" ⚠️ contradiction: {Escape(string.Join("; ", f.ContradictionReasons))}" : ""));
            }
            sb.AppendLine();
        }
    }

    private static void StaticAnalysisSources(StringBuilder sb, EngineeringReviewPackage p)
    {
        if (p.StaticAnalysisSources.Count == 0) return; // section only appears when a static source ran

        sb.AppendLine("## Static Analysis Sources");
        sb.AppendLine();
        sb.AppendLine("| Tool | Version | Results Imported | Observations Generated |");
        sb.AppendLine("|------|---------|-----------------:|-----------------------:|");
        foreach (var s in p.StaticAnalysisSources)
            sb.AppendLine($"| {Escape(s.Tool)} | {Escape(string.IsNullOrEmpty(s.Version) ? "—" : s.Version)} "
                + $"| {s.ImportedResults} | {s.GeneratedObservations} |");
        sb.AppendLine();
    }

    private static void AcquisitionCoverage(StringBuilder sb, EngineeringReviewPackage p)
    {
        sb.AppendLine("## Evidence Acquisition Coverage");
        sb.AppendLine();

        var c = p.AcquisitionCoverage;
        if (p.AcquisitionPlan is null)
        {
            sb.AppendLine("_No acquisition plan recorded._");
            sb.AppendLine();
            return;
        }

        string List(IReadOnlyList<string> xs) => xs.Count == 0 ? "—" : string.Join(", ", xs);

        sb.AppendLine($"- **Repository-wide sources:** {List(c.RepositoryWideSourcesExecuted)}");
        sb.AppendLine($"- **Discipline-specific sources:** {List(c.DisciplineScopedSourcesExecuted)}");
        sb.AppendLine($"- **Disciplines requested:** {List(c.DisciplinesRequested)}");
        sb.AppendLine($"- **Disciplines covered:** {List(c.DisciplinesCovered)}");
        sb.AppendLine($"- **Acquisition steps:** {c.AcquisitionSteps} ({p.AcquisitionPlan.RepositoryScopedSteps} repository, {p.AcquisitionPlan.DisciplineScopedSteps} discipline)");
        sb.AppendLine($"- **Failed steps:** {c.FailedSteps}");
        sb.AppendLine($"- **Context files selected (total):** {c.ContextFilesSelected}");
        if (c.UnsupportedCombinations.Count > 0)
            sb.AppendLine($"- **Unsupported source/discipline combinations:** {List(c.UnsupportedCombinations)}");
        sb.AppendLine("- Full acquisition plan + per-step telemetry are in `provider-execution.json`.");
        sb.AppendLine();
    }

    private static void ObservationTraceability(StringBuilder sb, EngineeringReviewPackage p)
    {
        var e = p.EvidenceSummary;
        sb.AppendLine("## Observation and Evidence Traceability");
        sb.AppendLine();
        sb.AppendLine($"- **Total observations:** {e.TotalObservations}");
        sb.AppendLine($"- **Unsupported evidence:** {e.UnsupportedEvidence}");
        sb.AppendLine($"- **Interpreter failures:** {e.InterpreterFailures}");
        sb.AppendLine("- Full observation detail (with provenance) is in `observations.json`.");
        sb.AppendLine();

        if (e.ObservationsByProvider.Count > 0)
        {
            sb.AppendLine("**Observations by provider**");
            sb.AppendLine();
            foreach (var kv in e.ObservationsByProvider.OrderByDescending(k => k.Value))
                sb.AppendLine($"- **{Escape(kv.Key)}:** {kv.Value}");
            sb.AppendLine();
        }

        if (e.ObservationsByDiscipline.Count > 0)
        {
            sb.AppendLine("**Observations by discipline**");
            sb.AppendLine();
            foreach (var kv in e.ObservationsByDiscipline.OrderByDescending(k => k.Value))
                sb.AppendLine($"- **{Escape(kv.Key)}:** {kv.Value}");
            sb.AppendLine();
        }

        if (p.Findings.Count > 0)
        {
            sb.AppendLine("**Per-finding supporting observations**");
            sb.AppendLine();
            sb.AppendLine("| Finding | Discipline | Supporting observations | Providers |");
            sb.AppendLine("|---------|------------|------------------------|-----------|");
            foreach (var f in p.Findings.OrderByDescending(f => f.Severity))
                sb.AppendLine($"| {Escape(f.Title)} | {f.Category} | {f.SupportingObservationCount} | "
                    + $"{Escape(string.Join(", ", f.SupportingProviders))} |");
            sb.AppendLine();
        }
    }

    private static void RepositoryInformation(StringBuilder sb, EngineeringReviewPackage p)
    {
        sb.AppendLine("## Repository Information");
        sb.AppendLine();
        sb.AppendLine($"- **Repository:** {Escape(p.Repository)}");
        sb.AppendLine($"- **Branch:** {Escape(p.Branch)}");
        sb.AppendLine($"- **Commit:** `{Escape(p.Commit)}`");
        sb.AppendLine($"- **Analysis run:** `{p.AnalysisRunId}`");
        sb.AppendLine($"- **Analysis duration:** {p.AnalysisDuration.TotalSeconds:0.00}s");
        sb.AppendLine();
    }

    private static void RepositoryStatistics(StringBuilder sb, EngineeringReviewPackage p)
    {
        sb.AppendLine("## Repository Statistics");
        sb.AppendLine();
        sb.AppendLine($"- **Files scanned:** {p.Metrics.RepositoryFiles}");
        sb.AppendLine($"- **Projects:** {p.Metrics.Projects}");
        sb.AppendLine();
    }

    /// <summary>
    /// Compact reproducibility provenance (Milestone 015.3C): a short block telling
    /// the reader exactly what source state this review describes. The Markdown
    /// shows a useful commit prefix while run.json/package JSON preserve the full
    /// values. Never exposes source contents, secrets, or absolute local paths.
    /// </summary>
    private static void RepositoryState(StringBuilder sb, EngineeringReviewPackage p)
    {
        var s = p.RepositorySnapshot;
        if (s is null) return; // pre-M15.3C packages carry no identity

        sb.AppendLine("## Repository State");
        sb.AppendLine();

        if (string.IsNullOrEmpty(s.VersionControl))
        {
            sb.AppendLine("- **Version control identity:** unavailable");
            if (!string.IsNullOrEmpty(s.SnapshotFingerprint))
                sb.AppendLine($"- **Snapshot:** `{s.SnapshotFingerprint}`");
            sb.AppendLine();
            return;
        }

        if (!string.IsNullOrEmpty(s.CommitSha))
            sb.AppendLine($"- **Commit:** `{Short(s.CommitSha)}`");
        if (!string.IsNullOrEmpty(s.Branch))
            sb.AppendLine($"- **Branch:** {Escape(s.Branch)}");
        sb.AppendLine($"- **Working tree:** {TriState(s.IsDirty, "clean", "modified")}");
        sb.AppendLine($"- **Untracked analyzed files:** {TriState(s.HasUntrackedFiles, "No", "Yes")}");
        if (!string.IsNullOrEmpty(s.SnapshotFingerprint))
            sb.AppendLine($"- **Snapshot:** `{s.SnapshotFingerprint}`");
        sb.AppendLine($"- **Changed during review:** {(s.RepositoryChangedDuringRun ? "Yes" : "No")}");
        sb.AppendLine();
    }

    private static string TriState(bool? value, string whenFalse, string whenTrue)
        => value switch
        {
            true => whenTrue,
            false => whenFalse,
            _ => "unavailable"
        };

    private static string Short(string sha)
        => sha.Length <= 12 ? sha : sha[..12] + "...";

    private static void ExecutiveSummary(StringBuilder sb, EngineeringReviewPackage p)
    {
        sb.AppendLine("## Executive Summary");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(p.ExecutiveSummary) ? "_None._" : p.ExecutiveSummary);
        sb.AppendLine();

        if (p.KeyStrengths.Count > 0)
        {
            sb.AppendLine("**Key strengths**");
            sb.AppendLine();
            foreach (var s in p.KeyStrengths) sb.AppendLine($"- {Escape(s)}");
            sb.AppendLine();
        }

        if (p.KeyRisks.Count > 0)
        {
            sb.AppendLine("**Key risks**");
            sb.AppendLine();
            foreach (var r in p.KeyRisks) sb.AppendLine($"- {Escape(r)}");
            sb.AppendLine();
        }

        if (p.RecommendedNextActions.Count > 0)
        {
            sb.AppendLine("**Recommended next actions**");
            sb.AppendLine();
            foreach (var a in p.RecommendedNextActions) sb.AppendLine($"- {Escape(a)}");
            sb.AppendLine();
        }
    }

    private static void HealthAndRisk(StringBuilder sb, EngineeringReviewPackage p)
    {
        sb.AppendLine("## Engineering Health");
        sb.AppendLine();
        sb.AppendLine($"**{p.OverallEngineeringHealth}** — scale: Excellent › Good › Fair › NeedsAttention › Critical.");
        sb.AppendLine();
        sb.AppendLine("## Overall Risk");
        sb.AppendLine();
        sb.AppendLine($"**{p.OverallRisk}** — scale: Low › Moderate › High › Critical.");
        sb.AppendLine();

        // Coverage caveat (M15.3A): scoring is deliberately unchanged, but the
        // report must never let a NoEvidence discipline read as "reviewed and clean".
        var noEvidence = p.DisciplineCoverage?.Entries
            .Where(e => e.Status == DisciplineCoverageStatus.NoEvidence)
            .ToList();
        if (noEvidence is { Count: > 0 })
        {
            sb.AppendLine($"> **Coverage limitation:** scores reflect only disciplines with acquired evidence. "
                + $"No successful evidence was acquired for {string.Join(", ", noEvidence.Select(e => e.Discipline))} "
                + $"(evidence coverage {noEvidence[0].SuccessfulProviders}/{noEvidence[0].AttemptedProviders} providers). "
                + "Absence of findings there must not be interpreted as assurance.");
            sb.AppendLine();
        }
    }

    private static void Metrics(StringBuilder sb, EngineeringReviewPackage p)
    {
        var m = p.Metrics;
        sb.AppendLine("## Engineering Metrics");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| Total findings | {m.TotalFindings} |");
        sb.AppendLine($"| Critical / High / Medium / Low / Info | {m.Critical} / {m.High} / {m.Medium} / {m.Low} / {m.Info} |");
        sb.AppendLine($"| Merged findings | {m.MergedFindings} |");
        sb.AppendLine($"| Evidence sources | {m.EvidenceSources} |");
        sb.AppendLine($"| Successful / partial / failed providers | {m.SuccessfulProviders} / {m.PartialProviders} / {m.FailedProviders} |");
        sb.AppendLine($"| Repository files | {m.RepositoryFiles} |");
        sb.AppendLine($"| Projects | {m.Projects} |");
        sb.AppendLine($"| Analysis duration | {m.AnalysisDuration.TotalSeconds:0.00}s |");
        sb.AppendLine($"| Average confidence | {m.AverageConfidence:0.00} |");
        sb.AppendLine();
        if (m.CoverageByDiscipline.Count > 0)
        {
            sb.AppendLine("**Coverage by discipline**");
            sb.AppendLine();
            foreach (var kv in m.CoverageByDiscipline.OrderByDescending(k => k.Value))
                sb.AppendLine($"- **{kv.Key}:** {kv.Value}");
            sb.AppendLine();
        }
    }

    private static void EvidenceSummary(StringBuilder sb, EngineeringReviewPackage p)
    {
        var e = p.EvidenceSummary;
        sb.AppendLine("## Evidence Summary");
        sb.AppendLine();
        sb.AppendLine($"- **Sources used:** {(e.EvidenceSourcesUsed.Count == 0 ? "—" : string.Join(", ", e.EvidenceSourcesUsed))}");
        sb.AppendLine($"- **Evidence collected:** {e.EvidenceCount}");
        sb.AppendLine($"- **Provider failures:** {e.ProviderFailures}");
        sb.AppendLine();
        if (e.ProviderHealth.Count > 0)
        {
            sb.AppendLine("| Provider | Status | Executions | Failures | Evidence | Duration |");
            sb.AppendLine("|----------|--------|-----------|----------|----------|----------|");
            foreach (var h in e.ProviderHealth)
                sb.AppendLine($"| {Escape(h.ProviderName)} | {h.Status} | {h.Executions} | {h.Failures} | {h.EvidenceCount} | {h.TotalDuration.TotalSeconds:0.00}s |");
            sb.AppendLine();
        }
    }

    private static void CouncilFindings(StringBuilder sb, EngineeringReviewPackage p)
    {
        sb.AppendLine("## Council Findings");
        sb.AppendLine();

        var byCategory = p.Findings.GroupBy(f => f.Category).ToDictionary(g => g.Key, g => g.ToList());
        var coverage = p.DisciplineCoverage?.Entries
            .Where(e => Enum.TryParse<FindingCategory>(e.Discipline, out _))
            .ToDictionary(e => Enum.Parse<FindingCategory>(e.Discipline), e => e);

        var renderedAny = false;
        foreach (var discipline in DisciplineOrder)
        {
            var findings = byCategory.GetValueOrDefault(discipline);
            if (findings is { Count: > 0 })
            {
                // CoveredWithFindings (or a finding that exists regardless of the
                // recorded coverage state) — normal findings reporting.
                renderedAny = true;
                sb.AppendLine($"### {discipline} Review");
                sb.AppendLine();
                foreach (var (f, i) in Ordered(findings).Select((f, i) => (f, i + 1)))
                    AppendFinding(sb, f, i);

                // Partial coverage stays visible even when findings exist (M15.3A):
                // a discipline whose evidence was only partially acquired is NOT
                // indistinguishable from a fully-covered one.
                if (coverage is not null
                    && coverage.TryGetValue(discipline, out var entry)
                    && entry.Status == DisciplineCoverageStatus.CoveredWithFindings
                    && entry.SuccessfulProviders < entry.AttemptedProviders)
                {
                    sb.AppendLine($"Evidence coverage: {entry.SuccessfulProviders}/{entry.AttemptedProviders} providers");
                    sb.AppendLine();
                }
                continue;
            }

            if (coverage is null || !coverage.TryGetValue(discipline, out var coverageEntry)) continue;

            renderedAny = true;
            sb.AppendLine($"### {discipline} Review");
            sb.AppendLine();
            if (coverageEntry.Status == DisciplineCoverageStatus.NoEvidence)
            {
                // The M15.3A defect: never imply this discipline was reviewed clean.
                sb.AppendLine("No successful evidence was acquired for this discipline; absence of findings must not be interpreted as assurance.");
            }
            else if (coverageEntry.Status == DisciplineCoverageStatus.CoveredNoFindings)
            {
                // Distinct from NoEvidence: evidence WAS acquired, findings reviewed.
                sb.AppendLine("No findings were identified from the available evidence.");
            }
            else
            {
                // CoveredWithFindings should have had findings above; safety net.
                sb.AppendLine("Findings were expected from the acquired evidence; see the finding sections.");
            }
            sb.AppendLine();
            sb.AppendLine($"Evidence coverage: {coverageEntry.SuccessfulProviders}/{coverageEntry.AttemptedProviders} providers");
            sb.AppendLine();
        }

        if (!renderedAny)
        {
            sb.AppendLine("_No findings and no recorded coverage data for this run._");
            sb.AppendLine();
        }
    }

    /// <summary>
    /// Compact Council-assessment counts (Milestone 014.3) — a pure projection of
    /// <see cref="EngineeringReviewPackage.CouncilAssessmentSummary"/>; never recomputed here.
    /// </summary>
    private static void CouncilAssessment(StringBuilder sb, EngineeringReviewPackage p)
    {
        var s = p.CouncilAssessmentSummary;
        if (s is null) return;

        sb.AppendLine("## Council Assessment");
        sb.AppendLine();
        sb.AppendLine($"- Strong agreement: {s.StrongAgreementCount}");
        sb.AppendLine($"- Agreement with differences: {s.AgreementWithDifferencesCount}");
        sb.AppendLine($"- Single source: {s.SingleSourceCount}");
        sb.AppendLine($"- Potential conflicts: {s.PotentialConflictCount}");
        sb.AppendLine();
    }

    private static void RecommendedBacklog(StringBuilder sb, EngineeringReviewPackage p)
    {
        sb.AppendLine("## Recommended Backlog");
        sb.AppendLine();

        // Quick wins: low/medium severity. Strategic: high/critical severity.
        var quickWins = Ordered(p.Findings.Where(f => f.Severity <= FindingSeverity.Medium)).ToList();
        var strategic = Ordered(p.Findings.Where(f => f.Severity >= FindingSeverity.High)).ToList();

        sb.AppendLine("### Quick Wins");
        sb.AppendLine();
        AppendBacklog(sb, quickWins);

        sb.AppendLine("### Strategic Improvements");
        sb.AppendLine();
        AppendBacklog(sb, strategic);
    }

    private static void AppendBacklog(StringBuilder sb, IReadOnlyList<Finding> findings)
    {
        if (findings.Count == 0)
        {
            sb.AppendLine("_None._");
            sb.AppendLine();
            return;
        }
        foreach (var f in findings)
        {
            var title = string.IsNullOrWhiteSpace(f.SuggestedTicketTitle) ? f.Title : f.SuggestedTicketTitle;
            sb.AppendLine($"- **[{f.Severity}/{f.Category}]** {Escape(title)}");
        }
        sb.AppendLine();
    }

    private static void Appendix(StringBuilder sb, EngineeringReviewPackage p)
    {
        sb.AppendLine("## Appendix");
        sb.AppendLine();

        sb.AppendLine("### Provider Execution");
        sb.AppendLine();
        if (p.ProviderExecution is { } pe)
        {
            sb.AppendLine($"- **Providers:** {string.Join(", ", pe.ProvidersExecuted)}");
            sb.AppendLine($"- **Executions:** {pe.TotalExecutions} ({pe.Failures} failed, {pe.EvidenceCount} evidence)");
            if (pe.TotalTokens is { } t) sb.AppendLine($"- **Tokens:** {t}");
            sb.AppendLine("- See `provider-execution.json` for per-call detail.");
        }
        else sb.AppendLine("_No provider execution recorded._");
        sb.AppendLine();

        sb.AppendLine("### Merge Notes");
        sb.AppendLine();
        if (p.Appendix.MergeNotes.Count == 0) sb.AppendLine("_No findings were merged._");
        else foreach (var note in p.Appendix.MergeNotes) sb.AppendLine($"- {Escape(note)}");
        sb.AppendLine();

        sb.AppendLine("### Raw Findings Reference");
        sb.AppendLine();
        if (p.Appendix.RawFindings.Count == 0)
        {
            sb.AppendLine("_No raw findings._");
        }
        else
        {
            sb.AppendLine($"The {p.Appendix.RawFindings.Count} raw finding(s) are preserved in `raw-findings.json`.");
            sb.AppendLine();
            sb.AppendLine("| Raw ID | Title | Category | Severity | Analyzer | Provider |");
            sb.AppendLine("|--------|-------|----------|----------|----------|----------|");
            foreach (var f in p.Appendix.RawFindings)
                sb.AppendLine($"| `{f.Id}` | {Escape(f.Title)} | {f.Category} | {f.Severity} | {Escape(f.SourceAgent)} | {Escape(f.EvidenceProvider)} |");
        }
        sb.AppendLine();
    }

    private static void AppendFinding(StringBuilder sb, Finding f, int index)
    {
        var agents = f.SourceAgents.Count > 0 ? string.Join(", ", f.SourceAgents) : f.SourceAgent;
        sb.AppendLine($"#### {index}. {Escape(f.Title)}");
        sb.AppendLine();
        sb.AppendLine($"`{f.Id}` · **{f.Severity}** severity · **{f.Confidence}** confidence · **{f.Status}** · "
            + $"_{Escape(agents)}_{(string.IsNullOrEmpty(f.EvidenceProvider) ? "" : $" · via {Escape(f.EvidenceProvider)}")} · "
            + $"{f.SupportingObservationCount} supporting observation(s)");
        sb.AppendLine();
        if (f.SupportingProviders.Count > 0)
        {
            var range = string.IsNullOrEmpty(f.SeverityRange) || f.SeverityRange == f.Severity.ToString()
                ? "" : $" · severity range {f.SeverityRange}";
            sb.AppendLine($"_Providers: {Escape(string.Join(", ", f.SupportingProviders))} "
                + $"· agreement {f.AgreementCount}{range}_"
                + (f.HasContradiction ? " · ⚠️ contradiction flagged" : ""));
            sb.AppendLine();

            if (f.CouncilAssessment is { } a)
            {
                sb.AppendLine($"Council Assessment: {a.Type}");
                sb.AppendLine($"Supporting Providers: {Escape(string.Join(", ", a.SupportingProviders))}");
                sb.AppendLine($"Differences: {(a.Differences.Count == 0 ? "None" : string.Join(", ", a.Differences))}");
                if (a.ContradictionReasons.Count > 0)
                    sb.AppendLine($"Explicit Contradictions: {Escape(string.Join("; ", a.ContradictionReasons))}");

                // Targeted semantic review (Milestone 014.4) — only ever present for the
                // narrow ObservationType-disagreement subset; a pure projection, never
                // recomputed or expanded into chain-of-thought here.
                if (f.SemanticReview is { } review)
                {
                    sb.AppendLine($"Semantic Review: {review.Decision}");
                    if (!string.IsNullOrWhiteSpace(review.Reason))
                        sb.AppendLine($"Reason: {Escape(review.Reason)}");
                }

                sb.AppendLine();
            }
        }
        Section(sb, "Summary", f.Summary);
        Section(sb, "Evidence", f.Evidence);
        if (f.FileReferences.Count > 0)
        {
            sb.AppendLine("**Files**");
            sb.AppendLine();
            foreach (var r in f.FileReferences)
                sb.AppendLine($"- `{Escape(r.Path)}`{FormatLines(r)}");
            sb.AppendLine();
        }
        Section(sb, "Recommendation", f.Recommendation);
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static IEnumerable<Finding> Ordered(IEnumerable<Finding> findings)
        => findings
            .OrderByDescending(f => f.Severity)
            .ThenByDescending(f => f.Confidence)
            .ThenBy(f => f.Title, StringComparer.OrdinalIgnoreCase);

    private static void Section(StringBuilder sb, string heading, string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        sb.AppendLine($"**{heading}**");
        sb.AppendLine();
        sb.AppendLine(body.Trim());
        sb.AppendLine();
    }

    private static string FormatLines(FileReference r)
    {
        if (r.StartLine is null) return string.Empty;
        return r.EndLine is null || r.EndLine == r.StartLine
            ? $" (line {r.StartLine})"
            : $" (lines {r.StartLine}-{r.EndLine})";
    }

    private static string Iso(DateTimeOffset value)
        => value.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture);

    private static string Escape(string? value)
        => (value ?? string.Empty).Replace("|", "\\|").Replace("\r\n", " ").Replace("\n", " ");
}
