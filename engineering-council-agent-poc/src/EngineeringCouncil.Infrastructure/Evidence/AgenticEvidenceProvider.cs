using System.Diagnostics;
using System.Text.Json;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Infrastructure.Evidence;

/// <summary>
/// A no-network agentic evidence provider that demonstrates the Milestone 013.1
/// contract end to end. It is <see cref="EvidenceProviderType.Agentic"/> and
/// <b>Discipline</b>-scoped: it receives the repository ROOT + file structure and
/// the discipline instructions — but NOT a bundled controlled context, NOT the
/// file bodies, and NOT a context fingerprint (the council never saw the agent's
/// effective context).
///
/// It "explores the repository itself": in this mock a deterministic, rule-based
/// agent reads the FILE MAP (paths/extensions/line counts only) and returns one
/// structured <c>observations</c> envelope item per file it deems relevant to the
/// discipline. A real agentic adapter (Codex / Claude Code / opencode) performs
/// the same task by reading the repository from disk at
/// <c>request.RepositorySnapshot.RootPath</c> — the contract is identical.
/// </summary>
public sealed class AgenticEvidenceProvider : IEvidenceProvider
{
    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public EvidenceProviderMetadata Metadata => new()
    {
        Name = "Agentic",
        ProviderType = EvidenceProviderType.Agentic,
        DefaultAcquisitionScope = EvidenceAcquisitionScope.Discipline,
        RequiresAnalyzerInstructions = true,
        SupportsRepositoryWideAnalysis = false,
        Version = "agentic-explorer-v1"
    };

    public Task<IReadOnlyList<Core.Domain.Evidence>> CollectAsync(
        EvidenceRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var discipline = request.Discipline;

        // The agent explores the repository itself. Only the file map is available
        // here (root + structure; the council bundled no context and no contents).
        var explored = discipline is { } d
            ? request.RepositorySnapshot.Files.Where(f => IsRelevant(d, f)).Take(3).ToList()
            : request.RepositorySnapshot.Files.Take(3).ToList();

        var json = BuildObservationsJson(discipline, explored);
        stopwatch.Stop();

        IReadOnlyList<Core.Domain.Evidence> result =
        [
            new Core.Domain.Evidence
            {
                ProviderName = Metadata.Name,
                ProviderId = "agentic",
                ProviderVersion = Metadata.Version!,
                ProviderType = EvidenceProviderType.Agentic,
                RawResponse = json,
                Confidence = 0.5,
                ExecutionTime = stopwatch.Elapsed,
                Success = true,
                Metadata = new Dictionary<string, string>
                {
                    ["agentRuntime"] = Metadata.Version!,
                    ["discipline"] = discipline?.ToString() ?? "repository",
                    ["filesExplored"] = explored.Count.ToString()
                }
            }
        ];
        return Task.FromResult(result);
    }

    /// <summary>The agent's own lightweight discipline relevance over the file map.</summary>
    private static bool IsRelevant(FindingCategory discipline, ScannedFile file)
    {
        var path = file.RelativePath.ToLowerInvariant();
        var name = System.IO.Path.GetFileName(path);

        return discipline switch
        {
            FindingCategory.Architecture =>
                path.Contains(".csproj") || path.Contains("/adr/") || name is "program.cs" or "startup.cs" or "apphost.cs",
            FindingCategory.CodeQuality =>
                file.Extension == ".cs" && file.LineCount > 100,
            FindingCategory.Reliability =>
                file.Extension == ".cs" && path.Contains("service"),
            FindingCategory.Security =>
                path.Contains("auth") || path.Contains("appsettings") || name.Contains("secret"),
            FindingCategory.Testing =>
                path.Contains("test") || name.Contains("tests.cs"),
            FindingCategory.Documentation =>
                file.Extension == ".md" || path.Contains("/docs/"),
            FindingCategory.Observability =>
                file.Extension == ".cs" && (path.Contains("health") || name.Contains("health")),
            _ => file.Extension == ".cs"
        };
    }

    private static string TypeFor(FindingCategory discipline) => discipline switch
    {
        FindingCategory.Architecture => ObservationTypes.LayerViolation,
        FindingCategory.CodeQuality => ObservationTypes.HighComplexity,
        FindingCategory.Reliability => ObservationTypes.MissingTimeout,
        FindingCategory.Security => ObservationTypes.HardcodedSecret,
        FindingCategory.Testing => ObservationTypes.MissingTests,
        FindingCategory.Documentation => ObservationTypes.MissingDocumentation,
        FindingCategory.Observability => ObservationTypes.MissingHealthCheck,
        _ => ObservationTypes.GeneralObservation
    };

    private static string BuildObservationsJson(FindingCategory? discipline, IReadOnlyList<ScannedFile> files)
    {
        var observations = files.Select((f, i) => new
        {
            type = discipline is { } d ? TypeFor(d) : ObservationTypes.GeneralObservation,
            discipline = discipline?.ToString() ?? "Unknown",
            title = $"Agentic: reviewed '{f.RelativePath}' for {discipline?.ToString() ?? "repository-wide"} concerns",
            description = "Autonomously explored by the agentic source; the council provided no controlled context.",
            severity = "Medium",
            confidence = "Medium",
            ruleId = $"AGENT-{(discipline?.ToString() ?? "REPO").ToUpperInvariant()}-{i + 1:000}",
            fileReferences = new object[] { new { path = f.RelativePath } },
            symbolReferences = Array.Empty<string>(),
            lineReferences = Array.Empty<int>(),
            evidenceExcerpt = "Agent exploration.",
            recommendationHint = $"Review the {discipline?.ToString() ?? "repository"} aspects of this area.",
            tags = new[] { "agentic", (discipline?.ToString() ?? "repository").ToLowerInvariant() }
        }).ToArray();

        return JsonSerializer.Serialize(
            new
            {
                schemaVersion = "1.0",
                discipline = discipline?.ToString() ?? "repository",
                observations
            },
            new JsonSerializerOptions { WriteIndented = true });
    }
}
