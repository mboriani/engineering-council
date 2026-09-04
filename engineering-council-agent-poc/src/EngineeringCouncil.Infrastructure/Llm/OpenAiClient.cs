using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EngineeringCouncil.Core.Abstractions;

namespace EngineeringCouncil.Infrastructure.Llm;

/// <summary>
/// Real OpenAI transport (Chat Completions). This is the ONLY place OpenAI's wire
/// format exists. A code-focused model is simply this provider's configured model.
/// The "Codex" name is NOT an alias for this provider (Milestone 013.4): "Codex" now
/// selects the real agentic Codex CLI provider. The API key is read from the configured
/// environment variable per call and is never logged, stored, or serialized.
/// </summary>
public sealed class OpenAiClient : IOpenAiClient, IDisposable
{
    private readonly OpenAiProviderOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public OpenAiClient(OpenAiProviderOptions options, HttpClient? httpClient = null)
    {
        _options = options;
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    public string ProviderId => "openai";

    public async Task<LlmCompletionResponse> CompleteAsync(
        LlmCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var apiKey = _options.ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new LlmProviderException(LlmErrorCategory.Configuration,
                $"OpenAI requires the environment variable {_options.ApiKeyEnvironmentVariable}.");

        var baseUrl = (string.IsNullOrWhiteSpace(_options.Endpoint) ? "https://api.openai.com" : _options.Endpoint!).TrimEnd('/');

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/chat/completions")
        {
            Content = JsonContent.Create(new OpenAiRequest
            {
                Model = request.Model,
                MaxCompletionTokens = request.MaxOutputTokens,
                Messages =
                [
                    new OpenAiMessage { Role = "system", Content = request.SystemInstructions },
                    new OpenAiMessage { Role = "user", Content = request.UserContent }
                ]
            })
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new LlmProviderException(LlmErrorCategory.Cancelled, "The OpenAI request was cancelled.");
        }
        catch (OperationCanceledException ex)
        {
            throw new LlmProviderException(LlmErrorCategory.Timeout, "The OpenAI request timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmProviderException(LlmErrorCategory.Network, "The OpenAI request failed to reach the service.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var category = LlmHttp.Categorize(response.StatusCode, body);
                throw new LlmProviderException(category,
                    $"OpenAI returned {(int)response.StatusCode} ({category}): {LlmHttp.Sanitize(body)}");
            }

            OpenAiResponse? payload;
            try
            {
                payload = JsonSerializer.Deserialize<OpenAiResponse>(body);
            }
            catch (JsonException ex)
            {
                throw new LlmProviderException(LlmErrorCategory.Unknown, "OpenAI returned an unreadable response envelope.", ex);
            }

            var choice = payload?.Choices?.FirstOrDefault();

            return new LlmCompletionResponse
            {
                Text = choice?.Message?.Content ?? string.Empty,
                ResponseId = payload?.Id,
                RequestId = LlmHttp.Header(response, "x-request-id", "request-id"),
                InputTokens = payload?.Usage?.PromptTokens,
                OutputTokens = payload?.Usage?.CompletionTokens,
                FinishReason = choice?.FinishReason,
                Truncated = string.Equals(choice?.FinishReason, "length", StringComparison.OrdinalIgnoreCase)
            };
        }
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    // ── OpenAI wire shapes (internal to this adapter) ─────────────────────────

    private sealed record OpenAiRequest
    {
        [JsonPropertyName("model")] public required string Model { get; init; }
        [JsonPropertyName("max_completion_tokens")] public required int MaxCompletionTokens { get; init; }
        [JsonPropertyName("messages")] public required IReadOnlyList<OpenAiMessage> Messages { get; init; }
    }

    private sealed record OpenAiMessage
    {
        [JsonPropertyName("role")] public required string Role { get; init; }
        [JsonPropertyName("content")] public required string Content { get; init; }
    }

    private sealed record OpenAiResponse
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("choices")] public List<OpenAiChoice>? Choices { get; init; }
        [JsonPropertyName("usage")] public OpenAiUsage? Usage { get; init; }
    }

    private sealed record OpenAiChoice
    {
        [JsonPropertyName("message")] public OpenAiMessageContent? Message { get; init; }
        [JsonPropertyName("finish_reason")] public string? FinishReason { get; init; }
    }

    private sealed record OpenAiMessageContent
    {
        [JsonPropertyName("content")] public string? Content { get; init; }
    }

    private sealed record OpenAiUsage
    {
        [JsonPropertyName("prompt_tokens")] public int? PromptTokens { get; init; }
        [JsonPropertyName("completion_tokens")] public int? CompletionTokens { get; init; }
    }
}
