using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace MiddlewareFilters.Api.Middleware;

/// <summary>
/// Turns an unhandled exception into a ProblemDetails response.
/// The body is the same in every environment: exception messages and stack traces stay in the server log.
/// </summary>
public sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug(
                "Request processing was canceled. CorrelationId: {CorrelationId}",
                CorrelationIdMiddleware.GetCorrelationId(context));
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(context, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = CorrelationIdMiddleware.GetCorrelationId(context);

        if (context.Response.HasStarted)
        {
            logger.LogError(
                exception,
                "Unhandled exception after the response started for {Method} {Path}. CorrelationId: {CorrelationId}",
                context.Request.Method,
                context.Request.Path.Value,
                correlationId);
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        logger.LogError(
            exception,
            "Unhandled exception processing {Method} {Path}. CorrelationId: {CorrelationId}",
            context.Request.Method,
            context.Request.Path.Value,
            correlationId);

        if (context.RequestAborted.IsCancellationRequested)
        {
            return;
        }

        context.Response.Clear();

        var problem = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            Title = "An unexpected error occurred.",
            Status = StatusCodes.Status500InternalServerError,
            Detail = "The request could not be completed. Include the correlation ID if you contact support.",
            Instance = context.Request.Path.Value
        };

        if (!string.IsNullOrEmpty(correlationId))
        {
            problem.Extensions["correlationId"] = correlationId;
        }

        try
        {
            await WriteProblemAsync(context, problem);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug(
                "The client disconnected before the error response was written. CorrelationId: {CorrelationId}",
                correlationId);
        }
    }

    private static Task WriteProblemAsync(HttpContext context, ProblemDetails problem)
    {
        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.Headers["Cache-Control"] = "no-store";

        var payload = new Dictionary<string, object?>();
        AddIfPresent(payload, "type", problem.Type);
        AddIfPresent(payload, "title", problem.Title);

        if (problem.Status is int status)
        {
            payload["status"] = status;
        }

        AddIfPresent(payload, "detail", problem.Detail);
        AddIfPresent(payload, "instance", problem.Instance);

        foreach (var extension in problem.Extensions)
        {
            if (extension.Value is not null)
            {
                payload[extension.Key] = extension.Value;
            }
        }

        return context.Response.WriteAsJsonAsync(
            payload,
            SerializerOptions,
            contentType: "application/problem+json",
            context.RequestAborted);
    }

    private static void AddIfPresent(Dictionary<string, object?> payload, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            payload[name] = value;
        }
    }
}
