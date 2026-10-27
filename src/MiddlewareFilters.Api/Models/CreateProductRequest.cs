namespace MiddlewareFilters.Api.Models;

public sealed record CreateProductRequest
{
    public string? Name { get; init; }

    public decimal Price { get; init; }
}
