using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Application;

/// <summary>
/// Builds a provider-neutral <see cref="ProviderComparisonReport"/> from data the
/// review pipeline ALREADY produced for one run — it never calls providers, never
/// rescans, never rebuilds context, and never re-interprets raw responses. It is a
/// deterministic projection of <see cref="AnalysisRun"/> state (M12.2).
///
/// Comparability rule: two LLM executions are compared only when they targeted the
/// same discipline and received the same effective context (same
/// <see cref="ProviderExecutionRecord.ContextFingerprint"/>). A comparison is
/// generated only for disciplines with ≥2 distinct LLM provider executions.
/// </summary>
public static class ProviderComparisonBuilder
{
    public static ProviderComparisonReport? Build(AnalysisRun run)
    {
        var records = run.ProviderExecution?.Records
            .Where(r => r.ProviderType == EvidenceProviderType.LLM)
            .ToList() ?? [];

        var disciplineComparisons = new List<DisciplineComparison>();
        foreach (var group in records
            .Where(r => r.RequestedDiscipline is not null)
            .GroupBy(r => r.RequestedDiscipline!.Value)
            .OrderBy(g => g.Key))
        {
            var providers = DistinctSorted(group.Select(r => r.ProviderName));
            if (providers.Count < 2)
                continue;
            disciplineComparisons.Add(BuildDiscipline(run, group.Key, providers, group.ToList()));
        }

        if (disciplineComparisons.Count == 0)
            return null;

        var comparedProviders = DistinctSorted(
            disciplineComparisons.SelectMany(d => d.Providers));
        var comparedRecords = records.Where(r =>
            comparedProviders.Contains(r.ProviderName, StringComparer.OrdinalIgnoreCase)).ToList();

        return new ProviderComparisonReport
        {
            RunId = run.RunId,
            Repository = run.SolutionName,
            RepositoryBranch = run.Branch,
            RepositoryCommit = run.Commit,
            ComparedProviders = comparedProviders,
            DisciplineComparisons = disciplineComparisons,
            OverallExecutionMetrics = ProviderExecutionReport.FromRecords(comparedRecords).ByProvider,
            Limitations = BuildLimitations(disciplineComparisons, comparedProviders),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static DisciplineComparison BuildDiscipline(
        AnalysisRun run, FindingCategory discipline, IReadOnlyList<string> providers,
        IReadOnlyList<ProviderExecutionRecord> records)
    {
        var fingerprints = records.Select(r => r.ContextFingerprint)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var contextComparable = fingerprints.Count == 1;
        var fingerprint = fingerprints.Count == 1 ? fingerprints[0] : string.Empty;
        var allSucceeded = records.All(r => r.Success);

        var status = !allSucceeded
            ? ProviderComparisonStatus.Incomplete
            : contextComparable
                ? ProviderComparisonStatus.Comparable
                : ProviderComparisonStatus.NonComparable;

        var executionMetrics = providers
            .Select(p => BuildExecution(provider: p, records.Where(r => ProviderEquals(r.ProviderName, p)).ToList()))
            .OrderBy(e => e.Provider, StringComparer.Ordinal)
            .ToList();

        var observationMetrics = new List<ProviderObservationComparison>();
        var findingMetrics = new List<ProviderFindingComparison>();
        AgreementMetrics? agreement = null;

        // Output comparison is meaningful only when every compared execution succeeded.
        if (allSucceeded)
        {
            observationMetrics = providers
                .Select(p => BuildObservation(run, discipline, p))
                .OrderBy(o => o.Provider, StringComparer.Ordinal)
                .ToList();
            findingMetrics = providers
                .Select(p => BuildFinding(run, discipline, p, providers))
                .OrderBy(f => f.Provider, StringComparer.Ordinal)
                .ToList();
        }

        if (status == ProviderComparisonStatus.Comparable)
            agreement = BuildAgreement(run, discipline, providers);

        var first = records[0];
        var contextDescription = contextComparable
            ? $"{first.ContextFileCount} files · {first.ContextCharacterCount} chars · fingerprint {fingerprint}"
            : $"{first.ContextFileCount} files · {first.ContextCharacterCount} chars";

        return new DisciplineComparison
        {
            Discipline = discipline,
            Providers = providers,
            ComparableContext = contextComparable,
            ContextFingerprint = fingerprint,
            ComparableContextDescription = contextDescription,
            Status = status,
            ExecutionMetrics = executionMetrics,
            ObservationMetrics = observationMetrics,
            FindingMetrics = findingMetrics,
            Agreement = agreement
        };
    }

    private static ProviderExecutionComparison BuildExecution(string provider, IReadOnlyList<ProviderExecutionRecord> records)
    {
        var first = records[0];
        var repairedAny = records.Any(r => r.RepairAttemptCount > 0);
        return new ProviderExecutionComparison
        {
            Provider = provider,
            Model = records.Select(r => r.ProviderVersion).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? string.Empty,
            Success = records.All(r => r.Success),
            Executions = records.Count,
            Failures = records.Count(r => !r.Success),
            Duration = records.Aggregate(TimeSpan.Zero, (t, r) => t + r.Duration),
            InputTokens = SumKnown(records.Select(r => r.InputTokens)),
            OutputTokens = SumKnown(records.Select(r => r.OutputTokens)),
            TotalTokens = SumKnown(records.Select(r => r.TokensUsed)),
            RetryCount = records.Sum(r => r.RetryCount),
            RepairAttemptCount = records.Sum(r => r.RepairAttemptCount),
            RepairSucceeded = repairedAny ? records.Any(r => r.RepairSucceeded == true) : null,
            ErrorCategory = records.Select(r => r.ErrorCategory).FirstOrDefault(e => e is not null),
            ResponseTruncated = records.Any(r => r.ResponseTruncated),
            ContextFileCount = first.ContextFileCount,
            ContextCharacterCount = first.ContextCharacterCount,
            ObservationCount = records.Sum(r => r.ObservationsProduced)
        };
    }

    private static ProviderObservationComparison BuildObservation(AnalysisRun run, FindingCategory discipline, string provider)
    {
        var observations = run.Observations
            .Where(o => ProviderEquals(o.SourceProvider, provider)
                && o.RequestedDiscipline == discipline)
            .ToList();

        var referencedFiles = observations
            .SelectMany(o => o.FileReferences)
            .Select(f => f.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var contextFileCount = run.ProviderExecution?.Records
            .FirstOrDefault(r => ProviderEquals(r.ProviderName, provider)
                && r.RequestedDiscipline == discipline)?.ContextFileCount ?? 0;

        return new ProviderObservationComparison
        {
            Provider = provider,
            ObservationCount = observations.Count,
            Types = Distribution(observations.Select(o => o.ObservationType)),
            Severities = Distribution(observations.Select(o => o.Severity.ToString())),
            Confidences = Distribution(observations.Select(o => o.Confidence.ToString())),
            FilesReferenced = referencedFiles.Count,
            ObservationsWithLocation = observations.Count(o => o.FileReferences.Count > 0),
            ObservationsWithoutLocation = observations.Count(o => o.FileReferences.Count == 0),
            ReferencedContextFileRate = contextFileCount > 0 && observations.Count > 0
                ? (double)referencedFiles.Count / contextFileCount
                : null
        };
    }

    private static ProviderFindingComparison BuildFinding(
        AnalysisRun run, FindingCategory discipline, string provider, IReadOnlyList<string> comparedProviders)
    {
        var supporting = run.Findings
            .Where(f => f.Category == discipline && SupportingCompared(f, comparedProviders).Count > 0)
            .ToList();

        var exclusive = supporting
            .Where(f => SupportingCompared(f, comparedProviders).SequenceEqual([provider], StringComparer.OrdinalIgnoreCase))
            .ToList();

        return new ProviderFindingComparison
        {
            Provider = provider,
            RawFindingCount = run.RawFindings.Count(f => f.Category == discipline && ProviderSupports(f, provider)),
            ConsolidatedFindingCount = supporting.Count(f => SupportingCompared(f, comparedProviders).Contains(provider, StringComparer.OrdinalIgnoreCase)),
            ExclusiveFindingCount = exclusive.Count,
            ExclusiveFindingIds = exclusive.Select(f => f.Id).OrderBy(id => id, StringComparer.Ordinal).ToList(),
            MultiProviderFindingCount = supporting.Count(f =>
                SupportingCompared(f, comparedProviders).Count >= 2
                && SupportingCompared(f, comparedProviders).Contains(provider, StringComparer.OrdinalIgnoreCase))
        };
    }

    private static AgreementMetrics? BuildAgreement(AnalysisRun run, FindingCategory discipline, IReadOnlyList<string> comparedProviders)
    {
        var relevant = run.Findings
            .Where(f => f.Category == discipline && SupportingCompared(f, comparedProviders).Count > 0)
            .ToList();

        var shared = relevant.Where(f => SupportingCompared(f, comparedProviders).Count >= 2).ToList();

        return new AgreementMetrics
        {
            ConsolidatedFindingCount = relevant.Count,
            SharedFindingCount = shared.Count,
            ExclusiveFindingCountByProvider = comparedProviders
                .Select(p => new NamedCount
                {
                    Name = p,
                    Count = relevant.Count(f =>
                        SupportingCompared(f, comparedProviders).SequenceEqual([p], StringComparer.OrdinalIgnoreCase))
                })
                .OrderBy(n => n.Name, StringComparer.Ordinal)
                .ToList(),
            MultiProviderFindingCount = relevant.Count(f => f.AgreementCount >= 2),
            AgreementRate = relevant.Count == 0
                ? null
                : (double)shared.Count / relevant.Count
        };
    }

    private static IReadOnlyList<string> BuildLimitations(
        IReadOnlyList<DisciplineComparison> disciplines, IReadOnlyList<string> comparedProviders)
    {
        var notes = new List<string>
        {
            "Descriptive comparison only — no ranking, scoring, weighting, or calibration.",
            "Agreement is not correctness."
        };

        foreach (var d in disciplines.OrderBy(d => d.Discipline))
        {
            if (d.Status == ProviderComparisonStatus.Incomplete)
            {
                var failed = d.ExecutionMetrics.Where(e => !e.Success)
                    .Select(e => $"{e.Provider} ({e.ErrorCategory ?? "failed"})");
                notes.Add($"Discipline {d.Discipline}: output comparison unavailable because {string.Join(", ", failed)} failed.");
            }
            else if (d.Status == ProviderComparisonStatus.NonComparable)
            {
                notes.Add($"Discipline {d.Discipline}: providers received different effective contexts (fingerprints differ); agreement not computed.");
            }

            foreach (var e in d.ExecutionMetrics.Where(e => !e.InputTokens.HasValue && !e.OutputTokens.HasValue))
                notes.Add($"Discipline {d.Discipline}, provider {e.Provider}: token usage unknown (unknown ≠ 0) — no difference is inferred.");
        }

        return notes;
    }

    private static IReadOnlyList<string> SupportingCompared(Finding f, IReadOnlyList<string> comparedProviders)
        => f.SupportingProviders
            .Where(p => comparedProviders.Contains(p, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

    private static bool ProviderSupports(Finding f, string provider)
        => ProviderEquals(f.EvidenceProvider, provider)
            || f.SupportingProviders.Any(p => ProviderEquals(p, provider));

    private static bool ProviderEquals(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> DistinctSorted(IEnumerable<string> values)
        => values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v.ToLowerInvariant(), StringComparer.Ordinal)
            .ThenBy(v => v, StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<NamedCount> Distribution(IEnumerable<string> names)
        => names
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(g => new NamedCount { Name = g.First(), Count = g.Count() })
            .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n.Name, StringComparer.Ordinal)
            .ToList();

    private static int? SumKnown(IEnumerable<int?> values)
    {
        var known = values.Where(v => v.HasValue).ToList();
        return known.Count == 0 ? null : known.Sum(v => v!.Value);
    }
}
