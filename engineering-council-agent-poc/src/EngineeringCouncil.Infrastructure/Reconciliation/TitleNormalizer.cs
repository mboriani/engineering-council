using System.Text.RegularExpressions;

namespace EngineeringCouncil.Infrastructure.Reconciliation;

/// <summary>
/// Deterministic finding-title normalizer used by the reconciler. It lowercases,
/// strips provider prefixes and file-path noise, removes punctuation, collapses
/// whitespace, and folds a small, EXPLICIT dictionary of technical synonyms. No
/// embeddings, no external AI — the same title always normalizes the same way.
/// </summary>
internal static partial class TitleNormalizer
{
    // Explicit, documented synonym folds: each variant → a canonical token phrase.
    // Keep this list small and obvious; it exists to align equivalent wording from
    // different tools/models, never to guess meaning.
    private static readonly (string Pattern, string Canonical)[] Synonyms =
    [
        ("hard coded", "hardcoded"),
        ("hard-coded", "hardcoded"),
        ("secret", "secret"),
        ("credential", "secret"),
        ("credentials", "secret"),
        ("password", "secret"),
        ("api key", "secret"),
        ("apikey", "secret"),
        ("sql injection", "injection"),
        ("sqli", "injection"),
        ("cross site scripting", "xss"),
        ("null reference", "null dereference"),
        ("null pointer", "null dereference"),
        ("nullref", "null dereference"),
        ("cyclomatic complexity", "complexity"),
        ("cognitive complexity", "complexity"),
        ("high complexity", "complexity"),
        ("missing timeout", "timeout"),
        ("no timeout", "timeout"),
        ("missing test", "missing tests"),
        ("untested", "missing tests"),
        ("missing documentation", "missing docs"),
        ("undocumented", "missing docs"),
        ("layering violation", "layer violation"),
        ("layer violation", "layer violation"),
    ];

    // Provider/model prefixes commonly prepended to titles.
    private static readonly string[] ProviderPrefixes = ["mock", "claude", "codex", "sarif", "ollama", "sonar", "roslyn"];

    public static string Normalize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;

        var text = title.ToLowerInvariant();

        // Remove file-path noise (anything that looks like a path or a file with an extension).
        text = PathNoise().Replace(text, " ");

        // Punctuation → space.
        text = NonAlphanumeric().Replace(text, " ");

        // Collapse whitespace and tokenize.
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        // Drop leading provider prefixes (e.g. "[MOCK] ...").
        while (tokens.Count > 0 && ProviderPrefixes.Contains(tokens[0]))
            tokens.RemoveAt(0);

        var collapsed = string.Join(' ', tokens);

        // Fold explicit synonyms (longest patterns first so multi-word wins).
        foreach (var (pattern, canonical) in Synonyms.OrderByDescending(s => s.Pattern.Length))
            collapsed = Regex.Replace(collapsed, $@"\b{Regex.Escape(pattern)}\b", canonical);

        // Re-collapse after folds.
        return string.Join(' ', collapsed.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Jaccard token overlap of two normalized titles in [0,1].</summary>
    public static double Similarity(string normalizedA, string normalizedB)
    {
        if (normalizedA.Length == 0 || normalizedB.Length == 0) return 0;
        if (normalizedA == normalizedB) return 1;

        var a = normalizedA.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var b = normalizedB.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (a.Count == 0 || b.Count == 0) return 0;

        var intersection = a.Count(b.Contains);
        var union = a.Count + b.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    [GeneratedRegex(@"[a-z0-9_./\\-]+\.[a-z0-9]+")]
    private static partial Regex PathNoise();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}
