namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// A generic, provider-agnostic summary of one deterministic static-analysis
/// source that contributed evidence to a run (e.g. a SARIF tool run). Part of the
/// Engineering Review Package. Introduced with the native SARIF source
/// (Milestone 009); no SARIF-specific types leak into the central model.
/// </summary>
public sealed record StaticAnalysisSource
{
    /// <summary>The analysis tool that produced the evidence (e.g. "ESLint", "CodeQL").</summary>
    public required string Tool { get; init; }

    /// <summary>Tool version, when reported.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>How many results the tool reported (imported from the source).</summary>
    public int ImportedResults { get; init; }

    /// <summary>How many normalized observations were generated from those results.</summary>
    public int GeneratedObservations { get; init; }

    /// <summary>
    /// Rolls up the static-analysis sources from the run's static-analyzer evidence
    /// and the observations produced from it. Each evidence item that reports a tool
    /// name is one source row; observations are attributed by <c>SourceEvidenceId</c>.
    /// </summary>
    public static IReadOnlyList<StaticAnalysisSource> From(
        IReadOnlyList<Evidence> evidence,
        IReadOnlyList<EngineeringObservation> observations)
    {
        var observationsByEvidence = observations
            .GroupBy(o => o.SourceEvidenceId)
            .ToDictionary(g => g.Key, g => g.Count());

        return evidence
            .Where(e => e.ProviderType == EvidenceProviderType.StaticAnalyzer && e.Success)
            .Select(e =>
            {
                e.Metadata.TryGetValue("tool", out var tool);
                e.Metadata.TryGetValue("toolVersion", out var version);
                var imported = e.Metadata.TryGetValue("resultCount", out var rc)
                    && int.TryParse(rc, out var n) ? n : 0;

                return new StaticAnalysisSource
                {
                    Tool = string.IsNullOrWhiteSpace(tool) ? e.ProviderName : tool!,
                    Version = version ?? string.Empty,
                    ImportedResults = imported,
                    GeneratedObservations = observationsByEvidence.GetValueOrDefault(e.Id)
                };
            })
            .OrderBy(s => s.Tool, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
