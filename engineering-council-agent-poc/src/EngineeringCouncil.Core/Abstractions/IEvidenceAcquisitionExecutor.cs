using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Abstractions;

/// <summary>Result of executing an acquisition plan: evidence + owned telemetry + the plan.</summary>
public sealed record EvidenceAcquisitionResult
{
    public required IReadOnlyList<Evidence> Evidence { get; init; }
    public required IReadOnlyList<ProviderExecutionRecord> Executions { get; init; }
    public required EvidenceAcquisitionPlan Plan { get; init; }
}

/// <summary>
/// Executes an <see cref="EvidenceAcquisitionPlan"/>. Independent steps run
/// concurrently up to <c>EvidenceOptions.MaxConcurrency</c> (bounded; default 1 =
/// strictly sequential), each with its own provider, request, timeout and
/// cancellation. A failing step becomes a failed <see cref="Evidence"/> and the rest
/// still run. Execution OWNS its telemetry (returned in the result — no
/// process-global state, so concurrent runs never interleave). Results are
/// aggregated in plan order after all steps complete, so parallel execution never
/// makes the artifacts nondeterministic.
/// </summary>
public interface IEvidenceAcquisitionExecutor
{
    Task<EvidenceAcquisitionResult> ExecuteAsync(
        EvidenceAcquisitionPlan plan,
        RepositorySnapshot repository,
        CancellationToken cancellationToken = default);
}
