using System;
using System.Net.Http;

namespace CleanService;

/// <summary>Composition root helpers for <see cref="CatalogClient"/>.</summary>
public static class ServiceRegistration
{
    /// <summary>Builds a catalog client with an explicit request timeout.</summary>
    public static CatalogClient CreateCatalogClient(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);

        var http = new HttpClient
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(10)   // explicit timeout
        };

        return new CatalogClient(http);
    }
}
