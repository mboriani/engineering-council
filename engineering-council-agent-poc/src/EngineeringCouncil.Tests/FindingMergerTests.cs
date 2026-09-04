using EngineeringCouncil.Core.Domain;
using EngineeringCouncil.Infrastructure.Merging;
using Xunit;

namespace EngineeringCouncil.Tests;

public sealed class FindingMergerTests
{
    private static Finding Raw(
        string id,
        string title,
        FindingCategory category,
        string agent,
        FindingSeverity severity = FindingSeverity.Medium,
        FindingConfidence confidence = FindingConfidence.Medium,
        string? recommendation = null,
        string[]? files = null,
        string evidence = "some evidence") => new()
    {
        Id = id,
        Title = title,
        Category = category,
        Severity = severity,
        Confidence = confidence,
        Evidence = evidence,
        Recommendation = recommendation ?? "Refactor the affected area.",
        SourceAgent = agent,
        SourceAgents = [agent],
        Status = FindingStatus.New,
        FileReferences = (files ?? []).Select(p => new FileReference { Path = p }).ToList()
    };

    private static readonly RuleBasedFindingMerger Merger = new();

    [Fact]
    public async Task Exact_duplicates_are_merged_into_one()
    {
        IReadOnlyList<Finding> raw =
        [
            Raw("RAW-001", "Large class Service should be split", FindingCategory.CodeQuality, "code-quality-analyzer"),
            Raw("RAW-002", "Large class Service should be split", FindingCategory.CodeQuality, "reviewer-two")
        ];

        var result = await Merger.MergeAsync(raw);

        var finding = Assert.Single(result);
        Assert.Equal(FindingStatus.Merged, finding.Status);
        Assert.Contains("RAW-001", finding.MergedFromFindingIds);
        Assert.Contains("RAW-002", finding.MergedFromFindingIds);
    }

    [Fact]
    public async Task Similar_titles_in_same_category_are_merged()
    {
        IReadOnlyList<Finding> raw =
        [
            Raw("RAW-001", "Service class is too large and should be split", FindingCategory.CodeQuality, "a"),
            Raw("RAW-002", "The Service class is large and should be split up", FindingCategory.CodeQuality, "b")
        ];

        var result = await Merger.MergeAsync(raw);

        var finding = Assert.Single(result);
        Assert.Equal(FindingStatus.Merged, finding.Status);
        Assert.Equal(2, finding.SourceAgents.Count);
    }

    [Fact]
    public async Task Severity_is_escalated_to_the_highest()
    {
        IReadOnlyList<Finding> raw =
        [
            Raw("RAW-001", "SQL injection risk in query builder", FindingCategory.Security, "a", severity: FindingSeverity.Medium),
            Raw("RAW-002", "SQL injection risk in query builder", FindingCategory.Security, "b", severity: FindingSeverity.Critical)
        ];

        var result = await Merger.MergeAsync(raw);

        var finding = Assert.Single(result);
        Assert.Equal(FindingSeverity.Critical, finding.Severity);
        Assert.Contains("Escalated", finding.SeverityRationale);
    }

    [Fact]
    public async Task Confidence_is_high_when_two_or_more_agents_agree()
    {
        IReadOnlyList<Finding> raw =
        [
            Raw("RAW-001", "Missing timeouts on HTTP calls", FindingCategory.Reliability, "a", confidence: FindingConfidence.Low),
            Raw("RAW-002", "Missing timeouts on HTTP calls", FindingCategory.Reliability, "b", confidence: FindingConfidence.Low)
        ];

        var result = await Merger.MergeAsync(raw);

        var finding = Assert.Single(result);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
    }

    [Fact]
    public async Task All_source_agents_and_evidence_are_preserved()
    {
        IReadOnlyList<Finding> raw =
        [
            Raw("RAW-001", "Config secret committed", FindingCategory.Security, "agent-x", evidence: "found KEY=123"),
            Raw("RAW-002", "Config secret committed", FindingCategory.Security, "agent-y", evidence: "appsettings has secret")
        ];

        var result = await Merger.MergeAsync(raw);

        var finding = Assert.Single(result);
        Assert.Contains("agent-x", finding.SourceAgents);
        Assert.Contains("agent-y", finding.SourceAgents);
        Assert.Contains("found KEY=123", finding.Evidence);
        Assert.Contains("appsettings has secret", finding.Evidence);
    }

    [Fact]
    public async Task Findings_in_unrelated_categories_are_not_merged()
    {
        IReadOnlyList<Finding> raw =
        [
            Raw("RAW-001", "Improve logging coverage", FindingCategory.Observability, "a"),
            Raw("RAW-002", "Improve test coverage", FindingCategory.Testing, "b")
        ];

        var result = await Merger.MergeAsync(raw);

        Assert.Equal(2, result.Count);
        Assert.All(result, f => Assert.Equal(FindingStatus.New, f.Status));
    }

    [Fact]
    public async Task Same_title_different_category_is_kept_separate_with_a_note()
    {
        IReadOnlyList<Finding> raw =
        [
            Raw("RAW-001", "Improve error handling", FindingCategory.Reliability, "a"),
            Raw("RAW-002", "Improve error handling", FindingCategory.CodeQuality, "b")
        ];

        var result = await Merger.MergeAsync(raw);

        Assert.Equal(2, result.Count);
        Assert.All(result, f => Assert.Contains(f.Tags, t => t.StartsWith("related:")));
    }

    [Fact]
    public async Task Merge_does_not_mutate_the_input_raw_findings()
    {
        var input = new List<Finding>
        {
            Raw("RAW-001", "Large class Service should be split", FindingCategory.CodeQuality, "a"),
            Raw("RAW-002", "Large class Service should be split", FindingCategory.CodeQuality, "b")
        };

        _ = await Merger.MergeAsync(input);

        // Originals remain untouched (records are immutable; merger returns new instances).
        Assert.All(input, f => Assert.Equal(FindingStatus.New, f.Status));
        Assert.Equal("RAW-001", input[0].Id);
        Assert.Single(input[0].SourceAgents);
    }
}
