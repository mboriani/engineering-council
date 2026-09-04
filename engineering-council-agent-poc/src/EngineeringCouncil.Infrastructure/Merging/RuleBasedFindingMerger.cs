using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringCouncil.Infrastructure.Merging;

/// <summary>
/// Deterministic, rule-based consolidation of raw analyzer findings — the first
/// council-like layer. No LLM. It clusters related findings, keeps the highest
/// severity, reconciles confidence by corroboration, preserves all evidence and
/// source agents, and records the merge rationale.
///
/// Merge rules (same category is required for rules 1 and 2):
///   1. Same category + same/very-similar title ⇒ merge.
///   2. Same category + overlapping file refs + similar recommendation ⇒ merge.
///   3. Same title, different category ⇒ keep separate, annotate with a note.
///   4. Severity ⇒ take the highest.
///   5. Confidence ⇒ High if ≥2 distinct agents; Medium if one agent with good
///      evidence; else Low.
///   6. Preserve all evidence.  7. Preserve all source agents.  8. Record rationale.
/// </summary>
public sealed class RuleBasedFindingMerger : IFindingMerger
{
    private const double TitleSimilarityThreshold = 0.6;
    private const double RecommendationSimilarityThreshold = 0.5;

    private readonly ILogger<RuleBasedFindingMerger> _logger;

    public RuleBasedFindingMerger(ILogger<RuleBasedFindingMerger>? logger = null)
        => _logger = logger ?? NullLogger<RuleBasedFindingMerger>.Instance;

    public Task<IReadOnlyList<Finding>> MergeAsync(
        IReadOnlyList<Finding> rawFindings,
        CancellationToken cancellationToken = default)
    {
        if (rawFindings.Count == 0)
            return Task.FromResult<IReadOnlyList<Finding>>([]);

        var clusters = Cluster(rawFindings);

        // Deterministic order: strongest first, so consolidated ids are stable.
        var ordered = clusters
            .OrderByDescending(c => c.Max(f => f.Severity))
            .ThenByDescending(c => c.Max(f => f.Confidence))
            .ThenBy(c => Normalize(c[0].Title), StringComparer.Ordinal)
            .ToList();

        var consolidated = new List<Finding>(ordered.Count);
        var index = 1;
        foreach (var cluster in ordered)
        {
            consolidated.Add(cluster.Count == 1
                ? Standalone(cluster[0], $"C-{index:D3}")
                : Merge(cluster, $"C-{index:D3}"));
            index++;
        }

        AnnotateCrossCategoryTitles(consolidated);

        _logger.LogInformation(
            "Merged {Raw} raw finding(s) into {Consolidated} consolidated finding(s)",
            rawFindings.Count, consolidated.Count);

        return Task.FromResult<IReadOnlyList<Finding>>(consolidated);
    }

    // ── Clustering ────────────────────────────────────────────────────────────

    private List<List<Finding>> Cluster(IReadOnlyList<Finding> findings)
    {
        var clusters = new List<List<Finding>>();

        foreach (var finding in findings)
        {
            var target = clusters.FirstOrDefault(c => c.Any(member => ShouldMerge(member, finding)));
            if (target is null)
                clusters.Add([finding]);
            else
                target.Add(finding);
        }

        return clusters;
    }

    private bool ShouldMerge(Finding a, Finding b)
    {
        if (a.Category != b.Category) return false;               // rules 1 & 2 require same category
        // No cross-provider merge yet (Milestone 005): findings from different
        // evidence providers stay separate — voting/consensus is a future milestone.
        if (!string.Equals(a.EvidenceProvider, b.EvidenceProvider, StringComparison.OrdinalIgnoreCase))
            return false;
        if (SimilarTitle(a.Title, b.Title)) return true;          // rule 1
        return FileReferencesOverlap(a, b)                        // rule 2
            && SimilarRecommendation(a.Recommendation, b.Recommendation);
    }

    // ── Building consolidated findings ─────────────────────────────────────────

    private static Finding Standalone(Finding f, string id) => f with
    {
        Id = id,
        Status = FindingStatus.New,
        MergedFromFindingIds = [f.Id],
        SourceAgents = f.SourceAgents.Count > 0 ? f.SourceAgents : [f.SourceAgent],
        SeverityRationale = $"Single-analyzer finding; severity {f.Severity} unchanged.",
        ConfidenceRationale = $"Single-analyzer finding; confidence {f.Confidence} unchanged."
    };

