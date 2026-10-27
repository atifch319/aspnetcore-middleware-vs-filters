using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using MiddlewareFilters.Api.Middleware;
using MiddlewareFilters.Api.Tests.Support;

namespace MiddlewareFilters.Api.Tests.Middleware;

public sealed class RequestResponseLoggingMiddlewareTests
{
    [Fact]
    public async Task Does_not_change_the_response_body()
    {
        var context = CreateContext();
        var logger = new CollectingLogger<RequestResponseLoggingMiddleware>();
        var middleware = new RequestResponseLoggingMiddleware(
            async httpContext =>
            {
                httpContext.Response.StatusCode = StatusCodes.Status201Created;
                await httpContext.Response.WriteAsync("original-body");
            },
            logger);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
        Assert.Equal("original-body", await ReadBodyAsync(context));

        var entry = Assert.Single(logger.Entries);
        Assert.Null(entry.Exception);
        Assert.Contains("POST", entry.Message, StringComparison.Ordinal);
        Assert.Contains("/api/products", entry.Message, StringComparison.Ordinal);
        Assert.Contains("201", entry.Message, StringComparison.Ordinal);
        Assert.Contains("cid-12345678", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logs_completion_when_downstream_throws_without_logging_the_exception()
    {
        var context = CreateContext();
        var logger = new CollectingLogger<RequestResponseLoggingMiddleware>();
        var middleware = new RequestResponseLoggingMiddleware(
            _ => throw new InvalidOperationException("secret failure"),
            logger);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Equal("secret failure", exception.Message);
        Assert.Equal(0, context.Response.Body.Length);

        var entry = Assert.Single(logger.Entries);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain("secret failure", entry.Message, StringComparison.Ordinal);
        Assert.Contains("500", entry.Message, StringComparison.Ordinal);
        Assert.Contains("cid-12345678", entry.Message, StringComparison.Ordinal);
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        var response = new TestHttpResponseFeature();
        context.Features.Set<IHttpResponseFeature>(response);
        context.Features.Set<IHttpResponseBodyFeature>(response);
        context.Request.Method = "POST";
        context.Request.Path = "/api/products";
        context.Items[CorrelationIdMiddleware.ItemKey] = "cid-12345678";
        return context;
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
