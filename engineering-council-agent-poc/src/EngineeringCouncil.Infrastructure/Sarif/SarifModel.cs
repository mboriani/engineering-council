using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace EngineeringCouncil.Infrastructure.Sarif;

// Minimal SARIF 2.1.0 object model — only the subset Milestone 009 supports:
// runs[] · tool.driver (+ rules) · results[] · locations[] · physicalLocation ·
// artifactLocation · region. codeFlows/threadFlows/graphs/suppressions/fixes/
// webRequests/webResponses are intentionally not modeled (a later milestone).
//
// This structural model is shared by the SARIF provider (which splits a log into
// per-run evidence and counts) and the SARIF interpreter (which maps results to
// observations). Only the interpreter assigns *meaning*; this model is pure shape.

internal static class SarifJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

internal sealed class SarifLog
{
    public string? Version { get; set; }
    public List<SarifRun> Runs { get; set; } = [];

    /// <summary>Parses a SARIF log; returns null when the text is not valid JSON.</summary>
    public static SarifLog? TryParse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SarifLog>(json, SarifJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed class SarifRun
{
    public SarifTool? Tool { get; set; }
    public List<SarifResult> Results { get; set; } = [];
    public List<SarifArtifact> Artifacts { get; set; } = [];
    public List<SarifInvocation> Invocations { get; set; } = [];

    public SarifDriver? Driver => Tool?.Driver;
}

internal sealed class SarifTool
{
    public SarifDriver? Driver { get; set; }
}

internal sealed class SarifDriver
{
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? SemanticVersion { get; set; }
    public string? InformationUri { get; set; }
    public List<SarifRule> Rules { get; set; } = [];

    public string? EffectiveVersion => Version ?? SemanticVersion;
}

internal sealed class SarifRule
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public SarifMessageString? ShortDescription { get; set; }
    public SarifMessageString? FullDescription { get; set; }
    public string? HelpUri { get; set; }
    public SarifReportingConfiguration? DefaultConfiguration { get; set; }

    /// <summary>Free-form rule metadata (tags, security-severity, precision, cwe, …).</summary>
    public JsonObject? Properties { get; set; }
}

internal sealed class SarifReportingConfiguration
{
    public string? Level { get; set; }
}

internal sealed class SarifResult
{
    public string? RuleId { get; set; }
    public int? RuleIndex { get; set; }
    public string? Level { get; set; }
    public SarifMessage? Message { get; set; }
    public List<SarifLocation> Locations { get; set; } = [];
    public JsonObject? Properties { get; set; }
}

internal sealed class SarifLocation
{
    public SarifPhysicalLocation? PhysicalLocation { get; set; }
}

internal sealed class SarifPhysicalLocation
{
    public SarifArtifactLocation? ArtifactLocation { get; set; }
    public SarifRegion? Region { get; set; }
}

internal sealed class SarifArtifactLocation
{
    public string? Uri { get; set; }
    public int? Index { get; set; }
}

internal sealed class SarifRegion
{
    public int? StartLine { get; set; }
    public int? StartColumn { get; set; }
    public int? EndLine { get; set; }
    public int? EndColumn { get; set; }
    public SarifMessageString? Snippet { get; set; }
}

internal sealed class SarifArtifact
{
    public SarifArtifactLocation? Location { get; set; }
}

internal sealed class SarifInvocation
{
    public bool? ExecutionSuccessful { get; set; }
    public string? CommandLine { get; set; }
    public DateTimeOffset? StartTimeUtc { get; set; }
    public DateTimeOffset? EndTimeUtc { get; set; }
}

/// <summary>SARIF multiformatMessageString ({ "text": "…" }).</summary>
internal sealed class SarifMessageString
{
    public string? Text { get; set; }
}

/// <summary>SARIF message ({ "text": "…" }); id/arguments are not modeled yet.</summary>
internal sealed class SarifMessage
{
    public string? Text { get; set; }
}
