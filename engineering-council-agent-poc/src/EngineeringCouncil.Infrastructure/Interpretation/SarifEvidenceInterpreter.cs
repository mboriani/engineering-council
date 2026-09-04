using System.Text.Json;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Sarif;

namespace EngineeringCouncil.Infrastructure.Interpretation;

// This namespace has a sibling ".Evidence" namespace that shadows the domain
// Evidence type, so it is referenced as Core.Domain.Evidence throughout.

/// <summary>
/// Interprets SARIF 2.1.0 evidence (one imported tool run) into normalized
/// <see cref="EngineeringObservation"/>s. This is the ONLY component that
/// understands the SARIF schema: analyzers never see SARIF. Every result becomes
/// one observation with a deterministic discipline/type/severity/confidence
/// mapping (see <see cref="SarifMappings"/>) and full rule metadata + provenance.
/// </summary>
public sealed class SarifEvidenceInterpreter : IEvidenceInterpreter
{
    public string ProviderName => "sarif";

    public bool CanInterpret(Core.Domain.Evidence evidence)
        => evidence.Success
           && evidence.ProviderType == EvidenceProviderType.StaticAnalyzer
           && (evidence.Metadata.TryGetValue("format", out var format)
                   && string.Equals(format, "sarif", StringComparison.OrdinalIgnoreCase)
               || string.Equals(evidence.ProviderName, "SARIF", StringComparison.OrdinalIgnoreCase));

    public Task<IReadOnlyList<EngineeringObservation>> InterpretAsync(
        Core.Domain.Evidence evidence, AnalyzerContext context, CancellationToken cancellationToken = default)
    {
        var log = SarifLog.TryParse(evidence.RawResponse);
        var run = log?.Runs.FirstOrDefault();
        if (run is null)
            return Task.FromResult<IReadOnlyList<EngineeringObservation>>([]);

        var rulesById = (run.Driver?.Rules ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.Id))
            .GroupBy(r => r.Id!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var rulesList = run.Driver?.Rules ?? [];
        var tool = run.Driver?.Name ?? evidence.Metadata.GetValueOrDefault("tool") ?? "Unknown";
        var toolVersion = run.Driver?.EffectiveVersion ?? evidence.Metadata.GetValueOrDefault("toolVersion") ?? string.Empty;

        var results = new List<EngineeringObservation>();
        var index = 1;
        foreach (var result in run.Results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(MapResult(result, ResolveRule(result, rulesById, rulesList), evidence, tool, toolVersion, index++));
        }

        return Task.FromResult<IReadOnlyList<EngineeringObservation>>(results);
    }

    private static SarifRule? ResolveRule(
        SarifResult result, IReadOnlyDictionary<string, SarifRule> byId, IReadOnlyList<SarifRule> list)
    {
        if (!string.IsNullOrWhiteSpace(result.RuleId) && byId.TryGetValue(result.RuleId!, out var rule))
            return rule;
        if (result.RuleIndex is { } i && i >= 0 && i < list.Count)
            return list[i];
        return null;
    }

