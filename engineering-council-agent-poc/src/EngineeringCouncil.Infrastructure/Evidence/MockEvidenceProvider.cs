using System.Diagnostics;
using System.Text.Json;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Infrastructure.Evidence;

/// <summary>
/// Offline LLM-style evidence provider used when no real backend is configured. It
/// is <b>Discipline</b>-scoped (it benefits from focused requests): for a discipline
/// step it returns one observation for that discipline; for a repository-wide step
/// it returns observations across all disciplines. Output is a structured
/// <c>observations</c> JSON envelope (never findings), referencing a real file from
/// the selected context so the "do not invent files" guard is satisfied downstream.
/// </summary>
public sealed class MockEvidenceProvider : IEvidenceProvider
{
    public bool IsAvailable => true;

    public EvidenceProviderMetadata Metadata => new()
    {
        Name = "Mock",
        ProviderType = EvidenceProviderType.LLM,
        DefaultAcquisitionScope = EvidenceAcquisitionScope.Discipline,
        RequiresAnalyzerInstructions = true,
        SupportsRepositoryWideAnalysis = false,
        Version = "mock-observer-v2"
    };

    private static readonly (FindingCategory Discipline, string Type, string Title)[] All =
    [
        (FindingCategory.Architecture, ObservationTypes.LayerViolation, "Potential layering/coupling smell"),
        (FindingCategory.CodeQuality, ObservationTypes.HighComplexity, "Method with elevated complexity"),
        (FindingCategory.Reliability, ObservationTypes.MissingTimeout, "I/O call without an explicit timeout"),
        (FindingCategory.Security, ObservationTypes.HardcodedSecret, "Possible sensitive value in source/config"),
        (FindingCategory.Testing, ObservationTypes.MissingTests, "Core logic with thin test coverage"),
        (FindingCategory.Documentation, ObservationTypes.MissingDocumentation, "Public surface lacking documentation"),
        (FindingCategory.Observability, ObservationTypes.MissingHealthCheck, "Service without a health/readiness signal"),
    ];

    public Task<IReadOnlyList<Core.Domain.Evidence>> CollectAsync(EvidenceRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var file = request.ContextSelection.Files.FirstOrDefault()?.RelativePath;

        var disciplines = request.Scope == EvidenceAcquisitionScope.Discipline && request.Discipline is { } d
            ? All.Where(x => x.Discipline == d)
            : All;

        var json = BuildObservationsJson(disciplines, file);
        stopwatch.Stop();

        IReadOnlyList<Core.Domain.Evidence> result =
        [
            new Core.Domain.Evidence
            {
                ProviderName = Metadata.Name,
                ProviderId = "mock",
                ProviderVersion = "mock-observer-v2",
                ProviderType = Metadata.ProviderType,
                RawResponse = json,
                Confidence = 0.3,
                ExecutionTime = stopwatch.Elapsed,
                Success = true,
                Metadata = new Dictionary<string, string> { ["model"] = "mock-observer-v2" }
            }
        ];
        return Task.FromResult(result);
    }

    private static string BuildObservationsJson(IEnumerable<(FindingCategory Discipline, string Type, string Title)> items, string? file)
    {
        var observations = items.Select(d => new
        {
            type = d.Type,
            discipline = d.Discipline.ToString(),
            title = $"[MOCK] {d.Title}",
            description = $"Placeholder {d.Discipline} observation produced by the offline mock provider.",
            severity = "Low",
            confidence = "Low",
            ruleId = $"MOCK-{d.Discipline.ToString().ToUpperInvariant()}-001",
            fileReferences = file is null ? Array.Empty<object>() : new object[] { new { path = file } },
            symbolReferences = Array.Empty<string>(),
            lineReferences = Array.Empty<int>(),
            evidenceExcerpt = file is null ? "No source files in context." : $"Observed in '{file}'.",
            recommendationHint = $"Review the {d.Discipline} aspects of this area.",
            tags = new[] { "mock", d.Discipline.ToString().ToLowerInvariant() }
        }).ToArray();

        return JsonSerializer.Serialize(new { observations }, new JsonSerializerOptions { WriteIndented = true });
    }
}
