using System.Text.Json;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Core.Serialization;

namespace EngineeringCouncil.Infrastructure.Reporting;

/// <summary>
/// Machine-readable exporter. Produces the findings.json contract consumed by
/// the future Engineering Dashboard, the raw-findings.json audit trail, and the
/// run.json used for reload.
/// </summary>
public sealed class JsonReportGenerator : IJsonReportGenerator
{
    public string RenderPackage(EngineeringReviewPackage package)
        => JsonSerializer.Serialize(package, CouncilJson.Options);

    public string RenderFindings(AnalysisRun run)
        => JsonSerializer.Serialize(run.Findings, CouncilJson.Options);

    public string RenderObservations(AnalysisRun run)
        => JsonSerializer.Serialize(run.Observations, CouncilJson.Options);

    public string RenderRawFindings(AnalysisRun run)
        => JsonSerializer.Serialize(run.RawFindings, CouncilJson.Options);

    public string RenderProviderExecution(AnalysisRun run)
        => JsonSerializer.Serialize(
            run.ProviderExecution ?? new ProviderExecutionReport(), CouncilJson.Options);

    public string RenderProviderComparison(AnalysisRun run)
        => JsonSerializer.Serialize(run.ProviderComparison, CouncilJson.Options);

    public string RenderCalibrationDiagnostics(AnalysisRun run)
        => JsonSerializer.Serialize(run.CalibrationDiagnostics, CouncilJson.Options);

    public string RenderRun(AnalysisRun run)
        => JsonSerializer.Serialize(run, CouncilJson.Options);
}
