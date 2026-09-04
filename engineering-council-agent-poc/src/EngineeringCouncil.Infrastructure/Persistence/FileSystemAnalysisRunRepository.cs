using System.Text.Json;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Core.Serialization;
using EngineeringCouncil.Infrastructure.Reporting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringCouncil.Infrastructure.Persistence;

public sealed class FileSystemRunRepositoryOptions
{
    /// <summary>Root outputs directory. Each run gets an <c>{OutputsRoot}/{runId}/</c> folder.</summary>
    public string OutputsRoot { get; set; } = "outputs";
}

/// <summary>
/// Writes each run to <c>outputs/{runId}/</c> using the dedicated exporters (it
/// does not render anything itself). The Engineering Review Package is the
/// primary deliverable; the rest are supporting/tooling artifacts:
/// <list type="bullet">
/// <item><c>engineering-review-package.json</c> — the package (primary machine artifact)</item>
/// <item><c>engineering-review.md</c> — the package as a review document (primary human artifact)</item>
/// <item><c>findings.json</c> — consolidated findings (dashboard contract)</item>
/// <item><c>raw-findings.json</c> — original per-analyzer findings</item>
/// <item><c>provider-execution.json</c> — multi-provider execution report</item>
/// <item><c>run.json</c> — the full run (reload)</item>
/// </list>
/// </summary>
public sealed class FileSystemAnalysisRunRepository : IAnalysisRunRepository
{
    private readonly FileSystemRunRepositoryOptions _options;
    private readonly IEngineeringReviewMarkdownExporter _markdown;
    private readonly IJsonReportGenerator _json;
    private readonly ILogger<FileSystemAnalysisRunRepository> _logger;

    public FileSystemAnalysisRunRepository(
        FileSystemRunRepositoryOptions options,
        IEngineeringReviewMarkdownExporter markdown,
        IJsonReportGenerator json,
        ILogger<FileSystemAnalysisRunRepository>? logger = null)
    {
        _options = options;
        _markdown = markdown;
        _json = json;
        _logger = logger ?? NullLogger<FileSystemAnalysisRunRepository>.Instance;
    }

    public async Task<string> SaveAsync(
        AnalysisRun run,
        EngineeringReviewPackage package,
        CancellationToken cancellationToken = default)
    {
        var runDir = ResolveRunDirectory(run.RunId);
        Directory.CreateDirectory(runDir);

        var reviewMd = _markdown.Export(package);
        var packageJson = _json.RenderPackage(package);
        var observationsJson = _json.RenderObservations(run);
        var findingsJson = _json.RenderFindings(run);
        var rawFindingsJson = _json.RenderRawFindings(run);
        var providerExecutionJson = _json.RenderProviderExecution(run);
        var runJson = _json.RenderRun(run);

        await File.WriteAllTextAsync(Path.Combine(runDir, "engineering-review-package.json"), packageJson, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runDir, "engineering-review.md"), reviewMd, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runDir, "observations.json"), observationsJson, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runDir, "findings.json"), findingsJson, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runDir, "raw-findings.json"), rawFindingsJson, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(runDir, "provider-execution.json"), providerExecutionJson, cancellationToken).ConfigureAwait(false);

        // Internal provider comparison (M12.2) — written only when ≥2 comparable LLM
        // executions exist; never required by the external Engineering Review app.
        if (run.ProviderComparison is { } comparison)
        {
            var comparisonJson = _json.RenderProviderComparison(run);
            var comparisonMd = ProviderComparisonMarkdownExporter.Export(comparison);
            await File.WriteAllTextAsync(Path.Combine(runDir, "provider-comparison.json"), comparisonJson, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(runDir, "provider-comparison.md"), comparisonMd, cancellationToken).ConfigureAwait(false);
        }

        // Internal calibration diagnostics (M12.3) — written only when a provider
        // comparison exists; never required by the external Engineering Review app.
        if (run.CalibrationDiagnostics is not null)
        {
            var calibrationJson = _json.RenderCalibrationDiagnostics(run);
            await File.WriteAllTextAsync(Path.Combine(runDir, "calibration-diagnostics.json"), calibrationJson, cancellationToken).ConfigureAwait(false);
        }

        await File.WriteAllTextAsync(Path.Combine(runDir, "run.json"), runJson, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Wrote Engineering Review Package + artifacts to {RunDir}", runDir);
        return runDir;
    }

    public async Task<AnalysisRun?> GetAsync(string runId, CancellationToken cancellationToken = default)
    {
        if (!AnalysisRunId.IsValid(runId))
            return null;

        string runDir;
        try
        {
            runDir = ResolveRunDirectory(runId);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var path = Path.Combine(runDir, "run.json");
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AnalysisRun>(stream, CouncilJson.Options, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the output directory for a run id and verifies it stays below the
    /// outputs root. Invalid ids and ids that resolve outside the root are rejected.
    /// </summary>
    private string ResolveRunDirectory(string runId)
    {
        if (!AnalysisRunId.IsValid(runId))
            throw new ArgumentException($"Invalid run id '{runId}'.", nameof(runId));

        var root = Path.GetFullPath(_options.OutputsRoot);
        var runDir = Path.GetFullPath(Path.Combine(root, runId));
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!runDir.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Run id '{runId}' resolves outside the outputs root.", nameof(runId));

        return runDir;
    }

    public Task<IReadOnlyList<string>> ListRunIdsAsync(CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(_options.OutputsRoot);
        if (!Directory.Exists(root))
            return Task.FromResult<IReadOnlyList<string>>([]);

        IReadOnlyList<string> ids = Directory.GetDirectories(root)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .OrderByDescending(name => name, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(ids);
    }
}
