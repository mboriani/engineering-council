using System.Text.Json;
using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Application;
using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Core.Serialization;
using EngineeringCouncil.Infrastructure.Llm;
using EngineeringCouncil.Infrastructure.Reconciliation;
using EngineeringCouncil.Infrastructure.Reporting;
using EngineeringCouncil.Tests.Contracts;
using EngineeringCouncil.Tests.Fakes;
using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// Milestone 014.4 — targeted semantic reconciliation. Verifies that ONLY
/// consolidated findings whose deterministic Council assessment is
/// <c>AgreementWithDifferences</c> with an <c>ObservationType</c> disagreement ever
/// invoke a semantic reviewer (M14.3's Decision B); that all three outcomes
/// (SameIssue / DifferentIssues / Inconclusive) safely preserve the deterministic
/// finding; that reviewer failure can never fail the run; and that the result is
/// exposed additively in the package and Markdown. All tests are offline and
/// deterministic — no network, no credentials, no real agent runtime.
/// </summary>
public sealed class SemanticReconciliationTests
{
    // ── Fixture helpers ────────────────────────────────────────────────────────

    private static ReconciliationAssessment Assessment(
        ReconciliationAssessmentType type, IReadOnlyList<string> providers,
        params ReconciliationAssessmentDisagreement[] differences)
        => new() { Type = type, SupportingProviders = providers, AgreementCount = providers.Count, Differences = differences };

    private static Finding Consolidated(string id, ReconciliationAssessment assessment, IReadOnlyList<string>? observationIds = null)
        => new()
        {
            Id = id,
            Title = $"Finding {id}",
            Category = FindingCategory.Security,
            Severity = FindingSeverity.High,
            Confidence = FindingConfidence.High,
            SupportingProviders = assessment.SupportingProviders,
            AgreementCount = assessment.AgreementCount,
            IsConsolidated = assessment.AgreementCount > 1,
            ObservationIds = observationIds ?? [],
            CouncilAssessment = assessment
        };

    private static readonly string[] TwoProviders = ["Claude", "Codex"];

    // ── 1-5, 7. Candidate selection (pure, deterministic) ─────────────────────

    [Fact]
    public void ObservationType_disagreement_is_a_candidate()
    {
        var finding = Consolidated("A", Assessment(
            ReconciliationAssessmentType.AgreementWithDifferences, TwoProviders, ReconciliationAssessmentDisagreement.ObservationType));

        Assert.True(SemanticReconciliationBuilder.IsCandidate(finding));
    }

    [Fact]
    public void Severity_only_disagreement_is_not_a_candidate()
    {
        var finding = Consolidated("A", Assessment(
            ReconciliationAssessmentType.AgreementWithDifferences, TwoProviders, ReconciliationAssessmentDisagreement.Severity));

        Assert.False(SemanticReconciliationBuilder.IsCandidate(finding));
    }

    [Fact]
    public void Location_only_disagreement_is_not_a_candidate()
    {
        var finding = Consolidated("A", Assessment(
            ReconciliationAssessmentType.AgreementWithDifferences, TwoProviders, ReconciliationAssessmentDisagreement.Location));

        Assert.False(SemanticReconciliationBuilder.IsCandidate(finding));
    }

    [Fact]
    public void StrongAgreement_is_not_a_candidate()
    {
        var finding = Consolidated("A", Assessment(ReconciliationAssessmentType.StrongAgreement, TwoProviders));
        Assert.False(SemanticReconciliationBuilder.IsCandidate(finding));
    }

    [Fact]
    public void SingleSource_is_not_a_candidate()
    {
        var finding = Consolidated("A", Assessment(ReconciliationAssessmentType.SingleSource, ["Claude"]));
        Assert.False(SemanticReconciliationBuilder.IsCandidate(finding));
    }

