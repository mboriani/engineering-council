using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EngineeringCouncil.Core.Abstractions;

namespace EngineeringCouncil.Infrastructure.Llm;

/// <summary>
/// Real Anthropic Claude transport (Messages API). This is the ONLY place Claude's
/// wire format exists: it maps the provider-neutral request/response to Anthropic's
/// shape and categorizes failures. The API key is read from the configured
/// environment variable per call and is never logged, stored, or serialized.
/// </summary>
public sealed class ClaudeClient : IClaudeClient, IDisposable
{
    private const string AnthropicVersion = "2023-06-01";

    private readonly ClaudeProviderOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public ClaudeClient(ClaudeProviderOptions options, HttpClient? httpClient = null)
    {
        _options = options;
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    public string ProviderId => "claude";

    public async Task<LlmCompletionResponse> CompleteAsync(
        LlmCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var apiKey = _options.ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new LlmProviderException(LlmErrorCategory.Configuration,
                $"Claude requires the environment variable {_options.ApiKeyEnvironmentVariable}.");

        var baseUrl = (string.IsNullOrWhiteSpace(_options.Endpoint) ? "https://api.anthropic.com" : _options.Endpoint!).TrimEnd('/');

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/messages")
        {
            Content = JsonContent.Create(new ClaudeRequest
            {
                Model = request.Model,
                MaxTokens = request.MaxOutputTokens,
                System = request.SystemInstructions,
                Messages = [new ClaudeMessage { Role = "user", Content = request.UserContent }]
            })
        };
        message.Headers.Add("x-api-key", apiKey);
        message.Headers.Add("anthropic-version", AnthropicVersion);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new LlmProviderException(LlmErrorCategory.Cancelled, "The Claude request was cancelled.");
        }
        catch (OperationCanceledException ex)
        {
            throw new LlmProviderException(LlmErrorCategory.Timeout, "The Claude request timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmProviderException(LlmErrorCategory.Network, "The Claude request failed to reach the service.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var category = LlmHttp.Categorize(response.StatusCode, body);
                throw new LlmProviderException(category,
                    $"Claude returned {(int)response.StatusCode} ({category}): {LlmHttp.Sanitize(body)}");
            }

            ClaudeResponse? payload;
            try
            {
                payload = JsonSerializer.Deserialize<ClaudeResponse>(body);
            }
            catch (JsonException ex)
            {
                throw new LlmProviderException(LlmErrorCategory.Unknown, "Claude returned an unreadable response envelope.", ex);
            }

            var text = string.Concat(payload?.Content?
                .Where(c => string.Equals(c.Type, "text", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Text) ?? []);

            return new LlmCompletionResponse
            {
                Text = text,
                ResponseId = payload?.Id,
                RequestId = LlmHttp.Header(response, "request-id", "x-request-id"),
                InputTokens = payload?.Usage?.InputTokens,
                OutputTokens = payload?.Usage?.OutputTokens,
                FinishReason = payload?.StopReason,
                Truncated = string.Equals(payload?.StopReason, "max_tokens", StringComparison.OrdinalIgnoreCase)
            };
        }
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    // ── Anthropic wire shapes (internal to this adapter) ──────────────────────

    private sealed record ClaudeRequest
    {
        [JsonPropertyName("model")] public required string Model { get; init; }
        [JsonPropertyName("max_tokens")] public required int MaxTokens { get; init; }
        [JsonPropertyName("system")] public required string System { get; init; }
        [JsonPropertyName("messages")] public required IReadOnlyList<ClaudeMessage> Messages { get; init; }
    }

    private sealed record ClaudeMessage
    {
        [JsonPropertyName("role")] public required string Role { get; init; }
        [JsonPropertyName("content")] public required string Content { get; init; }
    }

    private sealed record ClaudeResponse
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("content")] public List<ClaudeContent>? Content { get; init; }
        [JsonPropertyName("stop_reason")] public string? StopReason { get; init; }
        [JsonPropertyName("usage")] public ClaudeUsage? Usage { get; init; }
    }

    private sealed record ClaudeContent
    {
        [JsonPropertyName("type")] public string? Type { get; init; }
        [JsonPropertyName("text")] public string? Text { get; init; }
    }

    private sealed record ClaudeUsage
    {
        [JsonPropertyName("input_tokens")] public int? InputTokens { get; init; }
        [JsonPropertyName("output_tokens")] public int? OutputTokens { get; init; }
    }
}
