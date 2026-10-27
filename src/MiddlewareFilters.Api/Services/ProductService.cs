using MiddlewareFilters.Api.Models;

namespace MiddlewareFilters.Api.Services;

public sealed class ProductService
{
    private readonly Lock _gate = new();
    private readonly List<Product> _products = [];
    private int _nextId = 1;

    public ProductService()
    {
        Add(new CreateProductRequest { Name = "Notebook", Price = 12.50m });
        Add(new CreateProductRequest { Name = "Mechanical pencil", Price = 3.25m });
    }

    public IReadOnlyList<Product> GetAll()
    {
        lock (_gate)
        {
            return [.. _products];
        }
    }

    public Product? GetById(int id)
    {
        lock (_gate)
        {
            return _products.FirstOrDefault(product => product.Id == id);
        }
    }

    public Product Add(CreateProductRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name) || request.Price <= 0)
        {
            throw new ArgumentException("A product needs a name and a positive price.", nameof(request));
        }

        lock (_gate)
        {
            var product = new Product
            {
                Id = _nextId++,
                Name = request.Name.Trim(),
                Price = request.Price
            };

            _products.Add(product);
            return product;
        }
    }
}