    [Fact]
    public void PotentialConflict_and_unassessed_findings_are_not_candidates()
    {
        var conflict = Consolidated("A", Assessment(ReconciliationAssessmentType.PotentialConflict, TwoProviders,
            ReconciliationAssessmentDisagreement.ObservationType)); // even WITH the disagreement flag, PotentialConflict is excluded
        Assert.False(SemanticReconciliationBuilder.IsCandidate(conflict));

        var unassessed = Consolidated("B", Assessment(ReconciliationAssessmentType.AgreementWithDifferences, TwoProviders,
            ReconciliationAssessmentDisagreement.ObservationType)) with { CouncilAssessment = null };
        Assert.False(SemanticReconciliationBuilder.IsCandidate(unassessed));
    }

    // ── 6-7. Reviewer invocation gating ───────────────────────────────────────

    [Fact]
    public async Task Only_the_two_ObservationType_findings_invoke_the_reviewer_in_a_mixed_fixture()
    {
        // The exact scenario the milestone's own verification section describes.
        var findings = new[]
        {
            Consolidated("StrongAgreement", Assessment(ReconciliationAssessmentType.StrongAgreement, TwoProviders)),
            Consolidated("SingleSource", Assessment(ReconciliationAssessmentType.SingleSource, ["Claude"])),
            Consolidated("SeverityOnly", Assessment(ReconciliationAssessmentType.AgreementWithDifferences, TwoProviders, ReconciliationAssessmentDisagreement.Severity)),
            Consolidated("LocationOnly", Assessment(ReconciliationAssessmentType.AgreementWithDifferences, TwoProviders, ReconciliationAssessmentDisagreement.Location)),
            Consolidated("ObsType1", Assessment(ReconciliationAssessmentType.AgreementWithDifferences, TwoProviders, ReconciliationAssessmentDisagreement.ObservationType)),
            Consolidated("ObsType2", Assessment(ReconciliationAssessmentType.AgreementWithDifferences, TwoProviders, ReconciliationAssessmentDisagreement.ObservationType)),
        };
        var reviewer = new FakeSemanticReconciliationReviewer()
            .Returns(SemanticReconciliationDecision.SameIssue)
            .Returns(SemanticReconciliationDecision.SameIssue);

        var reviewed = await SemanticReconciliationBuilder.ApplyAsync(findings, [], reviewer);

        Assert.Equal(2, reviewer.CallCount);
        Assert.Equal(["ObsType1", "ObsType2"], reviewer.Requests.Select(r => r.FindingId));
        Assert.All(reviewed.Where(f => f.Id is not "ObsType1" and not "ObsType2"), f => Assert.Null(f.SemanticReview));
        Assert.All(reviewed.Where(f => f.Id is "ObsType1" or "ObsType2"), f => Assert.NotNull(f.SemanticReview));
    }

    [Fact]
    public async Task A_candidate_invokes_the_reviewer_exactly_once()
    {
        var finding = Consolidated("A", Assessment(
            ReconciliationAssessmentType.AgreementWithDifferences, TwoProviders, ReconciliationAssessmentDisagreement.ObservationType));
        var reviewer = new FakeSemanticReconciliationReviewer().Returns(SemanticReconciliationDecision.SameIssue);

        await SemanticReconciliationBuilder.ApplyAsync([finding], [], reviewer);

        Assert.Equal(1, reviewer.CallCount);
    }

