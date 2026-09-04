using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// Assembles the first-class <see cref="EngineeringReviewPackage"/> from a
/// completed <see cref="AnalysisRun"/> (its findings, council summary, provider
/// execution) plus computed metrics, health, risk and evidence summary.
///
/// It contains NO exporter/rendering logic — it only builds the domain object.
/// Exporters (Markdown, JSON, future HTML/PDF) project the package afterwards.
/// </summary>
public interface IEngineeringReviewPackageBuilder
{
    EngineeringReviewPackage Build(AnalysisRun run);
}
