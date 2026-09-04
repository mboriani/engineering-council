using System.Text.Json;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Core.Serialization;

namespace EngineeringCouncil.Infrastructure.Interpretation;

// This namespace has a sibling ".Evidence" namespace that shadows the domain
// Evidence type, so it is referenced as Core.Domain.Evidence throughout.

/// <summary>
/// Interprets the structured JSON output returned by LLM-style providers (Mock
/// and Claude) and by agentic sources into normalized
/// <see cref="EngineeringObservation"/>s. It parses the <c>{ "observations": [ … ] }</c>
/// envelope, validates required fields, drops file references not present in the
/// snapshot (the "do not invent files" guard), and lowers confidence when fields
/// or evidence are incomplete. Agentic sources return the same structured envelope,
/// so they reuse this interpretation logic unchanged.
/// </summary>
public sealed class StructuredLlmEvidenceInterpreter : IEvidenceInterpreter
{
    public string ProviderName => "structured-llm";

    public bool CanInterpret(Core.Domain.Evidence evidence)
        => evidence.Success
           && evidence.ProviderType is EvidenceProviderType.LLM or EvidenceProviderType.Agentic
           && !string.IsNullOrWhiteSpace(evidence.RawResponse)
           && evidence.RawResponse.Contains("observations", StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<EngineeringObservation>> InterpretAsync(
        Core.Domain.Evidence evidence, AnalyzerContext context, CancellationToken cancellationToken = default)
    {
        var json = JsonExtraction.ExtractJsonObject(evidence.RawResponse);
        if (json is null) return Task.FromResult<IReadOnlyList<EngineeringObservation>>([]);

        ObservationsEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ObservationsEnvelope>(json, CouncilJson.Options);
        }
        catch (JsonException)
        {
            return Task.FromResult<IReadOnlyList<EngineeringObservation>>([]);
        }

        if (envelope?.Observations is not { Count: > 0 })
            return Task.FromResult<IReadOnlyList<EngineeringObservation>>([]);

        var knownFiles = new HashSet<string>(
            context.Snapshot.Files.Select(f => f.RelativePath), StringComparer.OrdinalIgnoreCase);

        var results = new List<EngineeringObservation>();
        var index = 1;
        foreach (var dto in envelope.Observations)
        {
            if (string.IsNullOrWhiteSpace(dto.Title)) continue; // required field

            var claimedRefs = (dto.FileReferences ?? []).Count(r => !string.IsNullOrWhiteSpace(r.Path));
            var validRefs = (dto.FileReferences ?? [])
                .Where(r => !string.IsNullOrWhiteSpace(r.Path))
                .Select(r => new { Ref = r, Path = NormalizePath(r.Path!) })
                .Where(x => knownFiles.Contains(x.Path)) // drop invented files
                .Select(x => new FileReference
                {
                    Path = x.Path,
                    StartLine = x.Ref.StartLine is > 0 ? x.Ref.StartLine : null,
                    EndLine = x.Ref.EndLine is > 0 ? x.Ref.EndLine : null
                })
                .ToList();

            var confidence = ParseEnum(dto.Confidence, FindingConfidence.Medium);

            // Lower confidence when the observation is incomplete or referenced files were invented.
            if (string.IsNullOrWhiteSpace(dto.Description)) confidence = Lower(confidence);
            if (claimedRefs > 0 && validRefs.Count == 0) confidence = Lower(confidence);

            // An unrecognized discipline string is NEVER rewritten to a guessed one:
            // it maps to Unknown so no analyzer claims it and no discipline's metrics
            // are inflated by evidence whose discipline could not be determined.
            var discipline = ParseEnum(dto.Discipline, FindingCategory.Unknown);
            var tags = dto.Tags?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? [];

            // Calibration signal only (Milestone 012): how many claimed file references
            // did not exist in the snapshot and were dropped by the guard above.
            var droppedReferences = claimedRefs - validRefs.Count;
            var metadata = droppedReferences > 0
                ? new Dictionary<string, string> { ["droppedFileReferences"] = droppedReferences.ToString() }
                : [];

            // Discipline-mismatch rule: a discipline-scoped request that returns an
            // observation for a DIFFERENT discipline is not silently rewritten — the
            // observation keeps its own discipline, but confidence is lowered and it
            // is tagged so downstream analyzers and telemetry can see the drift.
            if (evidence.AcquisitionScope == EvidenceAcquisitionScope.Discipline
                && evidence.RequestedDiscipline is { } requested
                && discipline != requested)
            {
                confidence = Lower(confidence);
                tags = [.. tags, "discipline-mismatch"];
            }

            results.Add(new EngineeringObservation
            {
                Id = $"{evidence.Id}-{index}",
                ObservationType = string.IsNullOrWhiteSpace(dto.Type) ? ObservationTypes.GeneralObservation : dto.Type!.Trim(),
                Discipline = discipline,
                Title = dto.Title!.Trim(),
                Description = dto.Description?.Trim() ?? string.Empty,
                Severity = ParseEnum(dto.Severity, FindingSeverity.Medium),
                Confidence = confidence,
                SourceProvider = evidence.ProviderName,
                SourceProviderType = evidence.ProviderType,
                SourceEvidenceId = evidence.Id,
                RuleId = string.IsNullOrWhiteSpace(dto.RuleId) ? null : dto.RuleId!.Trim(),
                FileReferences = validRefs,
                SymbolReferences = dto.SymbolReferences?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? [],
                LineReferences = dto.LineReferences ?? [],
                EvidenceExcerpt = dto.EvidenceExcerpt?.Trim() ?? string.Empty,
                RecommendationHint = dto.RecommendationHint?.Trim() ?? string.Empty,
                Tags = tags,
                Metadata = metadata,
                // Acquisition provenance carried from the evidence request/step.
                AcquisitionScope = evidence.AcquisitionScope,
                RequestedDiscipline = evidence.RequestedDiscipline,
                AcquisitionCorrelationId = evidence.CorrelationId,
                AcquisitionStepId = evidence.AcquisitionStepId,
                ContextFileCount = evidence.ContextFileCount,
                ContextSelectionStrategy = evidence.ContextSelectionStrategy
            });
            index++;
        }

        return Task.FromResult<IReadOnlyList<EngineeringObservation>>(results);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('.', '/');

    private static FindingConfidence Lower(FindingConfidence c)
        => c == FindingConfidence.Low ? FindingConfidence.Low : (FindingConfidence)((int)c - 1);

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value?.Trim(), ignoreCase: true, out var parsed) ? parsed : fallback;
}
