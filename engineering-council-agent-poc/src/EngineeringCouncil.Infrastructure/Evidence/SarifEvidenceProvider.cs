using System.Text.Json;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Sarif;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EngineeringCouncil.Infrastructure.Evidence;

/// <summary>
/// Native SARIF evidence source (Milestone 009). It does NOT run scanners — it
/// imports existing SARIF 2.1.0 result files and produces <b>one
/// <see cref="Core.Domain.Evidence"/> per tool run</b>, preserving the tool name,
/// version, invocation metadata, artifact/result counts, and the original SARIF
/// payload. It is Repository-scoped, so it executes once per run regardless of the
/// selected disciplines. All SARIF <i>meaning</i> (result → observation mapping)
/// lives in the interpreter, not here — this provider only imports and shards.
/// </summary>
public sealed class SarifEvidenceProvider : IEvidenceProvider
{
    private readonly SarifOptions _options;
    private readonly ILogger<SarifEvidenceProvider> _logger;

    public SarifEvidenceProvider(SarifOptions options, ILogger<SarifEvidenceProvider>? logger = null)
    {
        _options = options;
        _logger = logger ?? NullLogger<SarifEvidenceProvider>.Instance;
    }

    public bool IsAvailable => _options.IsActive;

    public EvidenceProviderMetadata Metadata => new()
    {
        Name = "SARIF",
        ProviderType = EvidenceProviderType.StaticAnalyzer,
        DefaultAcquisitionScope = EvidenceAcquisitionScope.Repository,
        // SupportedDisciplines left empty ⇒ all disciplines (SARIF is cross-discipline).
        RequiresAnalyzerInstructions = false,
        SupportsRepositoryWideAnalysis = true,
        Version = "sarif-2.1.0"
    };

    public Task<IReadOnlyList<Core.Domain.Evidence>> CollectAsync(
        EvidenceRequest request, CancellationToken cancellationToken = default)
    {
        var evidence = new List<Core.Domain.Evidence>();

        foreach (var file in _options.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(file))
            {
                _logger.LogWarning("SARIF file not found, skipping: {File}", file);
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not read SARIF file, skipping: {File}", file);
                continue;
            }

            var log = SarifLog.TryParse(text);
            if (log is null || log.Runs.Count == 0)
            {
                _logger.LogWarning("SARIF file was malformed or contained no runs, skipping: {File}", file);
                continue;
            }

            var sourceName = Path.GetFileName(file);
            foreach (var run in log.Runs)
                evidence.Add(BuildEvidence(run, log.Version, sourceName));
        }

        return Task.FromResult<IReadOnlyList<Core.Domain.Evidence>>(evidence);
    }

    private static Core.Domain.Evidence BuildEvidence(SarifRun run, string? version, string sourceFile)
    {
        var driver = run.Driver;
        var tool = driver?.Name ?? "Unknown";
        var toolVersion = driver?.EffectiveVersion ?? string.Empty;
        var invocation = run.Invocations.FirstOrDefault();

        // Preserve the original payload for this run as a self-contained SARIF log.
        var singleRunLog = new SarifLog { Version = version ?? "2.1.0", Runs = [run] };
        var payload = JsonSerializer.Serialize(singleRunLog, SarifJson.Options);

        var metadata = new Dictionary<string, string>
        {
            ["format"] = "sarif",
            ["sarifVersion"] = version ?? "2.1.0",
            ["tool"] = tool,
            ["toolVersion"] = toolVersion,
            ["resultCount"] = run.Results.Count.ToString(),
            ["artifactCount"] = run.Artifacts.Count.ToString(),
            ["ruleCount"] = (driver?.Rules.Count ?? 0).ToString(),
            ["sourceFile"] = sourceFile
        };
        if (!string.IsNullOrWhiteSpace(driver?.InformationUri))
            metadata["informationUri"] = driver!.InformationUri!;
        if (invocation?.ExecutionSuccessful is { } ok)
            metadata["invocationSuccessful"] = ok ? "true" : "false";
        if (!string.IsNullOrWhiteSpace(invocation?.CommandLine))
            metadata["commandLine"] = invocation!.CommandLine!;

        return new Core.Domain.Evidence
        {
            ProviderName = "SARIF",
            ProviderId = "sarif",
            ProviderVersion = toolVersion,
            ProviderType = EvidenceProviderType.StaticAnalyzer,
            RawResponse = payload,
            Confidence = 0.95,
            Success = true,
            Metadata = metadata
        };
    }
}
