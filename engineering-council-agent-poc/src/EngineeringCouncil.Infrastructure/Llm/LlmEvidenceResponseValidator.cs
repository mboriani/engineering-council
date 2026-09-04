using System.Text.Json;
using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Infrastructure.Llm;

/// <summary>Outcome of validating one structured LLM evidence response.</summary>
internal sealed record LlmResponseValidation
{
    public required bool IsValid { get; init; }

    /// <summary>The extracted, validated JSON payload (the interpreter's input).</summary>
    public string Json { get; init; } = string.Empty;

    public string SchemaVersion { get; init; } = string.Empty;
    public int ObservationCount { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];

    public string ErrorSummary => string.Join(" ", Errors);

    public static LlmResponseValidation Invalid(params string[] errors) => new() { IsValid = false, Errors = errors };
}

/// <summary>
/// Validates a structured LLM evidence response BEFORE it may enter the
/// interpretation pipeline. Nothing malformed, mis-versioned, off-discipline or
/// truncated is ever silently converted into observations.
/// </summary>
internal static class LlmEvidenceResponseValidator
{
    private const string SupportedSchemaVersion = "1.0";

    private static readonly HashSet<string> Severities =
        new(Enum.GetNames<FindingSeverity>(), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Confidences =
        new(Enum.GetNames<FindingConfidence>(), StringComparer.OrdinalIgnoreCase);

    public static LlmResponseValidation Validate(string? rawText, FindingCategory? requestedDiscipline, bool truncated)
    {
        if (truncated)
            return LlmResponseValidation.Invalid("The provider reported the response was truncated by the output token limit.");

        if (!StructuredJsonExtractor.TryExtract(rawText, out var json, out var extractionError))
            return LlmResponseValidation.Invalid(extractionError);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return LlmResponseValidation.Invalid($"The response was not valid JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            var errors = new List<string>();

            // schemaVersion — required and supported.
            var schemaVersion = root.TryGetProperty("schemaVersion", out var sv) && sv.ValueKind == JsonValueKind.String
                ? sv.GetString() ?? string.Empty
                : string.Empty;
            if (schemaVersion.Length == 0) errors.Add("The response is missing 'schemaVersion'.");
            else if (schemaVersion != SupportedSchemaVersion)
                errors.Add($"Unsupported response schemaVersion '{schemaVersion}' (expected '{SupportedSchemaVersion}').");

            // discipline — required, and must match what was requested.
            var discipline = root.TryGetProperty("discipline", out var d) && d.ValueKind == JsonValueKind.String
                ? d.GetString() ?? string.Empty
                : string.Empty;
            if (discipline.Length == 0) errors.Add("The response is missing 'discipline'.");
            else if (requestedDiscipline is { } requested
                     && !string.Equals(discipline, requested.ToString(), StringComparison.OrdinalIgnoreCase))
                errors.Add($"Discipline mismatch: requested '{requested}' but the response declared '{discipline}'.");

            // observations — required collection (empty is valid).
            if (!root.TryGetProperty("observations", out var observations) || observations.ValueKind != JsonValueKind.Array)
                return LlmResponseValidation.Invalid([.. errors, "The response is missing an 'observations' array."]);

            var index = 0;
            foreach (var observation in observations.EnumerateArray())
            {
                index++;
                if (observation.ValueKind != JsonValueKind.Object)
                {
                    errors.Add($"Observation #{index} is not an object.");
                    continue;
                }

                if (!HasNonEmptyString(observation, "title"))
                    errors.Add($"Observation #{index} is missing a 'title'.");

                if (observation.TryGetProperty("severity", out var severity)
                    && (severity.ValueKind != JsonValueKind.String || !Severities.Contains(severity.GetString() ?? "")))
                    errors.Add($"Observation #{index} has an invalid 'severity'.");

                if (observation.TryGetProperty("confidence", out var confidence)
                    && (confidence.ValueKind != JsonValueKind.String || !Confidences.Contains(confidence.GetString() ?? "")))
                    errors.Add($"Observation #{index} has an invalid 'confidence' (expected Low, Medium or High).");

                if (observation.TryGetProperty("fileReferences", out var files))
                {
                    if (files.ValueKind != JsonValueKind.Array)
                        errors.Add($"Observation #{index} has a malformed 'fileReferences' (expected an array).");
                    else
                        foreach (var file in files.EnumerateArray())
                            if (file.ValueKind != JsonValueKind.Object || !HasNonEmptyString(file, "path"))
                            {
                                errors.Add($"Observation #{index} has a file reference without a 'path'.");
                                break;
                            }
                }
            }

            return errors.Count > 0
                ? new LlmResponseValidation { IsValid = false, Json = json, SchemaVersion = schemaVersion, Errors = errors }
                : new LlmResponseValidation
                {
                    IsValid = true,
                    Json = json,
                    SchemaVersion = schemaVersion,
                    ObservationCount = observations.GetArrayLength()
                };
        }
    }

    private static bool HasNonEmptyString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(value.GetString());
}
