using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MiddlewareFilters.Api.Middleware;
using MiddlewareFilters.Api.Models;

namespace MiddlewareFilters.Api.Filters;

/// <summary>
/// Validates a bound <see cref="CreateProductRequest"/> before the action runs.
/// Model binding has already produced the object, so the filter can reject it without reading the body again.
/// </summary>
public sealed class ValidateProductActionFilter : IAsyncActionFilter
{
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.ActionArguments.Values.OfType<CreateProductRequest>().FirstOrDefault();
        if (request is null)
        {
            context.Result = CreateValidationResult(
                context.HttpContext,
                new Dictionary<string, string[]>
                {
                    ["request"] = ["A product request is required."]
                });
            return Task.CompletedTask;
        }

        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["Product name is required."];
        }

        if (request.Price <= 0)
        {
            errors["price"] = ["Price must be greater than zero."];
        }

        if (errors.Count > 0)
        {
            context.Result = CreateValidationResult(context.HttpContext, errors);
            return Task.CompletedTask;
        }

        return next();
    }

    private static BadRequestObjectResult CreateValidationResult(
        HttpContext httpContext,
        IReadOnlyDictionary<string, string[]> errors)
    {
        var response = new ApiErrorResponse
        {
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            Title = "One or more validation errors occurred.",
            Status = StatusCodes.Status400BadRequest,
            Detail = "The product request is invalid.",
            Instance = httpContext.Request.Path.Value,
            CorrelationId = CorrelationIdMiddleware.GetCorrelationId(httpContext),
            Errors = errors
        };

        return new BadRequestObjectResult(response)
        {
            ContentTypes = { "application/problem+json" }
        };
    }
}
