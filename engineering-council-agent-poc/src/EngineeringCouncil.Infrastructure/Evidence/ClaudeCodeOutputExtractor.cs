using System.Text.Json;

namespace EngineeringCouncil.Infrastructure.Evidence;

/// <summary>
/// The minimal adapter between the Claude Code CLI's own JSON envelope and the
/// existing structured observations contract (Milestone 013.5, extended
/// Milestone 015.2A).
///
/// The demonstrated Claude CLI behavior: <c>claude -p --output-format json</c>
/// wraps the agent's final message text in a single result envelope
/// (<c>{"type":"result","is_error":false,"result":"…","usage":{…}, …}</c>). The
/// observations envelope the model produces is carried in that <c>result</c>
/// string. This extractor unwraps that ONE documented envelope field and then
/// hands the extracted text to the existing <c>LlmEvidenceResponseValidator</c> —
/// it is NOT a new parser/domain language, and it never heuristically interprets
/// arbitrary prose. Anything that is not exactly one valid result envelope with a
/// <c>result</c> string is rejected.
///
/// Milestone 015.2A: the same result envelope carries the CLI's OWN authoritative
/// session usage (<c>usage.input_tokens</c> / <c>usage.output_tokens</c>, plus
/// cache categories and <c>total_cost_usd</c>). The result message's <c>usage</c>
/// is the cumulative total for the whole call (the top-level agent loop, including
/// its internal Read/Glob/Grep tool interactions; subagents are not used by this
/// adapter). Milestone 015.2C: the cache categories (<c>cache_read_input_tokens</c>
/// / <c>cache_creation_input_tokens</c>) are ALSO lifted as separate provider-neutral
/// telemetry; they never inflate <c>TotalTokens</c>. Cost is out of scope.
/// </summary>
internal static class ClaudeCodeOutputExtractor
{
    /// <summary>Bounded surface for an error envelope's <c>result</c> text — never unlimited, never secret-laden.</summary>
    private const int MaxErrorResultCharacters = 300;

    public static bool TryExtractResult(string? envelope, out string result, out string error, out ClaudeCodeUsage usage)
    {
        result = string.Empty;
        error = string.Empty;
        usage = new ClaudeCodeUsage(null, null, null, null);

        if (string.IsNullOrWhiteSpace(envelope))
        {
            error = "The Claude Code CLI returned an empty response.";
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(envelope);
        }
        catch (JsonException ex)
        {
            error = $"The Claude Code CLI response was not a JSON envelope: {ex.Message}";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "The Claude Code CLI response was not a JSON envelope object.";
                return false;
            }

            if (root.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True)
            {
                var reason = root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String
                    ? r.GetString()
                    : null;
                error = string.IsNullOrWhiteSpace(reason)
                    ? "The Claude Code CLI reported an error."
                    : $"The Claude Code CLI reported an error: {Bound(reason!)}";
                return false;
            }

            if (!root.TryGetProperty("result", out var resultProperty) || resultProperty.ValueKind != JsonValueKind.String)
            {
                error = "The Claude Code CLI response envelope is missing a 'result' string field.";
                return false;
            }

            result = resultProperty.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(result))
            {
                error = "The Claude Code CLI returned an empty result.";
                return false;
            }

            usage = ParseUsage(root);
            return true;
        }
    }

    /// <summary>
    /// Lifts the CLI's own session usage (<c>usage.input_tokens</c>,
    /// <c>usage.output_tokens</c>, and the cache categories
    /// <c>usage.cache_read_input_tokens</c>/<c>usage.cache_creation_input_tokens</c>)
    /// from the result envelope. Missing/non-numeric values stay null — usage is
    /// never fabricated and never becomes zero. Only fields actually present and
    /// numeric are mapped; <c>total_cost_usd</c> and any other fields are ignored.
    /// </summary>
    private static ClaudeCodeUsage ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return new ClaudeCodeUsage(null, null, null, null);

        int? Number(string property)
            => usage.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n)
                ? n
                : null;

        return new ClaudeCodeUsage(
            Number("input_tokens"), Number("output_tokens"),
            Number("cache_read_input_tokens"), Number("cache_creation_input_tokens"));
    }

    private static string Bound(string text)
    {
        text = text.Trim();
        return text.Length <= MaxErrorResultCharacters ? text : text[..MaxErrorResultCharacters];
    }
}

/// <summary>
/// Provider-neutral token usage lifted from the Claude Code result envelope.
/// <see cref="InputTokens"/>/<see cref="OutputTokens"/> and the cache categories
/// (<see cref="CacheReadInputTokens"/>/<see cref="CacheCreationInputTokens"/>) are
/// null when the runtime did not report them; <see cref="TotalTokens"/> is the sum
/// of input + output when at least one side is known (M12.1 semantics), null when
/// neither is known. Cache tokens are carried as SEPARATE, additive telemetry and
/// are deliberately EXCLUDED from <see cref="TotalTokens"/> — cache usage is
/// observable on its own and never inflates the historical M12.1 total.
/// </summary>
internal sealed record ClaudeCodeUsage(
    int? InputTokens, int? OutputTokens,
    int? CacheReadInputTokens, int? CacheCreationInputTokens)
{
    public int? TotalTokens => InputTokens is null && OutputTokens is null
        ? null
        : (InputTokens ?? 0) + (OutputTokens ?? 0);
}
