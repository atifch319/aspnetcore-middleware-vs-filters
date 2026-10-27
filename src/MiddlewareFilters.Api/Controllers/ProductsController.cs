using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MiddlewareFilters.Api.Filters;
using MiddlewareFilters.Api.Middleware;
using MiddlewareFilters.Api.Models;
using MiddlewareFilters.Api.Services;

namespace MiddlewareFilters.Api.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(ProductService products, IHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<Product>> GetAll(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Ok(products.GetAll());
    }

    [HttpGet("{id:int}")]
    public ActionResult<Product> GetById(int id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var product = products.GetById(id);
        if (product is null)
        {
            var error = new ApiErrorResponse
            {
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                Title = "Product not found.",
                Status = StatusCodes.Status404NotFound,
                Detail = $"No product exists with id {id}.",
                Instance = HttpContext.Request.Path.Value,
                CorrelationId = CorrelationIdMiddleware.GetCorrelationId(HttpContext)
            };

            return new NotFoundObjectResult(error)
            {
                ContentTypes = { "application/problem+json" }
            };
        }

        return Ok(product);
    }

    [HttpPost]
    [ServiceFilter(typeof(ValidateProductActionFilter))]
    public ActionResult<Product> Create(CreateProductRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var product = products.Add(request);
        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }

    /// <summary>
    /// Deliberately throws so the global exception middleware can be demonstrated.
    /// Outside Development and Testing the action returns 404 and does not throw.
    /// </summary>
    [HttpGet("diagnostics/exception")]
    public IActionResult ThrowForDiagnostics()
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            return NotFound();
        }

        throw new InvalidOperationException("Diagnostic exception for pipeline demonstration.");
    }

    [Authorize]
    [HttpGet("secure-ping")]
    public IActionResult SecurePing()
    {
        return Ok(new
        {
            message = "Authorized request reached the action.",
            user = User.Identity?.Name
        });
    }
}
