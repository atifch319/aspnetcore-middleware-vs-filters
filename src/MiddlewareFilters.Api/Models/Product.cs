namespace MiddlewareFilters.Api.Models;

public sealed record Product
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required decimal Price { get; init; }
}