    [Fact]
    public async Task Reviewer_input_is_minimal_and_finding_scoped()
    {
        var observations = new[]
        {
            new EngineeringObservation { Id = "OBS-1", SourceProvider = "Claude", ObservationType = "HardcodedSecret",
                FileReferences = [new FileReference { Path = "src/A.cs" }], EvidenceExcerpt = "const string k = \"x\";" },
            new EngineeringObservation { Id = "OBS-2", SourceProvider = "Codex", ObservationType = "MissingAuthorization",
                FileReferences = [new FileReference { Path = "src/A.cs" }], EvidenceExcerpt = "[AllowAnonymous]" },
        };
        var finding = Consolidated("A", Assessment(
            ReconciliationAssessmentType.AgreementWithDifferences, TwoProviders, ReconciliationAssessmentDisagreement.ObservationType),
            observationIds: ["OBS-1", "OBS-2"]);
        var reviewer = new FakeSemanticReconciliationReviewer().Returns(SemanticReconciliationDecision.SameIssue);

        await SemanticReconciliationBuilder.ApplyAsync([finding], observations, reviewer);

        var request = Assert.Single(reviewer.Requests);
        Assert.Equal("A", request.FindingId);
        Assert.Equal(FindingCategory.Security, request.Category);
        Assert.Equal(2, request.Observations.Count);
        Assert.Contains(request.Observations, o => o is { Provider: "Claude", ObservationType: "HardcodedSecret" });
        Assert.Contains(request.Observations, o => o is { Provider: "Codex", ObservationType: "MissingAuthorization" });
    }

    // ── 8-10. Decision outcomes ────────────────────────────────────────────────

    [Fact]
    public async Task SameIssue_preserves_the_consolidated_finding_and_attaches_provenance()
    {
        var finding = Consolidated("A", Assessment(
            ReconciliationAssessmentType.AgreementWithDifferences, TwoProviders, ReconciliationAssessmentDisagreement.ObservationType));
        var reviewer = new FakeSemanticReconciliationReviewer().Returns(SemanticReconciliationDecision.SameIssue, "Same SQL construction, different labels.");

        var reviewed = Assert.Single(await SemanticReconciliationBuilder.ApplyAsync([finding], [], reviewer));

        Assert.Equal(SemanticReconciliationDecision.SameIssue, reviewed.SemanticReview!.Decision);
        // Deterministic reconciliation output is untouched.
        Assert.Equal(finding.Severity, reviewed.Severity);
        Assert.Equal(finding.Confidence, reviewed.Confidence);
        Assert.Equal(finding.CouncilAssessment, reviewed.CouncilAssessment);
        Assert.Equal(finding.SupportingProviders, reviewed.SupportingProviders);
    }

    [Fact]
    public async Task Inconclusive_preserves_the_consolidated_finding_and_does_not_guess()
    {
        var finding = Consolidated("A", Assessment(
            ReconciliationAssessmentType.AgreementWithDifferences, TwoProviders, ReconciliationAssessmentDisagreement.ObservationType));
        var reviewer = new FakeSemanticReconciliationReviewer().Returns(SemanticReconciliationDecision.Inconclusive, "Insufficient evidence.");

        var reviewed = Assert.Single(await SemanticReconciliationBuilder.ApplyAsync([finding], [], reviewer));

        Assert.Equal(SemanticReconciliationDecision.Inconclusive, reviewed.SemanticReview!.Decision);
        Assert.Equal(finding.Severity, reviewed.Severity);
        Assert.Equal(finding.CouncilAssessment, reviewed.CouncilAssessment);
    }

    [Fact]
    public async Task DifferentIssues_is_recorded_as_advisory_provenance_and_never_splits_the_finding()
    {
        var finding = Consolidated("A", Assessment(
            ReconciliationAssessmentType.AgreementWithDifferences, TwoProviders, ReconciliationAssessmentDisagreement.ObservationType));
        var reviewer = new FakeSemanticReconciliationReviewer().Returns(SemanticReconciliationDecision.DifferentIssues, "Distinct concerns co-located.");

        var reviewed = await SemanticReconciliationBuilder.ApplyAsync([finding], [], reviewer);

        // Exactly one finding in, exactly one finding out — no split.
        var only = Assert.Single(reviewed);
        Assert.Equal("A", only.Id);
        Assert.Equal(SemanticReconciliationDecision.DifferentIssues, only.SemanticReview!.Decision);
        Assert.Equal(finding.SupportingFindingIds, only.SupportingFindingIds); // grouping unchanged
        Assert.Equal(finding.Severity, only.Severity);                        // reconciliation output unchanged
    }

