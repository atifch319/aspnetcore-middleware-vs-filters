using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using MiddlewareFilters.Api.Middleware;
using MiddlewareFilters.Api.Tests.Support;

namespace MiddlewareFilters.Api.Tests.Middleware;

public sealed class GlobalExceptionMiddlewareTests
{
    [Fact]
    public async Task Returns_a_safe_problem_details_response()
    {
        const string secret = "Diagnostic exception for pipeline demonstration.";
        var context = CreateContext();
        context.Items[CorrelationIdMiddleware.ItemKey] = "cid-12345678";

        var middleware = new GlobalExceptionMiddleware(
            _ => throw new InvalidOperationException(secret),
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());

        var body = await ReadBodyAsync(context);
        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(body);
        Assert.Equal(500, document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("An unexpected error occurred.", document.RootElement.GetProperty("title").GetString());
        Assert.Equal("cid-12345678", document.RootElement.GetProperty("correlationId").GetString());
        Assert.False(document.RootElement.TryGetProperty("exception", out _));
        Assert.False(document.RootElement.TryGetProperty("extensions", out _));
    }

    [Fact]
    public async Task Does_not_write_a_response_when_the_client_cancels()
    {
        var context = CreateContext();
        var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        context.RequestAborted = cancellation.Token;

        var middleware = new GlobalExceptionMiddleware(
            _ => throw new OperationCanceledException(context.RequestAborted),
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task Does_not_replace_a_response_that_has_already_started()
    {
        var context = CreateContext();
        var middleware = new GlobalExceptionMiddleware(
            async httpContext =>
            {
                await httpContext.Response.StartAsync();
                await httpContext.Response.Body.WriteAsync(Encoding.UTF8.GetBytes("partial-body"));
                throw new InvalidOperationException("secret failure");
            },
            NullLogger<GlobalExceptionMiddleware>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Equal("secret failure", exception.Message);
        Assert.Equal("partial-body", await ReadBodyAsync(context));
        Assert.DoesNotContain("secret failure", await ReadBodyAsync(context), StringComparison.Ordinal);
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        var response = new TestHttpResponseFeature();
        context.Features.Set<IHttpResponseFeature>(response);
        context.Features.Set<IHttpResponseBodyFeature>(response);
        context.Request.Method = "GET";
        context.Request.Path = "/api/products/diagnostics/exception";
        return context;
    }

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
