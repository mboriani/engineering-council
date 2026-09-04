using EngineeringCouncil.Infrastructure.Llm;

namespace EngineeringCouncil.Tests.Fakes;

/// <summary>
/// A scripted test double for the LLM transports. It implements BOTH client seams so
/// a single fake can stand in for Claude or OpenAI, records every request, and replays
/// a queued script of responses/failures. The whole unit-test suite runs through this:
/// no network, no credentials, no paid model calls.
/// </summary>
internal sealed class ScriptedLlmClient : IClaudeClient, IOpenAiClient
{
    private readonly Queue<Func<LlmCompletionResponse>> _script = new();

    public ScriptedLlmClient(string providerId = "fake") => ProviderId = providerId;

    public string ProviderId { get; }

    /// <summary>Every request the provider issued (prompt inspection + call counting).</summary>
    public List<LlmCompletionRequest> Requests { get; } = [];

    public int CallCount => Requests.Count;

    public ScriptedLlmClient Returns(string text, int? inputTokens = null, int? outputTokens = null,
        string? finishReason = null, bool truncated = false, string? responseId = null, string? requestId = null)
    {
        _script.Enqueue(() => new LlmCompletionResponse
        {
            Text = text,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            FinishReason = finishReason,
            Truncated = truncated,
            ResponseId = responseId,
            RequestId = requestId
        });
        return this;
    }

    public ScriptedLlmClient Fails(LlmErrorCategory category, string message = "scripted failure")
    {
        _script.Enqueue(() => throw new LlmProviderException(category, message));
        return this;
    }

    /// <summary>Simulates a transport-level timeout (the client's own cancellation).</summary>
    public ScriptedLlmClient TimesOut()
    {
        _script.Enqueue(() => throw new OperationCanceledException());
        return this;
    }

    /// <summary>
    /// Simulates a transport that observed a run cancellation while a call was in
    /// flight: it cancels the run's token and reports the failure as Cancelled.
    /// </summary>
    public ScriptedLlmClient CancelsThenFails(CancellationTokenSource cts)
    {
        _script.Enqueue(() =>
        {
            cts.Cancel();
            throw new LlmProviderException(LlmErrorCategory.Cancelled, "cancelled by the run");
        });
        return this;
    }

    public Task<LlmCompletionResponse> CompleteAsync(LlmCompletionRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (_script.Count == 0)
            throw new InvalidOperationException("ScriptedLlmClient received an unscripted call.");

        return Task.FromResult(_script.Dequeue()());
    }
}
