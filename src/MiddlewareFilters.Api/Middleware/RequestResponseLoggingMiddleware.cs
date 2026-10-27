using System.Diagnostics;

namespace MiddlewareFilters.Api.Middleware;

/// <summary>
/// Logs the method, path, final status code, elapsed time, and correlation ID.
/// The request body, query string, cookies, and authorization headers are intentionally unread.
/// </summary>
public sealed class RequestResponseLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestResponseLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        Exception? escaped = null;

        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            // The exception is logged by GlobalExceptionMiddleware or the host.
            // This middleware only records how the request finished.
            escaped = exception;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            LogCompletion(context, stopwatch.ElapsedMilliseconds, escaped);
        }
    }

    private void LogCompletion(HttpContext context, long elapsedMilliseconds, Exception? escaped)
    {
        var correlationId = CorrelationIdMiddleware.GetCorrelationId(context);

        if (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation(
                "HTTP {Method} {Path} was canceled after {ElapsedMilliseconds} ms. CorrelationId: {CorrelationId}",
                context.Request.Method,
                context.Request.Path.Value,
                elapsedMilliseconds,
                correlationId);
            return;
        }

        var statusCode = context.Response.StatusCode;
        if (escaped is not null && statusCode < StatusCodes.Status400BadRequest)
        {
            // Downstream failed before it could set an error status.
            statusCode = StatusCodes.Status500InternalServerError;
        }

        logger.LogInformation(
            "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds} ms. CorrelationId: {CorrelationId}",
            context.Request.Method,
            context.Request.Path.Value,
            statusCode,
            elapsedMilliseconds,
            correlationId);
    }
}
