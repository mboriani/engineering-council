using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Infrastructure.Summarizing;

/// <summary>
/// Deterministic, rule-based council summary. No LLM in Milestone 003 — it
/// derives the executive view from the consolidated findings and the merge
/// provenance already recorded on them.
/// </summary>
public sealed class RuleBasedCouncilSummaryGenerator : ICouncilSummaryGenerator
{
    private const int MaxKeyRisks = 5;
    private const int MaxNextActions = 5;

    public Task<CouncilSummary> GenerateAsync(
        AnalysisRun run,
        IReadOnlyList<Finding> consolidatedFindings,
        CancellationToken cancellationToken = default)
    {
        var ranked = consolidatedFindings
            .OrderByDescending(f => f.Severity)
            .ThenByDescending(f => f.Confidence)
            .ThenBy(f => f.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var mergeSummary = BuildMergeSummary(consolidatedFindings);

        var summary = new CouncilSummary
        {
            ExecutiveSummary = BuildExecutiveSummary(run, ranked, mergeSummary),
            KeyRisks = BuildKeyRisks(ranked),
            RecommendedNextActions = BuildNextActions(ranked),
            CategoryBreakdown = CountBy(consolidatedFindings, f => f.Category.ToString()),
            SeverityBreakdown = CountBy(consolidatedFindings, f => f.Severity.ToString()),
            ConfidenceBreakdown = CountBy(consolidatedFindings, f => f.Confidence.ToString()),
            MergeSummary = mergeSummary,
            GeneratedAt = DateTimeOffset.UtcNow
        };

        return Task.FromResult(summary);
    }

    private static MergeSummary BuildMergeSummary(IReadOnlyList<Finding> consolidated)
    {
        // Raw count is reconstructed from provenance recorded during merging.
        var rawCount = consolidated.Sum(f => Math.Max(1, f.MergedFromFindingIds.Count));
        var mergedGroups = consolidated.Count(f => f.Status == FindingStatus.Merged);
        var duplicatesRemoved = Math.Max(0, rawCount - consolidated.Count);

        return new MergeSummary
        {
            RawFindingCount = rawCount,
            ConsolidatedFindingCount = consolidated.Count,
            MergedGroupCount = mergedGroups,
            DuplicatesRemoved = duplicatesRemoved,
            Note = mergedGroups == 0
                ? "No findings were merged; each raw finding is distinct."
                : $"{mergedGroups} consolidated finding(s) merged from {duplicatesRemoved + mergedGroups} raw finding(s)."
        };
    }

    private static string BuildExecutiveSummary(
        AnalysisRun run, IReadOnlyList<Finding> ranked, MergeSummary merge)
    {
        if (ranked.Count == 0)
            return $"The council reviewed {run.SolutionName} and found no actionable engineering opportunities in this run.";

        var highOrCritical = ranked.Count(f => f.Severity >= FindingSeverity.High);
        var topSeverity = ranked[0].Severity;
        var categories = ranked.Select(f => f.Category).Distinct().Count();

        var risk = highOrCritical > 0
            ? $"including {highOrCritical} at High/Critical severity"
            : "none at High/Critical severity";

        return $"The council reviewed {run.SolutionName} using {run.Provider} and consolidated "
             + $"{merge.RawFindingCount} raw observation(s) into {merge.ConsolidatedFindingCount} finding(s) "
             + $"across {categories} engineering discipline(s). Top severity is {topSeverity} ({risk}).";
    }

    private static IReadOnlyList<string> BuildKeyRisks(IReadOnlyList<Finding> ranked)
        => ranked
            .Where(f => f.Severity >= FindingSeverity.Medium)
            .Take(MaxKeyRisks)
            .Select(f => $"[{f.Severity}/{f.Confidence}] {f.Title} ({f.Category})")
            .DefaultIfEmpty("No medium-or-higher risks identified.")
            .ToList();

    private static IReadOnlyList<string> BuildNextActions(IReadOnlyList<Finding> ranked)
        => ranked
            .Take(MaxNextActions)
            .Select(f => !string.IsNullOrWhiteSpace(f.Recommendation)
                ? f.Recommendation.Trim()
                : (!string.IsNullOrWhiteSpace(f.SuggestedTicketTitle) ? f.SuggestedTicketTitle.Trim() : f.Title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyDictionary<string, int> CountBy(
        IEnumerable<Finding> findings, Func<Finding, string> selector)
        => findings
            .GroupBy(selector)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());
}
