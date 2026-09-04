using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>
/// Projects an <see cref="EngineeringReviewPackage"/> into the Markdown review
/// document — one representation of the package, not the package itself.
/// </summary>
public interface IEngineeringReviewMarkdownExporter
{
    string Export(EngineeringReviewPackage package);
}
