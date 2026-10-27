using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MiddlewareFilters.Api.Authentication;
using MiddlewareFilters.Api.Middleware;
using MiddlewareFilters.Api.Models;

namespace MiddlewareFilters.Api.Tests.Integration;

public sealed class ProductsApiTests : IClassFixture<DevelopmentApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public ProductsApiTests(DevelopmentApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_all_returns_the_product_list_unchanged_by_logging()
    {
        using var response = await _client.GetAsync("/api/products");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var products = JsonSerializer.Deserialize<List<Product>>(body, JsonOptions);
        Assert.NotNull(products);
        Assert.Contains(products, product => product.Id == 1 && product.Name == "Notebook" && product.Price == 12.50m);
        Assert.Contains(products, product => product.Id == 2 && product.Name == "Mechanical pencil" && product.Price == 3.25m);
        Assert.DoesNotContain("ElapsedMilliseconds", body, StringComparison.Ordinal);
        Assert.DoesNotContain("responded", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_by_id_returns_the_product()
    {
        using var response = await _client.GetAsync("/api/products/1");
        var product = await response.Content.ReadFromJsonAsync<Product>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(product);
        Assert.Equal("Notebook", product.Name);
        Assert.Equal(12.50m, product.Price);
    }

    [Fact]
    public async Task Missing_product_returns_404()
    {
        using var response = await _client.GetAsync("/api/products/99999");
        var body = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(body);
        Assert.Equal(404, body.Status);
        Assert.Equal("Product not found.", body.Title);
        Assert.False(string.IsNullOrWhiteSpace(body.CorrelationId));
    }

    [Fact]
    public async Task Valid_product_is_created()
    {
        using var response = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Desk lamp",
            Price = 19.99m
        });
        var product = await response.Content.ReadFromJsonAsync<Product>(JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(product);
        Assert.Equal("Desk lamp", product.Name);
        Assert.Equal(19.99m, product.Price);
        Assert.True(product.Id > 0);
        Assert.Equal($"/api/products/{product.Id}", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Empty_name_is_rejected_before_the_product_is_stored()
    {
        using var response = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "",
            Price = 5m
        });
        var body = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(body);
        Assert.Equal(400, body.Status);
        Assert.True(body.Errors!.ContainsKey("name"));
        Assert.False(string.IsNullOrWhiteSpace(body.CorrelationId));
        Assert.Equal(body.CorrelationId, response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
    }

    [Fact]
    public async Task Non_positive_price_is_rejected()
    {
        using var response = await _client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Desk lamp",
            Price = 0m
        });
        var body = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(body);
        Assert.True(body.Errors!.ContainsKey("price"));
    }

    [Fact]
    public async Task Unhandled_exception_returns_safe_problem_details()
    {
        using var response = await _client.GetAsync("/api/products/diagnostics/exception");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("Diagnostic exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("at MiddlewareFilters", body, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(body);
        Assert.Equal(500, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("An unexpected error occurred.", document.RootElement.GetProperty("title").GetString());
        Assert.False(document.RootElement.TryGetProperty("stackTrace", out _));

        var correlationId = document.RootElement.GetProperty("correlationId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
        Assert.Equal(correlationId, response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
    }

    [Fact]
    public async Task Correlation_id_is_generated_when_missing()
    {
        using var response = await _client.GetAsync("/api/products");

        var correlationId = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.True(Guid.TryParse(correlationId, out _));
    }

    [Fact]
    public async Task Correlation_id_is_returned_when_supplied()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/products");
        request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, "portfolio-demo-001");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("portfolio-demo-001", response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
    }

    [Fact]
    public async Task Invalid_correlation_id_is_replaced()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/products");
        request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, "../not-safe");

        using var response = await _client.SendAsync(request);

        var correlationId = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.NotEqual("../not-safe", correlationId);
        Assert.True(Guid.TryParse(correlationId, out _));
    }

    [Fact]
    public async Task Secure_ping_requires_an_authenticated_user()
    {
        using var response = await _client.GetAsync("/api/products/secure-ping");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Secure_ping_accepts_the_development_demo_user()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/products/secure-ping");
        request.Headers.TryAddWithoutValidation(DemoAuthenticationHandler.HeaderName, "portfolio.reader");

        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("portfolio.reader", body, StringComparison.Ordinal);
        Assert.Contains("Authorized request reached the action.", body, StringComparison.Ordinal);
    }
}
