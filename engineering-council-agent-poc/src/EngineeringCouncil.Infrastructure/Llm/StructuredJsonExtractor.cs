using System.Text.RegularExpressions;

namespace EngineeringCouncil.Infrastructure.Llm;

/// <summary>
/// CONSERVATIVE extraction of the single structured JSON document from a model
/// response. Deliberately not a general-purpose heuristic parser: it accepts only
/// (a) a response that is itself one JSON object, or (b) a response containing
/// exactly one fenced code block holding one JSON object. Anything ambiguous —
/// several JSON documents, several fenced blocks, unbalanced JSON, or a large
/// prose response with embedded fragments — is REJECTED so bad output can never be
/// silently reinterpreted as evidence.
/// </summary>
internal static partial class StructuredJsonExtractor
{
    /// <summary>The maximum prose allowed around an otherwise valid JSON object.</summary>
    private const int MaxSurroundingProse = 200;

    public static bool TryExtract(string? raw, out string json, out string error)
    {
        json = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "The provider returned an empty response.";
            return false;
        }

        var text = raw.Trim();

        // (b) Fenced blocks — exactly one is allowed.
        var fences = FencedBlock().Matches(text);
        if (fences.Count > 1)
        {
            error = $"The response contained {fences.Count} fenced blocks; exactly one structured JSON document is required.";
            return false;
        }
        if (fences.Count == 1)
            text = fences[0].Groups["body"].Value.Trim();

        // (a) The remaining text must be one balanced JSON object, optionally with
        // a small amount of surrounding prose.
        var start = text.IndexOf('{');
        if (start < 0)
        {
            error = "The response contained no JSON object.";
            return false;
        }

        var end = FindBalancedEnd(text, start);
        if (end < 0)
        {
            error = "The response contained an incomplete or unbalanced JSON object (possibly truncated).";
            return false;
        }

        var candidate = text[start..(end + 1)];
        var before = text[..start].Trim();
        var after = text[(end + 1)..].Trim();

        // A second JSON document after the first is ambiguous — reject it.
        if (after.Contains('{') && FindBalancedEnd(after, after.IndexOf('{')) >= 0)
        {
            error = "The response contained multiple JSON documents; exactly one is required.";
            return false;
        }

        if (before.Length + after.Length > MaxSurroundingProse)
        {
            error = "The response wrapped the JSON in too much prose; strict JSON output is required.";
            return false;
        }

        json = candidate;
        return true;
    }

    /// <summary>Index of the '}' closing the object that starts at <paramref name="start"/>, or -1.</summary>
    private static int FindBalancedEnd(string text, int start)
    {
        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0) return i;
                    break;
            }
        }

        return -1;
    }

    [GeneratedRegex(@"```[a-zA-Z]*\s*(?<body>[\s\S]*?)```", RegexOptions.Multiline)]
    private static partial Regex FencedBlock();
}
