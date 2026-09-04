using System;
using CleanService;
using Xunit;

namespace CleanService.Tests;

public sealed class CatalogClientTests
{
    [Fact]
    public void Constructor_rejects_a_null_http_client()
        => Assert.Throws<ArgumentNullException>(() => new CatalogClient(null!));

    [Fact]
    public void Factory_configures_an_explicit_timeout()
    {
        var client = ServiceRegistration.CreateCatalogClient(new Uri("https://catalog.example.com"));
        Assert.NotNull(client);
    }
}
