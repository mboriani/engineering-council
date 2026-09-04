using System.Text.RegularExpressions;

namespace EngineeringCouncil.Core.Domain;

/// <summary>
/// Run identifier policy: the single source of truth for the generated run-id
/// format. It is used to generate new ids (pipeline) and to validate ids arriving
/// from untrusted input (repository reads, API). A valid run id is the canonical
/// generated shape, so it can never inject path separators, traversal components,
/// or rooted paths into the outputs filesystem boundary.
/// </summary>
public static class AnalysisRunId
{
    private static readonly Regex Pattern = new(
        @"^[0-9]{8}-[0-9]{6}-[0-9a-f]{6}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string New()
        => $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";

    public static bool IsValid(string? runId)
        => runId is not null && Pattern.IsMatch(runId);
}
