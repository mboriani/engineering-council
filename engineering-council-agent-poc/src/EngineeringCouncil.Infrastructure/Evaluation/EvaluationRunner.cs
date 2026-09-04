using System.Text.Json;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringCouncil.Infrastructure.Evaluation;

/// <summary>Everything one evaluation case needs: a repository and a provider selection.</summary>
public sealed record EvaluationCase(EvaluationRepository Repository, IReadOnlyList<string> Providers);

/// <summary>
/// Runs the EXISTING pipeline over a dataset and measures it (Milestone 012). It adds
/// no analysis capability: it only executes cases, collects metrics, and reports. The
/// pipeline is supplied per case by a factory so each case gets its own provider
/// selection through the normal DI composition.
/// </summary>
public sealed class EvaluationRunner
{
    private readonly Func<EvaluationCase, AnalysisPipeline> _pipelineFactory;
    private readonly IReadOnlyDictionary<string, ModelPricing>? _pricing;
    private readonly ILogger _logger;

    public EvaluationRunner(
        Func<EvaluationCase, AnalysisPipeline> pipelineFactory,
        IReadOnlyDictionary<string, ModelPricing>? pricing = null,
        ILogger? logger = null)
    {
        _pipelineFactory = pipelineFactory;
        _pricing = pricing;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Reads the dataset manifest of every repository directory under <paramref name="datasetRoot"/>.</summary>
    public static IReadOnlyList<EvaluationRepository> LoadDataset(string datasetRoot)
    {
        if (!Directory.Exists(datasetRoot)) return [];

        var repositories = new List<EvaluationRepository>();
        foreach (var directory in Directory.GetDirectories(datasetRoot).OrderBy(d => d, StringComparer.Ordinal))
        {
            var manifestPath = Path.Combine(directory, "evaluation.json");
            var name = Path.GetFileName(directory);

            if (!File.Exists(manifestPath))
            {
                repositories.Add(new EvaluationRepository { Name = name, Path = directory });
                continue;
            }

            var manifest = JsonSerializer.Deserialize<EvaluationManifest>(File.ReadAllText(manifestPath), CouncilJson.Options);
            repositories.Add(new EvaluationRepository
            {
                Name = manifest?.Name ?? name,
                Path = directory,
                Purpose = manifest?.Purpose ?? string.Empty,
                ExpectedSignals = manifest?.ExpectedSignals ?? [],
                Sarif = manifest?.Sarif is { Length: > 0 } sarif ? Path.Combine(directory, sarif) : null
            });
        }

        return repositories;
    }

    /// <summary>
    /// Executes every repository × provider-selection case. Identical inputs per
    /// repository make the provider results directly comparable. A failing case is
    /// recorded, never fatal — the evaluation must always produce a report.
    /// </summary>
    public async Task<EvaluationReport> RunAsync(
        IReadOnlyList<EvaluationRepository> repositories,
        IReadOnlyList<IReadOnlyList<string>> providerSelections,
        CancellationToken cancellationToken = default)
    {
        var runs = new List<EvaluationRun>();
        var unavailable = new List<string>();

        foreach (var repository in repositories)
        {
            foreach (var providers in providerSelections)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var evaluationCase = new EvaluationCase(repository, providers);
                _logger.LogInformation("Evaluating {Repository} with [{Providers}]",
                    repository.Name, string.Join(", ", providers));

                try
                {
                    var pipeline = _pipelineFactory(evaluationCase);
                    var result = await pipeline.RunAsync(new AnalysisRequest
                    {
                        TargetPath = repository.Path,
                        ProviderName = string.Join(", ", providers)
                    }, cancellationToken).ConfigureAwait(false);

                    var metrics = EvaluationMetricsCollector.Collect(repository, providers, result, _pricing);
                    runs.Add(metrics);

                    foreach (var reason in metrics.Usage.FailureReasons)
                        if (!unavailable.Contains(reason, StringComparer.Ordinal)) unavailable.Add(reason);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Evaluation case {Repository}/[{Providers}] failed",
                        repository.Name, string.Join(", ", providers));
                    runs.Add(new EvaluationRun
                    {
                        Repository = repository.Name,
                        RepositoryPurpose = repository.Purpose,
                        Providers = providers,
                        Disciplines = [],
                        Status = "EvaluationError",
                        Error = ex.Message
                    });
                }
            }
        }

        return new EvaluationReport
        {
            Runs = runs,
            RepositoriesEvaluated = repositories.Select(r => r.Name).ToList(),
            ProvidersEvaluated = providerSelections.SelectMany(p => p)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.Ordinal).ToList(),
            ProvidersUnavailable = unavailable
        };
    }

    /// <summary>Wire shape of a repository's <c>evaluation.json</c> manifest.</summary>
    private sealed record EvaluationManifest
    {
        public string? Name { get; init; }
        public string? Purpose { get; init; }
        public List<string>? ExpectedSignals { get; init; }
        public string? Sarif { get; init; }
    }
}
