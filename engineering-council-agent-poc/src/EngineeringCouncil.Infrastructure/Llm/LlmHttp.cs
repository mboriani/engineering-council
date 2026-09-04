using System.Net;

namespace EngineeringCouncil.Infrastructure.Llm;

/// <summary>Shared, provider-neutral HTTP failure categorization. Never echoes secrets.</summary>
internal static class LlmHttp
{
    /// <summary>Maps an HTTP status (plus a short, sanitized body hint) to a category.</summary>
    public static LlmErrorCategory Categorize(HttpStatusCode status, string? bodyHint)
    {
        var hint = bodyHint ?? string.Empty;

        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => LlmErrorCategory.Authentication,
            HttpStatusCode.TooManyRequests => LlmErrorCategory.RateLimit,
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => LlmErrorCategory.Timeout,
            HttpStatusCode.NotFound => LlmErrorCategory.UnsupportedModel,
            HttpStatusCode.RequestEntityTooLarge => LlmErrorCategory.ContextTooLarge,
            HttpStatusCode.BadRequest when MentionsContextLimit(hint) => LlmErrorCategory.ContextTooLarge,
            HttpStatusCode.BadRequest when hint.Contains("model", StringComparison.OrdinalIgnoreCase)
                                            && hint.Contains("not", StringComparison.OrdinalIgnoreCase)
                => LlmErrorCategory.UnsupportedModel,
            HttpStatusCode.BadRequest => LlmErrorCategory.InvalidRequest,
            >= HttpStatusCode.InternalServerError => LlmErrorCategory.ServerError,
            _ => LlmErrorCategory.Unknown
        };
    }

    private static bool MentionsContextLimit(string hint)
        => hint.Contains("context", StringComparison.OrdinalIgnoreCase)
           || hint.Contains("too long", StringComparison.OrdinalIgnoreCase)
           || hint.Contains("maximum context", StringComparison.OrdinalIgnoreCase)
           || hint.Contains("token", StringComparison.OrdinalIgnoreCase) && hint.Contains("limit", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A short, sanitized excerpt of an error body for diagnostics. Truncated and
    /// stripped of anything key-shaped so no credential can ever be echoed back.
    /// </summary>
    public static string Sanitize(string? body, int max = 300)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        var text = body.ReplaceLineEndings(" ").Trim();
        foreach (var secretish in new[] { "sk-", "api_key", "apiKey", "x-api-key", "Bearer " })
        {
            var index = text.IndexOf(secretish, StringComparison.OrdinalIgnoreCase);
            if (index >= 0) text = text[..index] + "[redacted]";
        }
        return text.Length <= max ? text : text[..max] + "…";
    }

    /// <summary>Reads a response header value (e.g. request id) without throwing.</summary>
    public static string? Header(HttpResponseMessage response, params string[] names)
    {
        foreach (var name in names)
            if (response.Headers.TryGetValues(name, out var values))
                return values.FirstOrDefault();
        return null;
    }
}
