using System.Text;
using System.Text.Json;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Llm;

namespace EngineeringCouncil.Infrastructure.Reconciliation;

/// <summary>
/// Real, provider-neutral targeted semantic reviewer (Milestone 014.4). It depends
/// ONLY on the generic <see cref="ILlmChatClient"/> transport seam — never on a
/// specific provider (Claude/OpenAI/DeepSeek/…) — so any registered chat client can
/// back it, and it reuses the SAME conservative JSON extraction already used for
/// evidence responses (<see cref="StructuredJsonExtractor"/>) rather than inventing
/// a second parsing path. This is deliberately NOT a general-purpose LLM framework:
/// one prompt, one strict two-field output contract, no retries, no repair — a
/// single failed attempt (timeout, malformed output, transport error) simply
/// throws, and the caller (<c>SemanticReconciliationBuilder</c>) treats ANY
/// exception as <see cref="SemanticReconciliationDecision.Inconclusive"/>.
/// </summary>
public sealed class LlmSemanticReconciliationReviewer : ISemanticReconciliationReviewer
{
    private static readonly JsonSerializerOptions DecisionJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private const string SystemInstructions =
        "You are assisting a deterministic engineering-review Council. You are NOT the reviewer of the " +
        "whole Council and you do NOT judge, rank, or vote. You review EXACTLY ONE already-reconciled, " +
        "ambiguous finding and answer ONE question: do the supplied observations describe the SAME " +
        "underlying engineering issue, or DIFFERENT engineering issues that were incorrectly reconciled " +
        "together? Rules:\n" +
        "- Do NOT discover new findings.\n" +
        "- Do NOT change severity, confidence, or any recommendation.\n" +
        "- Do NOT rank or vote between providers, and do NOT choose a winner.\n" +
        "- Do NOT rewrite the Engineering Review.\n" +
        "- Use ONLY the evidence supplied below; never invent files, symbols, or reasoning.\n" +
        "- If the evidence is insufficient to decide, return \"Inconclusive\" — never guess.\n" +
        "Return ONLY a JSON object (no Markdown, no fences, no prose) of the form:\n" +
        "{ \"decision\": \"SameIssue | DifferentIssues | Inconclusive\", \"reason\": \"short rationale\" }";

    private readonly ILlmChatClient _client;
    private readonly SemanticReconciliationOptions _options;

    public LlmSemanticReconciliationReviewer(ILlmChatClient client, SemanticReconciliationOptions options)
    {
        _client = client;
        _options = options;
    }

    public async Task<SemanticReconciliationResult> ReviewAsync(
        SemanticReconciliationRequest request, CancellationToken cancellationToken = default)
    {
        var completionRequest = new LlmCompletionRequest
        {
            SystemInstructions = SystemInstructions,
            UserContent = BuildUserContent(request),
            Model = _options.Model,
            MaxOutputTokens = _options.MaxOutputTokens
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.Timeout);

        LlmCompletionResponse response;
        try
        {
            response = await _client.CompleteAsync(completionRequest, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // the RUN was cancelled — propagate
        }
        catch (OperationCanceledException ex)
        {
            throw new LlmProviderException(LlmErrorCategory.Timeout,
                $"Semantic reviewer timed out after {_options.Timeout.TotalSeconds:0}s.", ex);
        }

        if (response.Truncated)
            throw new LlmProviderException(LlmErrorCategory.SchemaValidation,
                "Semantic reviewer response was truncated by the output token limit.");

        if (!StructuredJsonExtractor.TryExtract(response.Text, out var json, out var extractionError))
            throw new LlmProviderException(LlmErrorCategory.SchemaValidation, extractionError);

        SemanticDecisionDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<SemanticDecisionDto>(json, DecisionJsonOptions);
        }
        catch (JsonException ex)
        {
            throw new LlmProviderException(LlmErrorCategory.SchemaValidation,
                "Semantic reviewer response was not valid JSON.", ex);
        }

        if (dto?.Decision is null || !Enum.TryParse<SemanticReconciliationDecision>(dto.Decision, ignoreCase: true, out var decision))
            throw new LlmProviderException(LlmErrorCategory.SchemaValidation,
                $"Semantic reviewer returned an unrecognized decision '{dto?.Decision}'.");

        return new SemanticReconciliationResult { Decision = decision, Reason = dto.Reason?.Trim() ?? string.Empty };
    }

    private static string BuildUserContent(SemanticReconciliationRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Finding: {request.Title}");
        sb.AppendLine($"Discipline: {request.Category}");
        sb.AppendLine($"Supporting providers: {string.Join(", ", request.SupportingProviders)}");
        sb.AppendLine();
        sb.AppendLine("Observations:");
        foreach (var observation in request.Observations)
        {
            sb.AppendLine($"- Provider: {observation.Provider}");
            sb.AppendLine($"  Type: {observation.ObservationType}");
            if (observation.FileReferences.Count > 0)
                sb.AppendLine($"  Files: {string.Join(", ", observation.FileReferences)}");
            if (!string.IsNullOrWhiteSpace(observation.EvidenceExcerpt))
                sb.AppendLine($"  Evidence: {observation.EvidenceExcerpt}");
        }
        return sb.ToString();
    }

    private sealed class SemanticDecisionDto
    {
        public string? Decision { get; set; }
        public string? Reason { get; set; }
    }
}