    private static EngineeringObservation MapResult(
        SarifResult result, SarifRule? rule, Core.Domain.Evidence evidence, string tool, string toolVersion, int index)
    {
        var ruleId = result.RuleId ?? rule?.Id ?? string.Empty;
        var ruleName = rule?.Name ?? ruleId;
        var ruleDescription = rule?.ShortDescription?.Text ?? rule?.FullDescription?.Text ?? string.Empty;
        var tags = ExtractTags(rule);

        // Combined deterministic signal used for discipline/type mapping. Built from
        // rule id, name, description and tags (which carry taxonomy such as CWE) —
        // NOT the raw properties JSON, whose keys (e.g. "security-severity") would
        // cause false keyword matches.
        var signal = string.Join(' ', new[] { ruleId, ruleName, ruleDescription, string.Join(' ', tags) });

        var discipline = SarifMappings.Discipline(signal);
        var observationType = SarifMappings.ObservationType(signal);
        var severity = SarifMappings.Severity(result.Level ?? rule?.DefaultConfiguration?.Level);

        // File references / lines / columns from the physical locations.
        var fileRefs = new List<FileReference>();
        var lines = new List<int>();
        var columns = new List<int>();
        string? excerpt = null;
        foreach (var location in result.Locations)
        {
            var phys = location.PhysicalLocation;
            var uri = phys?.ArtifactLocation?.Uri;
            var region = phys?.Region;
            if (!string.IsNullOrWhiteSpace(uri))
                fileRefs.Add(new FileReference
                {
                    Path = NormalizePath(uri!),
                    StartLine = region?.StartLine is > 0 ? region!.StartLine : null,
                    EndLine = region?.EndLine is > 0 ? region!.EndLine : null
                });
            if (region?.StartLine is > 0) lines.Add(region.StartLine!.Value);
            if (region?.StartColumn is > 0) columns.Add(region.StartColumn!.Value);
            excerpt ??= region?.Snippet?.Text;
        }

        // Confidence: count concrete data deficiencies (never invented heuristics).
        var deficiencies = 0;
        if (string.IsNullOrWhiteSpace(ruleId)) deficiencies++;                                   // malformed rule
        if (fileRefs.Count == 0) deficiencies++;                                                 // missing file / location
        if (lines.Count == 0) deficiencies++;                                                    // incomplete location
        if (string.IsNullOrWhiteSpace(result.Message?.Text) && string.IsNullOrWhiteSpace(ruleDescription)) deficiencies++; // incomplete metadata
        if (!SarifMappings.IsKnownLevel(result.Level ?? rule?.DefaultConfiguration?.Level)) deficiencies++;               // incomplete metadata
        var (confidence, score) = SarifMappings.Confidence(deficiencies);

        var description = result.Message?.Text ?? ruleDescription;
        var metadata = new Dictionary<string, string>
        {
            ["provider"] = "SARIF",
            ["tool"] = tool,
            ["toolVersion"] = toolVersion,
            ["ruleId"] = ruleId,
            ["confidenceScore"] = score.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(ruleName)) metadata["ruleName"] = ruleName;
        if (!string.IsNullOrWhiteSpace(ruleDescription)) metadata["ruleDescription"] = ruleDescription;
        if (!string.IsNullOrWhiteSpace(rule?.HelpUri)) metadata["helpUri"] = rule!.HelpUri!;
        if (tags.Count > 0) metadata["tags"] = string.Join(", ", tags);
        var props = PropertiesText(rule);
        if (!string.IsNullOrWhiteSpace(props)) metadata["properties"] = props;

        return new EngineeringObservation
        {
            Id = $"{evidence.Id}-{index}",
            ObservationType = observationType,
            Discipline = discipline,
            Title = string.IsNullOrWhiteSpace(ruleName) ? (ruleId is "" ? "SARIF result" : ruleId) : ruleName,
            Description = description,
            Severity = severity,
            Confidence = confidence,
            SourceProvider = "SARIF",
            SourceProviderType = EvidenceProviderType.StaticAnalyzer,
            SourceEvidenceId = evidence.Id,
            RuleId = string.IsNullOrWhiteSpace(ruleId) ? null : ruleId,
            FileReferences = fileRefs,
            LineReferences = lines.Distinct().ToList(),
            ColumnReferences = columns.Distinct().ToList(),
            EvidenceExcerpt = excerpt?.Trim() ?? string.Empty,
            Tags = tags,
            Metadata = metadata,
            // Acquisition provenance carried from the (repository-scoped) evidence.
            AcquisitionScope = evidence.AcquisitionScope,
            RequestedDiscipline = evidence.RequestedDiscipline,
            AcquisitionCorrelationId = evidence.CorrelationId,
            AcquisitionStepId = evidence.AcquisitionStepId,
            ContextFileCount = evidence.ContextFileCount,
            ContextSelectionStrategy = evidence.ContextSelectionStrategy
        };
    }

    private static IReadOnlyList<string> ExtractTags(SarifRule? rule)
    {
        if (rule?.Properties is null || !rule.Properties.TryGetPropertyValue("tags", out var node) || node is null)
            return [];
        try
        {
            return node.AsArray()
                .Where(n => n is not null)
                .Select(n => n!.GetValue<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    private static string PropertiesText(SarifRule? rule)
        => rule?.Properties is null ? string.Empty : rule.Properties.ToJsonString();

    private static string NormalizePath(string uri)
    {
        var path = uri;
        if (path.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)) path = path[8..];
        else if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) path = path[7..];
        return path.Replace('\\', '/').TrimStart('.', '/');
    }
}
