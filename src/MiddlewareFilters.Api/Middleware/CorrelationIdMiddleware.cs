namespace MiddlewareFilters.Api.Middleware;

/// <summary>
/// Accepts a safe <c>X-Correlation-ID</c> or replaces it with a generated identifier.
/// The value is stored for logging and error responses, then copied to the response
/// when the response starts. Arbitrary header text is rejected so it cannot be
/// reflected into response headers or logs.
/// </summary>
public sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";

    private const int MinimumLength = 1;
    private const int MaximumLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);
        context.Items[ItemKey] = correlationId;

        // OnStarting still runs if a later middleware clears headers before the response starts.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object>
        {
            [ItemKey] = correlationId
        }))
        {
            await next(context);
        }
    }

    public static string? GetCorrelationId(HttpContext context)
    {
        return context.Items.TryGetValue(ItemKey, out var value) ? value as string : null;
    }

    private string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var values) && values.Count == 1)
        {
            var candidate = values.ToString();
            if (IsValidCorrelationId(candidate))
            {
                return candidate;
            }

            logger.LogDebug("Rejected an invalid correlation ID and generated a new identifier.");
        }

        return Guid.NewGuid().ToString("D");
    }

    private static bool IsValidCorrelationId(string value)
    {
        if (value.Length is < MinimumLength or > MaximumLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
