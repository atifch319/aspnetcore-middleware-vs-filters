using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using MiddlewareFilters.Api.Filters;
using MiddlewareFilters.Api.Middleware;
using MiddlewareFilters.Api.Models;

namespace MiddlewareFilters.Api.Tests.Filters;

public sealed class ValidateProductActionFilterTests
{
    private readonly ValidateProductActionFilter _filter = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rejects_empty_product_name(string? name)
    {
        var context = CreateContext(new CreateProductRequest
        {
            Name = name,
            Price = 10m
        });
        var actionWasCalled = false;

        await _filter.OnActionExecutionAsync(context, () =>
        {
            actionWasCalled = true;
            return Task.FromResult(CreateExecutedContext(context));
        });

        Assert.False(actionWasCalled);

        var result = Assert.IsType<BadRequestObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Contains("application/problem+json", result.ContentTypes);

        var body = Assert.IsType<ApiErrorResponse>(result.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, body.Status);
        Assert.Equal("test-correlation", body.CorrelationId);
        Assert.True(body.Errors!.ContainsKey("name"));
        Assert.False(body.Errors.ContainsKey("price"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Rejects_non_positive_price(int price)
    {
        var context = CreateContext(new CreateProductRequest
        {
            Name = "Notebook",
            Price = price
        });
        var actionWasCalled = false;

        await _filter.OnActionExecutionAsync(context, () =>
        {
            actionWasCalled = true;
            return Task.FromResult(CreateExecutedContext(context));
        });

        Assert.False(actionWasCalled);

        var result = Assert.IsType<BadRequestObjectResult>(context.Result);
        var body = Assert.IsType<ApiErrorResponse>(result.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, body.Status);
        Assert.True(body.Errors!.ContainsKey("price"));
        Assert.False(body.Errors.ContainsKey("name"));
    }

    [Fact]
    public async Task Valid_input_reaches_the_action()
    {
        var context = CreateContext(new CreateProductRequest
        {
            Name = "Desk lamp",
            Price = 19.99m
        });
        var actionWasCalled = false;

        await _filter.OnActionExecutionAsync(context, () =>
        {
            actionWasCalled = true;
            return Task.FromResult(CreateExecutedContext(context));
        });

        Assert.True(actionWasCalled);
        Assert.Null(context.Result);
    }

    private static ActionExecutingContext CreateContext(CreateProductRequest request)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/products";
        httpContext.Items[CorrelationIdMiddleware.ItemKey] = "test-correlation";

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>
            {
                ["request"] = request
            },
            controller: new object());
    }

    private static ActionExecutedContext CreateExecutedContext(ActionExecutingContext context)
    {
        return new ActionExecutedContext(context, new List<IFilterMetadata>(), context.Controller);
    }
}
