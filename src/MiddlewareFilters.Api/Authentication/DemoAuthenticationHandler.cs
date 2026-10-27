using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MiddlewareFilters.Api.Authentication;

/// <summary>
/// Shows where authentication middleware sits in the pipeline.
/// This is not a production authentication scheme: there is no credential, secret, or identity provider.
/// In Development and Testing, a safe <c>X-Demo-User</c> value becomes the user name.
/// In every other environment the handler authenticates nobody, so <c>[Authorize]</c> fails closed.
/// </summary>
public sealed class DemoAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Demo";
    public const string HeaderName = "X-Demo-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!Request.Headers.TryGetValue(HeaderName, out var values) || values.Count != 1)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var userName = values.ToString();
        if (!IsSafeDemoUserName(userName))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, userName)],
            authenticationType: SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static bool IsSafeDemoUserName(string value)
    {
        if (value.Length is < 1 or > 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
