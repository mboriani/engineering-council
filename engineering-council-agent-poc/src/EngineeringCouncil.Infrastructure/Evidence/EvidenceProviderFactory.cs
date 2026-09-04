using EngineeringCouncil.Core.Abstractions;

namespace EngineeringCouncil.Infrastructure.Evidence;

/// <summary>
/// Resolves evidence providers by name from the set registered in DI. Because it
/// is built from <c>IEnumerable&lt;IEvidenceProvider&gt;</c>, adding a provider
/// requires no change here and no change to any analyzer.
/// </summary>
public sealed class EvidenceProviderFactory : IEvidenceProviderFactory
{
    private readonly IReadOnlyDictionary<string, IEvidenceProvider> _providers;

    public EvidenceProviderFactory(IEnumerable<IEvidenceProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.Metadata.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> ProviderNames => (IReadOnlyCollection<string>)_providers.Keys;

    public IEvidenceProvider GetProvider(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Provider name is required.", nameof(name));

        if (_providers.TryGetValue(name, out var provider))
            return provider;

        throw new InvalidOperationException(
            $"Unknown evidence provider '{name}'. Registered providers: {string.Join(", ", _providers.Keys)}.");
    }

    public bool TryGetProvider(string name, out IEvidenceProvider provider)
    {
        if (!string.IsNullOrWhiteSpace(name) && _providers.TryGetValue(name, out var found))
        {
            provider = found;
            return true;
        }

        provider = null!;
        return false;
    }
}
