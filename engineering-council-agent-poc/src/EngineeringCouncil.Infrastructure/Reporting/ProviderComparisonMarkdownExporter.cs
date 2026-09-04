using System.Text;
using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Infrastructure.Reporting;

/// <summary>
/// Concise human-readable projection of a <see cref="ProviderComparisonReport"/>
/// (provider-comparison.md). Mirror of the JSON artifact: descriptive only, no
/// ranking or quality judgment. Small by design — the JSON is authoritative.
/// </summary>
public static class ProviderComparisonMarkdownExporter
{
    public static string Export(ProviderComparisonReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Provider Comparison");
        sb.AppendLine();
        sb.AppendLine($"> Run `{report.RunId}` · {report.Repository} · descriptive only — no ranking, scoring, or calibration.");
        sb.AppendLine();

        sb.AppendLine("## Scope");
        sb.AppendLine();
        sb.AppendLine($"- Repository: {report.Repository} ({report.RepositoryBranch} @ {report.RepositoryCommit})");
        sb.AppendLine($"- Compared providers: {string.Join(", ", report.ComparedProviders)}");
        sb.AppendLine($"- Compared disciplines: {string.Join(", ", report.DisciplineComparisons.Select(d => d.Discipline.ToString()))}");
        sb.AppendLine();

        sb.AppendLine("## Comparable Inputs");
        sb.AppendLine();
        foreach (var d in report.DisciplineComparisons)
        {
            sb.AppendLine($"- **{d.Discipline}** ({d.Status}): {d.ComparableContextDescription}");
        }
        sb.AppendLine();

        sb.AppendLine("## Execution Metrics");
        sb.AppendLine();
        sb.AppendLine("| Discipline | Provider | Model | Success | Duration | InputTokens | OutputTokens | Retries | RepairAttempts | ErrorCategory |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var d in report.DisciplineComparisons)
            foreach (var e in d.ExecutionMetrics)
                sb.AppendLine($"| {d.Discipline} | {e.Provider} | {e.Model} | {e.Success} | {e.Duration} | {Fmt(e.InputTokens)} | {Fmt(e.OutputTokens)} | {e.RetryCount} | {e.RepairAttemptCount} | {e.ErrorCategory ?? "—"} |");
        sb.AppendLine();

        sb.AppendLine("## Observation Comparison");
        sb.AppendLine();
        foreach (var d in report.DisciplineComparisons)
        {
            if (d.ObservationMetrics.Count == 0)
            {
                sb.AppendLine($"- **{d.Discipline}**: output comparison unavailable.");
                continue;
            }
            sb.AppendLine($"- **{d.Discipline}**:");
            foreach (var o in d.ObservationMetrics)
            {
                sb.AppendLine($"  - {o.Provider}: {o.ObservationCount} observations · files referenced {o.FilesReferenced} · " +
                    $"with location {o.ObservationsWithLocation} · without location {o.ObservationsWithoutLocation} · " +
                    $"referenced-context rate {Rate(o.ReferencedContextFileRate)}");
                sb.AppendLine($"    - Types: {Dist(o.Types)}");
                sb.AppendLine($"    - Severities: {Dist(o.Severities)}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("## Finding Agreement");
        sb.AppendLine();
        foreach (var d in report.DisciplineComparisons)
        {
            if (d.Agreement is null)
            {
                sb.AppendLine($"- **{d.Discipline}**: agreement not computed ({d.Status}).");
                continue;
            }
            var a = d.Agreement;
            sb.AppendLine($"- **{d.Discipline}**: {a.ConsolidatedFindingCount} consolidated · " +
                $"{a.SharedFindingCount} shared · agreement rate {Rate(a.AgreementRate)}");
        }
        sb.AppendLine();

        sb.AppendLine("## Provider-Exclusive Findings");
        sb.AppendLine();
        foreach (var d in report.DisciplineComparisons)
        {
            var exclusives = d.FindingMetrics
                .Where(f => f.ExclusiveFindingCount > 0)
                .Select(f => $"{f.Provider}: {f.ExclusiveFindingCount} ({string.Join(", ", f.ExclusiveFindingIds)})");
            sb.AppendLine($"- **{d.Discipline}**: {(exclusives.Any() ? string.Join(" · ", exclusives) : "none")}");
        }
        sb.AppendLine();

        sb.AppendLine("## Limitations");
        sb.AppendLine();
        foreach (var note in report.Limitations)
            sb.AppendLine($"- {note}");
        sb.AppendLine();

        return sb.ToString();
    }

    private static string Fmt(int? v) => v.HasValue ? v.Value.ToString() : "unknown";
    private static string Rate(double? v) => v.HasValue ? $"{v.Value:P0}" : "unknown";
    private static string Dist(IReadOnlyList<NamedCount> counts)
        => counts.Count == 0 ? "—" : string.Join(", ", counts.Select(c => $"{c.Name}={c.Count}"));
}
