using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringCouncil.Infrastructure.Reconciliation;

/// <summary>
/// Deterministic multi-source finding reconciler (Milestone 010). Groups raw
/// analyzer findings that describe the same engineering issue using EXPLICIT,
/// staged rules — never an LLM, never voting or provider weights — and emits one
/// consolidated finding per group with stable ids, severity/confidence
/// reconciliation, provider-agreement counting, and contradiction flags.
///
/// Grouping stages (all require the same discipline; best match wins):
///   1. exact-rule-location  — shared normalized rule id + shared file + close lines.
///   2. dedup-identity       — deterministic dedup (M15.3D, hardened M15.4A): shared
///                             method-symbol (suffix-compatible) that is a PRIMARY-focus
///                             symbol of both findings + close lines + consistent title
///                             focus + no material severity contradiction.
///   3. type-symbol          — shared observation type + shared file + shared symbol.
///   4. title-location       — similar normalized title + shared file or symbol.
///   5. keep-separate        — insufficient deterministic evidence ⇒ standalone.
/// False negatives are preferred to incorrect merges.
/// </summary>
public sealed class RuleBasedFindingReconciler : IFindingReconciler
{
    private const double TitleSimilarityThreshold = 0.6;
    private const int LineTolerance = 3;

    // Deterministic dedup focus guard (M15.3D): two findings only merge when their
    // titles share at least this many significant (stopword-filtered) tokens, or
    // when both name the shared symbol/file. Exact-token matching only — never
    // fuzzy similarity. This is the guard that keeps a multi-issue bundle from
    // swallowing a distinct single-issue finding merely because one of its
    // secondary observations happens to share a symbol.
    private const int MinFocusTitleTokens = 2;

    // M15.4A primary-focus anchor: a shared dedup method-part only carries identity
    // when it is a PRIMARY-focus method-part of BOTH findings. A method-part is
    // primary-focus for a finding when it is attached to an observation whose
    // normalized title is similar enough to the finding's own title. This is the
    // guard that keeps a generic category phrase (e.g. "magic string") shared
    // between distinct sub-issues — or a symbol inherited only from a bundle's
    // secondary observation — from becoming sufficient evidence of the same defect.
    // Grounded in the persisted M15.2/M15.4 runs: same-defect-rephrased primary
    // observations score >= 0.37, distinct-sub-issue observations score <= 0.25.
    private const double PrimaryFocusAnchorSimilarity = 0.30;

