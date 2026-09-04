using EngineeringCouncil.Core.Abstractions;
using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Tests.Fakes;

/// <summary>
/// A scripted, deterministic test double for <see cref="ISemanticReconciliationReviewer"/>
/// (Milestone 014.4). Records every request it received (for "invoked exactly once" /
/// "never invoked" assertions) and replays a queued script of results/failures. The
/// whole M14.4 unit-test suite runs through this: no network, no credentials, no real
/// agent runtime.
/// </summary>
internal sealed class FakeSemanticReconciliationReviewer : ISemanticReconciliationReviewer
{
    private readonly Queue<Func<SemanticReconciliationResult>> _script = new();

    /// <summary>Every request the reviewer received.</summary>
    public List<SemanticReconciliationRequest> Requests { get; } = [];

    public int CallCount => Requests.Count;

    public FakeSemanticReconciliationReviewer Returns(SemanticReconciliationDecision decision, string reason = "")
    {
        _script.Enqueue(() => new SemanticReconciliationResult { Decision = decision, Reason = reason });
        return this;
    }

    public FakeSemanticReconciliationReviewer Fails(string message = "scripted reviewer failure")
    {
        _script.Enqueue(() => throw new InvalidOperationException(message));
        return this;
    }

    public FakeSemanticReconciliationReviewer TimesOut()
    {
        _script.Enqueue(() => throw new OperationCanceledException());
        return this;
    }

    public Task<SemanticReconciliationResult> ReviewAsync(
        SemanticReconciliationRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (_script.Count == 0)
            throw new InvalidOperationException("FakeSemanticReconciliationReviewer received an unscripted call.");

        return Task.FromResult(_script.Dequeue()());
    }
}
