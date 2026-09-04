using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CleanService;

/// <summary>Reads catalog entries from the catalog service.</summary>
public sealed class CatalogClient
{
    private readonly HttpClient _http;

    /// <summary>Creates a client over an injected, pre-configured <see cref="HttpClient"/>.</summary>
    public CatalogClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <summary>Fetches one catalog entry. Cancellation is honored by the caller's token.</summary>
    public async Task<string> GetEntryAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        using var response = await _http.GetAsync($"catalog/{Uri.EscapeDataString(id)}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
