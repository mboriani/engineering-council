using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringCouncil.Agent;

/// <summary>
/// Base class for every specialized analyzer. As of Milestone 007 analyzers
/// consume normalized <see cref="EngineeringObservation"/>s (not raw evidence).
/// The base:
/// <list type="number">
///   <item>selects observations for this analyzer's <see cref="Category"/>;</item>
///   <item>correlates related observations (grouping by observation type);</item>
///   <item>produces <see cref="Finding"/>s preserving full provenance
///   (observation ids, providers, rules, files, evidence excerpts).</item>
/// </list>
/// A concrete analyzer normally only declares its <see cref="Name"/> and
/// <see cref="Category"/>; it may override <see cref="BuildFinding"/> for
/// discipline-specific reasoning.
///
/// Analyzers ONLY analyze: no evidence acquisition, no file writes, no reports.
/// </summary>
public abstract class ObservationBasedAnalyzer : IAnalyzerAgent
{
    private readonly ILogger _logger;

    protected ObservationBasedAnalyzer(ILogger? logger = null)
        => _logger = logger ?? NullLogger.Instance;

    public abstract string Name { get; }

    public abstract FindingCategory Category { get; }

    public Task<IReadOnlyList<Finding>> AnalyzeAsync(
        IReadOnlyList<EngineeringObservation> observations,
        AnalyzerContext context,
        CancellationToken cancellationToken = default)
    {
        var relevant = observations.Where(o => o.Discipline == Category).ToList();
        if (relevant.Count == 0)
            return Task.FromResult<IReadOnlyList<Finding>>([]);

        // Correlate: related observations of the same type collapse into one
        // finding. Deterministic and simple in v7 (no LLM, no consensus).
        var findings = new List<Finding>();
        var index = 1;
        foreach (var group in relevant.GroupBy(o => o.ObservationType, StringComparer.OrdinalIgnoreCase))
            findings.Add(BuildFinding(group.Key, group.ToList(), index++));

        _logger.LogInformation(
            "Analyzer '{Analyzer}' ({Category}) produced {Findings} finding(s) from {Obs} observation(s)",
            Name, Category, findings.Count, relevant.Count);

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }

    /// <summary>Builds one finding from a correlated group of observations, preserving provenance.</summary>
    protected virtual Finding BuildFinding(string observationType, IReadOnlyList<EngineeringObservation> obs, int index)
    {
        var primary = obs
            .OrderByDescending(o => o.Severity)
            .ThenByDescending(o => o.Confidence)
            .First();

        var providers = obs.Select(o => o.SourceProvider)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var rules = obs.Select(o => o.RuleId)
            .Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var files = obs.SelectMany(o => o.FileReferences)
            .GroupBy(r => (r.Path, r.StartLine, r.EndLine))
            .Select(g => g.First()).ToList();

        return new Finding
        {
            Id = $"F-{index:D3}",
            Title = primary.Title,
            Category = Category,
            Severity = obs.Max(o => o.Severity),
            Confidence = primary.Confidence,
            Summary = primary.Description,
            Description = string.Join("\n", obs
                .Select(o => o.Description).Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct(StringComparer.Ordinal)),
            Evidence = string.Join("\n", obs
                .Where(o => !string.IsNullOrWhiteSpace(o.EvidenceExcerpt))
                .Select(o => $"[{o.SourceProvider}] {o.EvidenceExcerpt}")),
            FileReferences = files,
            Recommendation = primary.RecommendationHint,
            Tags = obs.SelectMany(o => o.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SourceAgent = Name,
            SourceAgents = [Name],
            EvidenceProvider = primary.SourceProvider,
            ObservationIds = obs.Select(o => o.Id).ToList(),
            SourceRules = rules,
            SupportingProviders = providers,
            SupportingObservationCount = obs.Count,
            Status = FindingStatus.New
        };
    }
}