    // Conservative English function words dropped before exact title-token overlap.
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "any", "are", "as", "at", "be", "been", "being", "but", "by",
        "can", "could", "did", "do", "does", "during", "each", "for", "from", "has",
        "have", "had", "how", "if", "in", "into", "is", "it", "its", "may", "might",
        "must", "no", "nor", "not", "of", "off", "on", "or", "over", "should", "so",
        "such", "than", "that", "the", "their", "them", "then", "there", "these",
        "they", "this", "those", "through", "to", "too", "under", "until", "up",
        "upon", "was", "we", "were", "what", "when", "where", "which", "while",
        "who", "whom", "why", "will", "with", "without", "would", "you", "your"
    };

    // Providers whose evidence is deterministic (static analyzers / repository tools).
    private static readonly HashSet<string> DeterministicProviders =
        new(StringComparer.OrdinalIgnoreCase) { "SARIF", "Roslyn", "Sonar", "Semgrep", "NDepend", "Coverage", "Git" };

    private readonly ILogger<RuleBasedFindingReconciler> _logger;

    public RuleBasedFindingReconciler(ILogger<RuleBasedFindingReconciler>? logger = null)
        => _logger = logger ?? NullLogger<RuleBasedFindingReconciler>.Instance;

    public ReconciliationResult Reconcile(
        IReadOnlyList<Finding> rawFindings,
        IReadOnlyList<EngineeringObservation> observations,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var observationsById = observations
            .GroupBy(o => o.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Deterministic input order so clustering never depends on arrival order.
        var signatures = rawFindings
            .Select(f => Signature.Build(f, observationsById))
            .OrderByDescending(s => s.Finding.Severity)
            .ThenByDescending(s => s.Finding.Confidence)
            .ThenBy(s => s.NormalizedTitle, StringComparer.Ordinal)
            .ThenBy(s => s.Finding.Id, StringComparer.Ordinal)
            .ToList();

        var clusters = BuildClusters(signatures, cancellationToken);

        var consolidated = new List<Finding>(clusters.Count);
        var groups = new List<ReconciliationGroup>(clusters.Count);
        var usedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var cluster in clusters)
        {
            var finding = BuildConsolidated(cluster, usedIds);
            consolidated.Add(finding);
            groups.Add(BuildGroup(cluster, finding));
        }

        // Deterministic output order: strongest first, then stable by id.
        var ordered = consolidated
            .OrderByDescending(f => f.Severity)
            .ThenByDescending(f => f.Confidence)
            .ThenByDescending(f => f.AgreementCount)
            .ThenBy(f => f.Category.ToString(), StringComparer.Ordinal)
            .ThenBy(f => f.Id, StringComparer.Ordinal)
            .ToList();

        stopwatch.Stop();
        var summary = BuildSummary(rawFindings, signatures, ordered, groups, clusters, stopwatch.Elapsed);

        _logger.LogInformation(
            "Reconciled {Raw} raw finding(s) into {Consolidated} consolidated finding(s) ({Dedup} deduplicated, {Multi} multi-provider, {Contradictions} contradiction(s))",
            rawFindings.Count, ordered.Count, summary.DeduplicatedFindingCount, summary.MultiProviderFindingCount, summary.ContradictionCount);

        return new ReconciliationResult { ConsolidatedFindings = ordered, Groups = groups, Summary = summary };
    }

    // ── Clustering ────────────────────────────────────────────────────────────

    private static List<Cluster> BuildClusters(IReadOnlyList<Signature> signatures, CancellationToken cancellationToken)
    {
        var clusters = new List<Cluster>();

        foreach (var sig in signatures)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Cluster? best = null;
            var bestScore = 0.0;
            string bestStrategy = "", bestReason = "";

            foreach (var cluster in clusters)
            {
                foreach (var member in cluster.Members)
                {
                    var match = Match(member, sig);
                    if (match is { } m && m.Score > bestScore)
                    {
                        best = cluster;
                        bestScore = m.Score;
                        bestStrategy = m.Strategy;
                        bestReason = m.Reason;
                    }
                }
            }

            if (best is null)
                clusters.Add(new Cluster([sig]));
            else
            {
                best.Members.Add(sig);
                if (bestScore > best.MatchScore) { /* keep strongest link as representative */ }
                best.RecordJoin(bestScore, bestStrategy, bestReason);
            }
        }

        return clusters;
    }

    /// <summary>Returns the strongest staged match between two findings, or null.</summary>
    private static (double Score, string Strategy, string Reason)? Match(Signature a, Signature b)
    {
        if (a.Finding.Category != b.Finding.Category) return null; // never merge across disciplines

        // Stage 1 — exact rule + location.
        if (Intersects(a.Rules, b.Rules) && Intersects(a.Files, b.Files) && LinesClose(a, b))
            return (0.95, "exact-rule-location",
                $"Same discipline, shared rule ({Shared(a.Rules, b.Rules)}) and overlapping location.");

        // Stage 2 — deterministic dedup identity (M15.3D): a shared method-symbol
        // (suffix-compatible — e.g. `onactionexecutionasync` matches
        // `requestfilter.onactionexecutionasync`) whose line ranges are close in the
        // SAME file, combined with a focus-consistency guard. Refuses findings that
        // materially disagree on severity (an explicit contradiction must never be
        // silently erased by dedup).
        if (TryFindDedupIdentityMatch(a, b, out var sharedMethod))
            return (0.9, "dedup-identity",
                $"Same discipline, shared symbol method-part ({sharedMethod}) with close lines, consistent title focus, and no material disagreement.");

        // Stage 3 — observation type + symbol.
        if (Intersects(a.ObsTypes, b.ObsTypes) && Intersects(a.Files, b.Files) && Intersects(a.Symbols, b.Symbols))
            return (0.85, "type-symbol",
                $"Same discipline, observation type ({Shared(a.ObsTypes, b.ObsTypes)}), file and symbol.");

        // Stage 3 — similar title + shared file or symbol.
        var titleSim = TitleNormalizer.Similarity(a.NormalizedTitle, b.NormalizedTitle);
        if (titleSim >= TitleSimilarityThreshold && (Intersects(a.Files, b.Files) || Intersects(a.Symbols, b.Symbols)))
            return (Math.Round(0.6 + 0.1 * titleSim, 3), "title-location",
                $"Same discipline, similar title and shared file/symbol.");

        return null; // Stage 5 — keep separate.
    }

    // ── Deterministic dedup identity (Milestone 15.3D) ──────────────────────────

    /// <summary>
    /// Deterministic dedup predicate. Merges only when the two findings share a
    /// suffix-compatible method-symbol with close line ranges in the same file,
    /// that method-symbol is a PRIMARY-focus symbol of BOTH findings (M15.4A), their
    /// title focus is consistent, and they do not materially contradict each other
    /// on severity. This is a FINDING-DEDUPLICATION identity, never an LLM, never
    /// embeddings, never fuzzy title similarity on its own.
    /// </summary>
    private static bool TryFindDedupIdentityMatch(Signature a, Signature b, out string sharedMethod)
    {
        sharedMethod = "";

        // An explicit contradiction must never be silently consolidated: severities
        // two or more steps apart (e.g. Low vs Critical) are kept separate.
        if (Math.Abs((int)a.Finding.Severity - (int)b.Finding.Severity) >= 2)
            return false;

        foreach (var method in a.MethodSymbols.Intersect(b.MethodSymbols).OrderBy(x => x, StringComparer.Ordinal))
        {
            // M15.4A primary-focus anchor: the shared method-part must be the primary
            // focus of BOTH findings. A secondary observation (or a bundle member's
            // incidental symbol) never lends identity on its own.
            if (!a.PrimaryMethods.Contains(method) || !b.PrimaryMethods.Contains(method)) continue;
            if (!SymbolLinesClose(a, b, method)) continue;
            if (!FocusConsistent(a, b)) continue;
            sharedMethod = method;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Close-line check scoped to ONE shared method-symbol and to each finding's
    /// PRIMARY-focus evidence only: at least one of the symbol's (file, line) pairs
    /// carried by each finding's primary observations must share the same normalized
    /// file with lines within <see cref="LineTolerance"/>. Symbol evidence is
    /// collected per observation/finding in <see cref="Signature.Build"/>.
    /// </summary>
    private static bool SymbolLinesClose(Signature a, Signature b, string method)
    {
        var ea = EvidenceFor(a, method);
        var eb = EvidenceFor(b, method);
        foreach (var (fa, la) in ea)
            foreach (var (fb, lb) in eb)
                if (string.Equals(fa, fb, StringComparison.Ordinal) && Math.Abs(la - lb) <= LineTolerance)
                    return true;
        return false;
    }

    /// <summary>
    /// Focus-consistency guard: the two titles must share at least
    /// <see cref="MinFocusTitleTokens"/> significant (stopword-filtered) tokens.
    /// Exact-token matching only. This prevents a multi-issue bundle whose title
    /// focuses on issue X from absorbing a distinct finding about issue Y merely
    /// because the bundle also carries a secondary Y observation, or because a
    /// verbose summary happens to name shared symbols.
    /// </summary>
    private static bool FocusConsistent(Signature a, Signature b)
        => a.TitleTokens.Intersect(b.TitleTokens).Count() >= MinFocusTitleTokens;

    private static IEnumerable<(string File, int Line)> EvidenceFor(Signature s, string method)
        => s.PrimarySymbolEvidence.TryGetValue(method, out var list) ? list : [];

    /// <summary>Last '.'-separated segment of a symbol — the deterministic method-part identity.</summary>
    private static string MethodPart(string symbol)
    {
        var idx = symbol.LastIndexOf('.');
        return idx >= 0 ? symbol[(idx + 1)..] : symbol;
    }

    private static HashSet<string> SignificantTokens(string normalizedTitle)
        => normalizedTitle
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3 && !StopWords.Contains(t))
            .ToHashSet(StringComparer.Ordinal);

    // ── Building the consolidated finding ──────────────────────────────────────

    private Finding BuildConsolidated(Cluster cluster, HashSet<string> usedIds)
    {
        var members = cluster.Members.Select(s => s.Finding).ToList();
        var primary = members
            .OrderByDescending(f => f.Severity)
            .ThenByDescending(f => f.Confidence)
            .ThenByDescending(f => f.Evidence.Length)
            .ThenBy(f => f.Id, StringComparer.Ordinal)
            .First();

        var providers = cluster.Members
            .SelectMany(s => s.Providers)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var severity = members.Max(f => f.Severity);
        var (severityRange, severityRationale) = ReconcileSeverity(members, severity);

        var (contradiction, contradictionReasons) = DetectContradictions(cluster, members);
        var disciplineMismatch = members.Any(f => f.Tags.Contains("discipline-mismatch", StringComparer.OrdinalIgnoreCase));

        var (confidence, confidenceRange, confidenceRationale) =
            ReconcileConfidence(members, providers, contradictionReasons.Any(r => r.Contains("file")), disciplineMismatch);

        var isConsolidated = members.Count > 1;
        var id = StableId(cluster, primary, usedIds);

        var fileRefs = cluster.Members
            .SelectMany(s => s.Finding.FileReferences)
            .GroupBy(r => (r.Path, r.StartLine, r.EndLine))
            .Select(g => g.First())
            .ToList();

        return primary with
        {
            Id = id,
            Status = isConsolidated ? FindingStatus.Merged : FindingStatus.New,
            Severity = severity,
            Confidence = confidence,
            Evidence = string.Join("\n", cluster.Members
                .Select(s => s.Finding).Where(f => !string.IsNullOrWhiteSpace(f.Evidence))
                .Select(f => $"[{(string.IsNullOrEmpty(f.EvidenceProvider) ? f.SourceAgent : f.EvidenceProvider)}] {f.Evidence.Trim()}")),
            FileReferences = fileRefs,
            SymbolReferences = cluster.Members.SelectMany(s => s.Symbols).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            LineReferences = cluster.Members.SelectMany(s => s.Lines).Distinct().OrderBy(x => x).ToList(),
            Tags = members.SelectMany(f => f.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SourceAgent = primary.SourceAgent,
            SourceAgents = members.SelectMany(f => f.SourceAgents.Count > 0 ? f.SourceAgents : [f.SourceAgent]).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            MergedFromFindingIds = members.Select(f => f.Id).ToList(),
            SupportingFindingIds = members.Select(f => f.Id).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            ObservationIds = members.SelectMany(f => f.ObservationIds).Distinct(StringComparer.Ordinal).ToList(),
            SourceRules = members.SelectMany(f => f.SourceRules).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SupportingProviders = providers,
            SupportingObservationCount = members.Sum(f => f.SupportingObservationCount),
            AgreementCount = providers.Count,
            SeverityRange = severityRange,
            ConfidenceRange = confidenceRange,
            SeverityRationale = severityRationale,
            ConfidenceRationale = confidenceRationale,
            ReconciliationReason = isConsolidated ? cluster.Reason : "Standalone finding — no equivalent from another source.",
            ReconciliationStrategy = isConsolidated ? cluster.Strategy : "standalone",
            IsConsolidated = isConsolidated,
            HasContradiction = contradiction,
            ContradictionReasons = contradictionReasons,
            Metadata = new Dictionary<string, string>
            {
                ["matchScore"] = (isConsolidated ? cluster.MatchScore : 1.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                ["sourceCount"] = members.Count.ToString()
            }
        };
    }

    private static (string Range, string Rationale) ReconcileSeverity(IReadOnlyList<Finding> members, FindingSeverity severity)
    {
        var min = members.Min(f => f.Severity);
        var max = members.Max(f => f.Severity);
        var range = min == max ? max.ToString() : $"{min}–{max}";
        var rationale = min == max
            ? $"All {members.Count} source(s) agree on {severity}."
            : $"Highest severity retained ({severity}); sources ranged {min}–{max}. Agreement does not inflate severity.";
        return (range, rationale);
    }

    /// <summary>
    /// Deterministic confidence rule (documented in ADR-011). Start from the highest
    /// source confidence; a single provider gets no boost. ≥2 independent providers
    /// boost moderately (+1); a deterministic source agreeing with an LLM boosts more
    /// (+2). Location disagreement caps at Medium; a discipline-mismatch tag lowers a
    /// level. Always capped at High. Never a simple average; never provider weights.
    /// </summary>
    private static (FindingConfidence Level, string Range, string Rationale) ReconcileConfidence(
        IReadOnlyList<Finding> members, IReadOnlyList<string> providers, bool locationContradiction, bool disciplineMismatch)
    {
        var start = members.Max(f => f.Confidence);
        var level = (int)start;
        var reasons = new List<string>();

        var hasDeterministic = providers.Any(DeterministicProviders.Contains);
        var hasLlm = providers.Any(p => !DeterministicProviders.Contains(p));

        if (providers.Count >= 2)
        {
            var boost = hasDeterministic && hasLlm ? 2 : 1;
            level = Math.Min((int)FindingConfidence.High, level + boost);
            reasons.Add(hasDeterministic && hasLlm
                ? $"{providers.Count} independent providers incl. a deterministic source corroborate this."
                : $"{providers.Count} independent providers agree.");
        }
        else
        {
            reasons.Add("Single provider — confidence not inflated.");
        }

        if (locationContradiction && level > (int)FindingConfidence.Medium)
        {
            level = (int)FindingConfidence.Medium;
            reasons.Add("Location disagreement caps confidence at Medium.");
        }
        if (disciplineMismatch && level > (int)FindingConfidence.Low)
        {
            level--;
            reasons.Add("Discipline-mismatch tag lowers confidence.");
        }

        var minC = members.Min(f => f.Confidence);
        var maxC = members.Max(f => f.Confidence);
        var range = minC == maxC ? maxC.ToString() : $"{minC}–{maxC}";
        return ((FindingConfidence)level, range, string.Join(" ", reasons));
    }

    private static (bool HasContradiction, IReadOnlyList<string> Reasons) DetectContradictions(
        Cluster cluster, IReadOnlyList<Finding> members)
    {
        if (members.Count < 2) return (false, []);

        var reasons = new List<string>();

        var minSev = members.Min(f => f.Severity);
        var maxSev = members.Max(f => f.Severity);
        if (maxSev - minSev >= 2)
            reasons.Add($"Material severity disagreement: {minSev}–{maxSev}.");

        // No single file shared by ALL members while ≥2 distinct files exist → location divergence.
        var fileSets = cluster.Members.Select(s => (ISet<string>)s.Files).ToList();
        var commonToAll = fileSets.Aggregate(new HashSet<string>(fileSets[0], StringComparer.OrdinalIgnoreCase),
            (acc, s) => { acc.IntersectWith(s); return acc; });
        var distinctFiles = fileSets.SelectMany(s => s).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (commonToAll.Count == 0 && distinctFiles > 1)
            reasons.Add("Sources reference different files (no common location).");

        return (reasons.Count > 0, reasons);
    }

    // ── Stable identity ────────────────────────────────────────────────────────

    /// <summary>
    /// Deterministic id derived from discipline + primary rule + primary file +
    /// primary symbol + normalized title, so the same issue in the same repository
    /// state yields the same id regardless of finding arrival order. Collisions
    /// (identical signatures) get a numeric suffix to stay unique within the run.
    /// </summary>
    private static string StableId(Cluster cluster, Finding primary, HashSet<string> usedIds)
    {
        var rule = cluster.Members.SelectMany(s => s.Rules).OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault() ?? "";
        var file = cluster.Members.SelectMany(s => s.Files).OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault() ?? "";
        var symbol = cluster.Members.SelectMany(s => s.Symbols).OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault() ?? "";
        var title = cluster.Members.Select(s => s.NormalizedTitle).OrderBy(x => x, StringComparer.Ordinal).First();

        var signature = $"{primary.Category}|{rule}|{file}|{symbol}|{title}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)))[..10].ToLowerInvariant();

        var baseId = $"{DisciplineCode(primary.Category)}-{hash}";
        var id = baseId;
        var suffix = 2;
        while (!usedIds.Add(id)) id = $"{baseId}-{suffix++}";
        return id;
    }

    private static string DisciplineCode(FindingCategory category) => category switch
    {
        FindingCategory.Architecture => "ARC",
        FindingCategory.CodeQuality => "CQL",
        FindingCategory.Maintainability => "MNT",
        FindingCategory.Testing => "TST",
        FindingCategory.Performance => "PRF",
        FindingCategory.Security => "SEC",
        FindingCategory.Reliability => "REL",
        FindingCategory.Observability => "OBS",
        FindingCategory.Dependencies => "DEP",
        FindingCategory.Documentation => "DOC",
        FindingCategory.DeveloperExperience => "DEV",
        _ => "UNK"
    };

    private ReconciliationGroup BuildGroup(Cluster cluster, Finding consolidated) => new()
    {
        Id = $"RG-{consolidated.Id}",
        SourceFindingIds = cluster.Members.Select(s => s.Finding.Id).OrderBy(x => x, StringComparer.Ordinal).ToList(),
        ObservationIds = consolidated.ObservationIds,
        SupportingProviders = consolidated.SupportingProviders,
        ReconciliationReason = consolidated.ReconciliationReason,
        MatchScore = cluster.Members.Count > 1 ? cluster.MatchScore : 1.0,
        WasConsolidated = cluster.Members.Count > 1,
        ConsolidatedFindingId = consolidated.Id
    };

    private static ReconciliationSummary BuildSummary(
        IReadOnlyList<Finding> raw, IReadOnlyList<Signature> signatures,
        IReadOnlyList<Finding> consolidated, IReadOnlyList<ReconciliationGroup> groups,
        IReadOnlyList<Cluster> clusters, TimeSpan duration)
    {
        var byProvider = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var sig in signatures)
            foreach (var provider in sig.Providers.Count > 0 ? sig.Providers : ["(unknown)"])
                byProvider[provider] = byProvider.GetValueOrDefault(provider) + 1;

        // Each dedup-identity join consolidates two findings into one: exactly one
        // finding is removed per join, so PreDedup = PostDedup + joins.
        var deduplicated = clusters.Sum(c => c.DedupJoins);
        var consolidatedCount = consolidated.Count;

        return new ReconciliationSummary
        {
            RawFindingCount = raw.Count,
            ConsolidatedFindingCount = consolidatedCount,
            PreDedupFindingCount = consolidatedCount + deduplicated,
            PostDedupFindingCount = consolidatedCount,
            DeduplicatedFindingCount = deduplicated,
            ReconciliationGroupCount = groups.Count,
            MultiProviderFindingCount = consolidated.Count(f => f.AgreementCount >= 2),
            SingleProviderFindingCount = consolidated.Count(f => f.AgreementCount < 2),
            ContradictionCount = consolidated.Count(f => f.HasContradiction),
            FindingsByProvider = byProvider,
            Duration = duration
        };
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static bool Intersects(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b)
        => a.Count > 0 && b.Count > 0 && a.Any(x => b.Contains(x, StringComparer.OrdinalIgnoreCase));

    private static string Shared(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b)
        => a.FirstOrDefault(x => b.Contains(x, StringComparer.OrdinalIgnoreCase)) ?? "";

    private static bool LinesClose(Signature a, Signature b)
    {
        if (a.Lines.Count == 0 || b.Lines.Count == 0) return true; // rule+file already matched
        return a.Lines.Any(la => b.Lines.Any(lb => Math.Abs(la - lb) <= LineTolerance));
    }

    // ── Internal signature + cluster ─────────────────────────────────────────────

    private sealed class Cluster(List<Signature> members)
    {
        public List<Signature> Members { get; } = members;
        public double MatchScore { get; private set; }
        public string Strategy { get; private set; } = "standalone";
        public string Reason { get; private set; } = "";

        /// <summary>How many members joined this cluster via the dedup-identity stage (M15.3D).</summary>
        public int DedupJoins { get; private set; }

        public void RecordJoin(double score, string strategy, string reason)
        {
            if (strategy == "dedup-identity") DedupJoins++;
            if (score > MatchScore) { MatchScore = score; Strategy = strategy; Reason = reason; }
        }
    }

    private sealed record Signature(
        Finding Finding,
        string NormalizedTitle,
        HashSet<string> Rules,
        HashSet<string> ObsTypes,
        HashSet<string> Files,
        List<int> Lines,
        HashSet<string> Symbols,
        List<string> Providers,
        HashSet<string> MethodSymbols,
        Dictionary<string, List<(string File, int Line)>> SymbolEvidence,
        HashSet<string> TitleTokens,
        HashSet<string> PrimaryMethods,
        Dictionary<string, List<(string File, int Line)>> PrimarySymbolEvidence)
    {
        public static Signature Build(Finding f, IReadOnlyDictionary<string, EngineeringObservation> obsById)
        {
            var obs = f.ObservationIds.Select(id => obsById.GetValueOrDefault(id)).Where(o => o is not null).Select(o => o!).ToList();

            var rules = f.SourceRules.Concat(obs.Select(o => o.RuleId ?? ""))
                .Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.ToLowerInvariant())
                .ToHashSet(StringComparer.Ordinal);

            var obsTypes = obs.Select(o => o.ObservationType).Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);

            var files = f.FileReferences.Select(r => r.Path)
                .Concat(obs.SelectMany(o => o.FileReferences.Select(r => r.Path)))
                .Where(p => !string.IsNullOrWhiteSpace(p)).Select(NormalizePath)
                .ToHashSet(StringComparer.Ordinal);

            var lines = f.FileReferences.Select(r => r.StartLine).Where(l => l is > 0).Select(l => l!.Value)
                .Concat(f.LineReferences)
                .Concat(obs.SelectMany(o => o.LineReferences))
                .Concat(obs.SelectMany(o => o.FileReferences.Select(r => r.StartLine)).Where(l => l is > 0).Select(l => l!.Value))
                .Distinct().ToList();

            var symbols = obs.SelectMany(o => o.SymbolReferences)
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.ToLowerInvariant())
                .ToHashSet(StringComparer.Ordinal);

            var providers = f.SupportingProviders.Concat(string.IsNullOrWhiteSpace(f.EvidenceProvider) ? [] : [f.EvidenceProvider])
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            // Deterministic dedup identity (M15.3D): method-part symbols with per-symbol
            // (file, line) evidence, gathered from the finding's observations AND from
            // symbols the finding carries directly (e.g. consolidated findings). All
            // ordering is Ordinal so the identity never depends on arrival order.
            var methodSymbols = new HashSet<string>(StringComparer.Ordinal);
            var symbolEvidence = new Dictionary<string, List<(string File, int Line)>>(StringComparer.Ordinal);

            // M15.4A primary-focus anchor: method-parts attached to observations whose
            // normalized title is similar enough to the finding's title. Only these
            // carry dedup identity — a bundle's secondary observation never does.
            var findingTitleNorm = TitleNormalizer.Normalize(f.Title);
            var primaryMethods = new HashSet<string>(StringComparer.Ordinal);
            var primarySymbolEvidence = new Dictionary<string, List<(string File, int Line)>>(StringComparer.Ordinal);

            bool IsPrimaryObservation(EngineeringObservation o)
            {
                if (string.IsNullOrWhiteSpace(o.Title) || findingTitleNorm.Length == 0) return false;
                return TitleNormalizer.Similarity(findingTitleNorm, TitleNormalizer.Normalize(o.Title)) >= PrimaryFocusAnchorSimilarity;
            }

            void AddSymbolEvidence(string? symbol, string? file, int line, bool primary)
            {
                if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(file) || line <= 0) return;
                var method = MethodPart(symbol.ToLowerInvariant());
                if (method.Length == 0) return;
                methodSymbols.Add(method);
                if (!symbolEvidence.TryGetValue(method, out var list))
                    symbolEvidence[method] = list = [];
                if (!list.Contains((file, line))) list.Add((file, line));

                if (primary)
                {
                    primaryMethods.Add(method);
                    if (!primarySymbolEvidence.TryGetValue(method, out var plist))
                        primarySymbolEvidence[method] = plist = [];
                    if (!plist.Contains((file, line))) plist.Add((file, line));
                }
            }

            foreach (var o in obs)
            {
                var primary = IsPrimaryObservation(o);
                foreach (var s in o.SymbolReferences)
                    foreach (var r in o.FileReferences)
                        if (r.StartLine is > 0)
                            AddSymbolEvidence(s, NormalizePath(r.Path), r.StartLine!.Value, primary);
            }

            foreach (var s in f.SymbolReferences)
                foreach (var r in f.FileReferences)
                    if (r.StartLine is > 0)
                        AddSymbolEvidence(s, NormalizePath(r.Path), r.StartLine!.Value, false);

            var normalizedTitle = TitleNormalizer.Normalize(f.Title);
            var titleTokens = SignificantTokens(normalizedTitle);

            return new Signature(f, normalizedTitle, rules, obsTypes, files, lines, symbols, providers,
                methodSymbols, symbolEvidence, titleTokens, primaryMethods, primarySymbolEvidence);
        }

        private static string NormalizePath(string p) => p.Replace('\\', '/').TrimStart('.', '/').ToLowerInvariant();
    }
}
