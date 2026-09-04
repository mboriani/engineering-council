using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Application;

/// <summary>
/// Applies OPTIONAL targeted semantic review (Milestone 014.4) to consolidated
/// findings. Candidate selection is pure, deterministic, and independent of the
/// reviewer; ONLY candidates ever invoke <see cref="ISemanticReconciliationReviewer"/>
/// — everything else passes through completely unchanged. A reviewer that fails,
/// times out, or returns malformed output is treated as
/// <see cref="SemanticReconciliationDecision.Inconclusive"/>; it NEVER fails the run.
/// </summary>
public static class SemanticReconciliationBuilder
{
    /// <summary>
    /// A finding is a semantic-review candidate ONLY when its Council assessment is
    /// <c>AgreementWithDifferences</c> AND the recorded disagreement includes
    /// <c>ObservationType</c> (Milestone 014.3's Decision B). Everything else —
    /// <c>SingleSource</c>, <c>StrongAgreement</c>, <c>PotentialConflict</c>,
    /// severity-only differences, location-only differences, and findings without a
    /// Council assessment at all — bypasses semantic review entirely.
    /// </summary>
    public static bool IsCandidate(Finding finding)
        => finding.CouncilAssessment is { Type: ReconciliationAssessmentType.AgreementWithDifferences } assessment
           && assessment.Differences.Contains(ReconciliationAssessmentDisagreement.ObservationType);

    /// <summary>
    /// Reviews every candidate finding (sequentially — no parallel execution) and
    /// returns the findings with <see cref="Finding.SemanticReview"/> stamped on the
    /// candidates only. Non-candidate findings are returned by reference, unchanged,
    /// and NEVER invoke the reviewer.
    /// </summary>
    public static async Task<IReadOnlyList<Finding>> ApplyAsync(
        IReadOnlyList<Finding> findings,
        IReadOnlyList<EngineeringObservation> observations,
        ISemanticReconciliationReviewer reviewer,
        CancellationToken cancellationToken = default)
    {
        var observationsById = observations
            .GroupBy(o => o.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var results = new List<Finding>(findings.Count);
        foreach (var finding in findings)
        {
            if (!IsCandidate(finding))
            {
                results.Add(finding);
                continue;
            }

            var result = await ReviewOneAsync(finding, observationsById, reviewer, cancellationToken).ConfigureAwait(false);
            results.Add(finding with { SemanticReview = result });
        }

        return results;
    }

    private static async Task<SemanticReconciliationResult> ReviewOneAsync(
        Finding finding,
        IReadOnlyDictionary<string, EngineeringObservation> observationsById,
        ISemanticReconciliationReviewer reviewer,
        CancellationToken cancellationToken)
    {
        var request = BuildRequest(finding, observationsById);

        try
        {
            return await reviewer.ReviewAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // the RUN was cancelled — propagate, never silently swallow
        }
        catch (Exception)
        {
            // Reviewer failed / timed out / returned malformed output / is unavailable:
            // semantic review is optional enrichment and must NEVER fail the run.
            return new SemanticReconciliationResult
            {
                Decision = SemanticReconciliationDecision.Inconclusive,
                Reason = "Semantic reviewer unavailable."
            };
        }
    }

    /// <summary>Builds the MINIMAL, finding-scoped reviewer input — never the repository, other findings, or the package.</summary>
    private static SemanticReconciliationRequest BuildRequest(
        Finding finding, IReadOnlyDictionary<string, EngineeringObservation> observationsById)
    {
        var observations = finding.ObservationIds
            .Select(id => observationsById.GetValueOrDefault(id))
            .Where(o => o is not null)
            .Select(o => new SemanticReconciliationObservationInput
            {
                Provider = o!.SourceProvider,
                ObservationType = o.ObservationType,
                FileReferences = o.FileReferences.Select(r => r.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                EvidenceExcerpt = o.EvidenceExcerpt
            })
            .ToList();

        return new SemanticReconciliationRequest
        {
            FindingId = finding.Id,
            Title = finding.Title,
            Category = finding.Category,
            SupportingProviders = finding.SupportingProviders,
            Observations = observations
        };
    }
}