    // ── 11-12. Failure semantics ───────────────────────────────────────────────

    [Fact]
    public async Task A_thrown_reviewer_failure_becomes_Inconclusive_and_never_fails_ApplyAsync()
    {
        var finding = Consolidated("A", Assessment(
            ReconciliationAssessmentType.AgreementWithDifferences, TwoProviders, ReconciliationAssessmentDisagreement.ObservationType));
        var reviewer = new FakeSemanticReconciliationReviewer().Fails("simulated reviewer crash");

        var reviewed = Assert.Single(await SemanticReconciliationBuilder.ApplyAsync([finding], [], reviewer));

        Assert.Equal(SemanticReconciliationDecision.Inconclusive, reviewed.SemanticReview!.Decision);
        Assert.NotEmpty(reviewed.SemanticReview.Reason);
    }

    [Fact]
    public async Task A_reviewer_timeout_unrelated_to_run_cancellation_becomes_Inconclusive()
    {
        var finding = Consolidated("A", Assessment(
            ReconciliationAssessmentType.AgreementWithDifferences, TwoProviders, ReconciliationAssessmentDisagreement.ObservationType));
        var reviewer = new FakeSemanticReconciliationReviewer().TimesOut();

        var reviewed = Assert.Single(await SemanticReconciliationBuilder.ApplyAsync([finding], [], reviewer, CancellationToken.None));

        Assert.Equal(SemanticReconciliationDecision.Inconclusive, reviewed.SemanticReview!.Decision);
    }

