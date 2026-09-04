using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// Renders JSON artifacts. The <see cref="EngineeringReviewPackage"/> is the
/// primary product; the run-derived artifacts remain for tooling and reload.
/// </summary>
public interface IJsonReportGenerator
{
    /// <summary>The Engineering Review Package (engineering-review-package.json).</summary>
    string RenderPackage(EngineeringReviewPackage package);

    /// <summary>The consolidated findings array (findings.json) — the dashboard contract.</summary>
    string RenderFindings(AnalysisRun run);

    /// <summary>The normalized observations (observations.json).</summary>
    string RenderObservations(AnalysisRun run);

    /// <summary>The raw, pre-consolidation analyzer findings (raw-findings.json).</summary>
    string RenderRawFindings(AnalysisRun run);

    /// <summary>The multi-provider execution report (provider-execution.json).</summary>
    string RenderProviderExecution(AnalysisRun run);

    /// <summary>The internal provider comparison (provider-comparison.json; null-safe).</summary>
    string RenderProviderComparison(AnalysisRun run);

    /// <summary>The internal calibration diagnostics (calibration-diagnostics.json; null-safe).</summary>
    string RenderCalibrationDiagnostics(AnalysisRun run);

    /// <summary>The full run object (run.json) for reload.</summary>
    string RenderRun(AnalysisRun run);
}