    private static Finding Merge(IReadOnlyList<Finding> cluster, string id)
    {
        var primary = cluster
            .OrderByDescending(f => f.Severity)
            .ThenByDescending(f => f.Confidence)
            .ThenByDescending(f => f.Evidence.Length)
            .First();

        var severity = cluster.Max(f => f.Severity);
        var sourceAgents = cluster
            .SelectMany(f => f.SourceAgents.Count > 0 ? f.SourceAgents : [f.SourceAgent])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var (confidence, confidenceRationale) = ReconcileConfidence(cluster, sourceAgents.Count);

        return primary with
        {
            Id = id,
            Status = FindingStatus.Merged,
            Severity = severity,
            Confidence = confidence,
            Evidence = PreserveEvidence(cluster),
            FileReferences = UnionFileReferences(cluster),
            Tags = cluster.SelectMany(f => f.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SourceAgent = primary.SourceAgent,
            SourceAgents = sourceAgents,
            MergedFromFindingIds = cluster.Select(f => f.Id).ToList(),
            // Preserve observation-based provenance across the merged cluster.
            ObservationIds = cluster.SelectMany(f => f.ObservationIds).Distinct().ToList(),
            SourceRules = cluster.SelectMany(f => f.SourceRules).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SupportingProviders = cluster.SelectMany(f => f.SupportingProviders).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SupportingObservationCount = cluster.Sum(f => f.SupportingObservationCount),
            SeverityRationale = BuildSeverityRationale(cluster, severity),
            ConfidenceRationale = confidenceRationale
        };
    }

    private static (FindingConfidence, string) ReconcileConfidence(IReadOnlyList<Finding> cluster, int distinctAgents)
    {
        if (distinctAgents >= 2)
            return (FindingConfidence.High,
                $"High — corroborated by {distinctAgents} independent analyzers.");

        var hasGoodEvidence = cluster.Any(f =>
            f.Confidence >= FindingConfidence.Medium && !string.IsNullOrWhiteSpace(f.Evidence));

        return hasGoodEvidence
            ? (FindingConfidence.Medium, "Medium — a single analyzer with substantive evidence.")
            : (FindingConfidence.Low, "Low — weak or limited evidence.");
    }

    private static string BuildSeverityRationale(IReadOnlyList<Finding> cluster, FindingSeverity severity)
    {
        var severities = cluster.Select(f => f.Severity).Distinct().ToList();
        if (severities.Count == 1)
            return $"All {cluster.Count} merged findings agree on {severity}.";

        var list = string.Join(", ", cluster.Select(f => $"{f.Severity} ({f.SourceAgent})"));
        return $"Escalated to {severity}: highest among merged severities [{list}].";
    }

    private static string PreserveEvidence(IReadOnlyList<Finding> cluster)
        => string.Join("\n", cluster
            .Where(f => !string.IsNullOrWhiteSpace(f.Evidence))
            .Select(f => $"[{f.SourceAgent}] {f.Evidence.Trim()}"));

    private static IReadOnlyList<FileReference> UnionFileReferences(IReadOnlyList<Finding> cluster)
        => cluster
            .SelectMany(f => f.FileReferences)
            .GroupBy(r => (r.Path, r.StartLine, r.EndLine))
            .Select(g => g.First())
            .ToList();

    /// <summary>Rule 3: consolidated findings that share a title across categories get a cross-reference note.</summary>
    private static void AnnotateCrossCategoryTitles(List<Finding> consolidated)
    {
        for (var i = 0; i < consolidated.Count; i++)
        {
            var related = consolidated
                .Where((other, j) => j != i
                    && other.Category != consolidated[i].Category
                    && Normalize(other.Title) == Normalize(consolidated[i].Title))
                .Select(other => other.Category)
                .Distinct()
                .ToList();

            if (related.Count == 0) continue;

            var note = "Related finding(s) with the same title exist in category(ies): "
                + string.Join(", ", related) + " — kept separate.";
            var tags = consolidated[i].Tags.ToList();
            foreach (var cat in related)
                tags.Add($"related:{cat.ToString().ToLowerInvariant()}");

            consolidated[i] = consolidated[i] with
            {
                Tags = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                SeverityRationale = string.IsNullOrWhiteSpace(consolidated[i].SeverityRationale)
                    ? note
                    : $"{consolidated[i].SeverityRationale} {note}"
            };
        }
    }

    // ── Text similarity helpers ────────────────────────────────────────────────

    private static bool SimilarTitle(string a, string b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);
        if (na.Length == 0 || nb.Length == 0) return false;
        if (na == nb) return true;
        return Jaccard(Tokenize(na), Tokenize(nb)) >= TitleSimilarityThreshold;
    }

    private static bool SimilarRecommendation(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        return Jaccard(Tokenize(Normalize(a)), Tokenize(Normalize(b))) >= RecommendationSimilarityThreshold;
    }

    private static bool FileReferencesOverlap(Finding a, Finding b)
    {
        if (a.FileReferences.Count == 0 || b.FileReferences.Count == 0) return false;
        var pathsA = a.FileReferences.Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return b.FileReferences.Any(r => pathsA.Contains(r.Path));
    }

    private static string Normalize(string text)
    {
        var lowered = text.ToLowerInvariant();
        var chars = lowered.Select(c => char.IsLetterOrDigit(c) ? c : ' ');
        var cleaned = new string(chars.ToArray());
        // Drop a leading "mock" token from the placeholder titles so real and mock compare fairly.
        return string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t != "mock"));
    }

    private static HashSet<string> Tokenize(string normalized)
        => normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var intersection = a.Count(b.Contains);
        var union = a.Count + b.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }
}
