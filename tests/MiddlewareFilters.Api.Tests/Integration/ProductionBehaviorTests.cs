using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MiddlewareFilters.Api.Authentication;
using MiddlewareFilters.Api.Models;

namespace MiddlewareFilters.Api.Tests.Integration;

public sealed class ProductionBehaviorTests : IClassFixture<ProductionApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public ProductionBehaviorTests(ProductionApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Diagnostic_endpoint_does_not_throw_in_production()
    {
        using var response = await _client.GetAsync("/api/products/diagnostics/exception");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("Diagnostic exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Demo_user_header_does_not_authenticate_in_production()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/products/secure-ping");
        request.Headers.TryAddWithoutValidation(DemoAuthenticationHandler.HeaderName, "portfolio.reader");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Product_list_remains_available()
    {
        using var response = await _client.GetAsync("/api/products");
        var products = await response.Content.ReadFromJsonAsync<List<Product>>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(products);
        Assert.Contains(products, product => product.Name == "Notebook");
    }
}
