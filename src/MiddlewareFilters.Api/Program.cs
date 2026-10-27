using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using MiddlewareFilters.Api.Authentication;
using MiddlewareFilters.Api.Filters;
using MiddlewareFilters.Api.Middleware;
using MiddlewareFilters.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });
builder.Services.AddSingleton<ProductService>();
builder.Services.AddScoped<ValidateProductActionFilter>();

// The demo scheme only authenticates in Development and Testing.
// In every other environment its handler returns no user, so [Authorize] fails closed.
builder.Services.AddAuthentication(DemoAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, DemoAuthenticationHandler>(
        DemoAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization();

var app = builder.Build();

// Correlation is outside logging and exception handling so both can read the same id.
app.UseMiddleware<CorrelationIdMiddleware>();

// Logging wraps exception handling and records the status that handler writes.
app.UseMiddleware<RequestResponseLoggingMiddleware>();

// Handles exceptions from routing, authentication, authorization, and MVC.
// In Development the host also places the developer exception page outside this pipeline.
// This middleware handles the exception and does not rethrow, so the client still gets ProblemDetails.
app.UseMiddleware<GlobalExceptionMiddleware>();

// Explicit routing keeps this middleware outside endpoint matching.
// WebApplication would otherwise insert UseRouting before user middleware.
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