    [Fact]
    public async Task Run_cancellation_still_propagates_instead_of_being_swallowed()
    {
        var finding = Consolidated("A", Assessment(
            ReconciliationAssessmentType.AgreementWithDifferences, TwoProviders, ReconciliationAssessmentDisagreement.ObservationType));
        var reviewer = new FakeSemanticReconciliationReviewer().Returns(SemanticReconciliationDecision.SameIssue);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => SemanticReconciliationBuilder.ApplyAsync([finding], [], reviewer, cts.Token));
    }

    // ── 13-14. Package + Markdown exposure ─────────────────────────────────────

    [Fact]
    public void Package_serializes_semantic_review_additively_and_the_consumer_DTO_deserializes_it()
    {
        var reviewed = Consolidated("A", Assessment(
            ReconciliationAssessmentType.AgreementWithDifferences, TwoProviders, ReconciliationAssessmentDisagreement.ObservationType))
            with { SemanticReview = new SemanticReconciliationResult { Decision = SemanticReconciliationDecision.SameIssue, Reason = "Same construction." } };
        var untouched = Consolidated("B", Assessment(ReconciliationAssessmentType.StrongAgreement, TwoProviders));

        var package = new EngineeringReviewPackage
        {
            Repository = "R", AnalysisRunId = "run1", SchemaVersion = "1.1",
            Findings = [reviewed, untouched]
        };
        var json = JsonSerializer.Serialize(package, CouncilJson.Options);

        Assert.Equal("1.1", package.SchemaVersion); // additive — schema unchanged
        Assert.Contains("\"semanticReview\":", json);
        Assert.Contains("\"decision\": \"sameIssue\"", json);

        var dto = JsonSerializer.Deserialize<PackageContract>(json, CouncilJson.Options)!;
        var reviewedDto = Assert.Single(dto.Findings, f => f.Id == "A");
        Assert.Equal("sameIssue", reviewedDto.SemanticReview!.Decision);
        Assert.Equal("Same construction.", reviewedDto.SemanticReview.Reason);

        var untouchedDto = Assert.Single(dto.Findings, f => f.Id == "B");
        Assert.Null(untouchedDto.SemanticReview); // absent, not guessed
    }

    [Fact]
    public void Markdown_renders_the_semantic_review_decision_and_reason()
    {
        var reviewed = Consolidated("A", Assessment(
            ReconciliationAssessmentType.AgreementWithDifferences, TwoProviders, ReconciliationAssessmentDisagreement.ObservationType))
            with { SemanticReview = new SemanticReconciliationResult { Decision = SemanticReconciliationDecision.SameIssue, Reason = "Both describe the same unsanitized SQL construction." } };

        var package = new EngineeringReviewPackage { Repository = "R", AnalysisRunId = "run1", Findings = [reviewed] };
        var md = new EngineeringReviewMarkdownExporter().Export(package);

        Assert.Contains("Council Assessment: AgreementWithDifferences", md);
        Assert.Contains("Differences: ObservationType", md);
        Assert.Contains("Semantic Review: SameIssue", md);
        Assert.Contains("Reason: Both describe the same unsanitized SQL construction.", md);
    }

    // ── 15. Disabled by default ────────────────────────────────────────────────

    [Fact]
    public void Semantic_reconciliation_is_disabled_by_default()
    {
        Assert.False(new SemanticReconciliationOptions().Enabled);
        Assert.Equal("Council:SemanticReconciliation", SemanticReconciliationOptions.SectionName);
    }

    // ── LlmSemanticReconciliationReviewer (real implementation, offline via ScriptedLlmClient) ──

    [Fact]
    public async Task Real_reviewer_parses_a_valid_decision_from_the_shared_LLM_transport()
    {
        var client = new ScriptedLlmClient().Returns("""{"decision":"DifferentIssues","reason":"Different root causes."}""");
        var options = new SemanticReconciliationOptions { Enabled = true, Model = "test-model", TimeoutSeconds = 5 };
        var reviewer = new LlmSemanticReconciliationReviewer(client, options);

        var result = await reviewer.ReviewAsync(new SemanticReconciliationRequest
        {
            FindingId = "A", Title = "t", Category = FindingCategory.Security,
            SupportingProviders = TwoProviders, Observations = []
        });

        Assert.Equal(SemanticReconciliationDecision.DifferentIssues, result.Decision);
        Assert.Equal("Different root causes.", result.Reason);
        Assert.Equal("test-model", client.Requests[0].Model);
        // The prompt asks exactly one conceptual question and states the guardrails.
        Assert.Contains("SAME", client.Requests[0].SystemInstructions);
        Assert.Contains("Inconclusive", client.Requests[0].SystemInstructions);
        Assert.Contains("Do NOT", client.Requests[0].SystemInstructions);
    }

    [Fact]
    public async Task Real_reviewer_categorizes_malformed_output_as_SchemaValidation()
    {
        var client = new ScriptedLlmClient().Returns("this is not json");
        var reviewer = new LlmSemanticReconciliationReviewer(client, new SemanticReconciliationOptions { Enabled = true, Model = "m" });

        var error = await Assert.ThrowsAsync<LlmProviderException>(() => reviewer.ReviewAsync(
            new SemanticReconciliationRequest { FindingId = "A", Title = "t", Category = FindingCategory.Security, SupportingProviders = [], Observations = [] }));

        Assert.Equal(LlmErrorCategory.SchemaValidation, error.Category);
    }

    [Fact]
    public async Task Real_reviewer_categorizes_a_truncated_response_as_SchemaValidation()
    {
        var client = new ScriptedLlmClient().Returns("""{"decision":"SameIssue"}""", truncated: true);
        var reviewer = new LlmSemanticReconciliationReviewer(client, new SemanticReconciliationOptions { Enabled = true, Model = "m" });

        var error = await Assert.ThrowsAsync<LlmProviderException>(() => reviewer.ReviewAsync(
            new SemanticReconciliationRequest { FindingId = "A", Title = "t", Category = FindingCategory.Security, SupportingProviders = [], Observations = [] }));

        Assert.Equal(LlmErrorCategory.SchemaValidation, error.Category);
    }
}
