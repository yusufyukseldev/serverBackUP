using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ServerBackup.Service.Tests;

/// <summary>
/// Stand-in for Negotiate in tests. The real Negotiate handler requires
/// Kestrel's IConnectionItemsFeature, which WebApplicationFactory's in-memory
/// TestServer doesn't provide — that's a testing-infrastructure limitation of
/// the library, not something specific to this app (the real handshake is
/// verified manually against a live Kestrel server with a real Windows
/// account instead). This handler exists purely so
/// <see cref="AuthorizationTests"/> can verify OUR policy wiring
/// (FallbackPolicy + AllowAnonymous on /health): no marker header means "no
/// identity", exactly like an anonymous request against Negotiate.
/// </summary>
internal sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestScheme";
    public const string AuthenticatedHeader = "X-Test-Authenticated";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(AuthenticatedHeader, out var value) || value != "true")
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "test-user")], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
