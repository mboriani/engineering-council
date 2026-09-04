using Xunit;

namespace EngineeringCouncil.Tests;

/// <summary>
/// The process-runner tests (OpenCode M13.2, Codex M13.4) share one NON-PARALLEL
/// collection. Both launch short-lived <c>cmd /c ping</c> process trees and assert no
/// orphan survives; running them concurrently would make one test's survivor check
/// observe the other test's still-running child.
/// </summary>
[CollectionDefinition(ProcessRunnerTestsShared.CollectionName, DisableParallelization = true)]
public sealed class ProcessRunnerTestsShared
{
    public const string CollectionName = "ProcessRunnerTests";
}
