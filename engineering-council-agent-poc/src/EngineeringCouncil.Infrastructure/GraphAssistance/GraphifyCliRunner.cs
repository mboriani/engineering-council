using System.Diagnostics;
using EngineeringCouncil.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringCouncil.Infrastructure.GraphAssistance;

/// <summary>
/// Executes the Graphify CLI to extract a repository graph.
/// Graphify must be installed and available on PATH or at the configured path.
/// Extraction is local, deterministic, and requires no LLM.
/// </summary>
public sealed class GraphifyCliRunner
{
    private readonly GraphAssistanceOptions _options;
    private readonly ILogger _logger;

    public GraphifyCliRunner(GraphAssistanceOptions options, ILogger? logger = null)
    {
        _options = options;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Runs Graphify against a repository root and produces graph.json at the
    /// specified output path. Returns true on success.
    /// </summary>
    public async Task<bool> ExtractAsync(string repositoryRoot, string outputGraphPath, CancellationToken cancellationToken = default)
    {
        var outputDir = Path.GetDirectoryName(outputGraphPath);
        if (outputDir is not null)
            Directory.CreateDirectory(outputDir);

        var timeout = TimeSpan.FromSeconds(_options.ExtractionTimeoutSeconds);

        var psi = new ProcessStartInfo
        {
            FileName = _options.GraphifyExecutable,
            Arguments = $"\"{repositoryRoot}\" --output \"{outputGraphPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _logger.LogInformation("Running Graphify: {Executable} {Arguments}", psi.FileName, psi.Arguments);

        var sw = Stopwatch.StartNew();
        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                _logger.LogError("Failed to start Graphify process");
                return false;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError("Graphify extraction timed out after {Timeout}", timeout);
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return false;
            }

            sw.Stop();

            if (process.ExitCode != 0)
            {
                var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogError("Graphify failed with exit code {ExitCode}: {Error}", process.ExitCode, stderr);
                return false;
            }

            if (!File.Exists(outputGraphPath))
            {
                _logger.LogError("Graphify completed but graph.json not found at {Path}", outputGraphPath);
                return false;
            }

            _logger.LogInformation("Graphify extraction completed in {Duration}ms", sw.ElapsedMilliseconds);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Graphify execution failed");
            return false;
        }
    }
}
