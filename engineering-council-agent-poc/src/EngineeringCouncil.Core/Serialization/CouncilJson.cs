using System.Text.Json;
using System.Text.Json.Serialization;

namespace EngineeringCouncil.Core.Serialization;

/// <summary>
/// Shared JSON settings so every artifact (findings.json, API responses, the
/// prompt payloads sent to the LLM) serializes identically: enums as strings,
/// camelCase, indented and forgiving on read.
/// </summary>
public static class CouncilJson
{
    public static JsonSerializerOptions Options { get; } = Create(indented: true);

    public static JsonSerializerOptions Compact { get; } = Create(indented: false);

    private static JsonSerializerOptions Create(bool indented) => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = indented,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
