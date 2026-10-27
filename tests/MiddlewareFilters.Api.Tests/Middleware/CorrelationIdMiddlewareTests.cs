using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using MiddlewareFilters.Api.Middleware;
using MiddlewareFilters.Api.Tests.Support;

namespace MiddlewareFilters.Api.Tests.Middleware;

public sealed class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task Generates_an_identifier_when_the_header_is_missing()
    {
        var context = CreateContext();
        var middleware = CreateMiddleware();

        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        var correlationId = CorrelationIdMiddleware.GetCorrelationId(context);
        Assert.NotNull(correlationId);
        Assert.True(Guid.TryParse(correlationId, out _));
        Assert.Equal(correlationId, context.Response.Headers[CorrelationIdMiddleware.HeaderName]);
    }

    [Fact]
    public async Task Returns_a_valid_incoming_identifier()
    {
        var context = CreateContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "portfolio-demo-001";
        var middleware = CreateMiddleware();

        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        Assert.Equal("portfolio-demo-001", CorrelationIdMiddleware.GetCorrelationId(context));
        Assert.Equal("portfolio-demo-001", context.Response.Headers[CorrelationIdMiddleware.HeaderName]);
    }

    [Theory]
    [InlineData("not safe")]
    [InlineData("../not-safe")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Replaces_an_invalid_identifier(string incoming)
    {
        var context = CreateContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = incoming;
        var middleware = CreateMiddleware();

        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        var correlationId = CorrelationIdMiddleware.GetCorrelationId(context);
        Assert.NotNull(correlationId);
        Assert.NotEqual(incoming, correlationId);
        Assert.True(Guid.TryParse(correlationId, out _));
        Assert.Equal(correlationId, context.Response.Headers[CorrelationIdMiddleware.HeaderName]);
        Assert.DoesNotContain(incoming, context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replaces_multiple_header_values()
    {
        var context = CreateContext();
        context.Request.Headers.Append(CorrelationIdMiddleware.HeaderName, "alpha-one");
        context.Request.Headers.Append(CorrelationIdMiddleware.HeaderName, "beta-two");
        var middleware = CreateMiddleware();

        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        var correlationId = CorrelationIdMiddleware.GetCorrelationId(context);
        Assert.NotNull(correlationId);
        Assert.True(Guid.TryParse(correlationId, out _));
        Assert.DoesNotContain("alpha-one", correlationId, StringComparison.Ordinal);
        Assert.DoesNotContain("beta-two", correlationId, StringComparison.Ordinal);
    }

    private static CorrelationIdMiddleware CreateMiddleware()
    {
        return new CorrelationIdMiddleware(
            _ => Task.CompletedTask,
            NullLogger<CorrelationIdMiddleware>.Instance);
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        var response = new TestHttpResponseFeature();
        context.Features.Set<IHttpResponseFeature>(response);
        context.Features.Set<IHttpResponseBodyFeature>(response);
        return context;
    }
}
