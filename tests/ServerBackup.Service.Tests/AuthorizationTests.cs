using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ServerBackup.Service.Scheduling;
using Xunit;

namespace ServerBackup.Service.Tests;

/// <summary>
/// Verifies the authorization policy itself (every page requires an
/// authenticated identity except /health). Negotiate is swapped for
/// <see cref="TestAuthHandler"/> here — see that class for why — so this
/// exercises OUR wiring (FallbackPolicy + AllowAnonymous), not Microsoft's
/// Negotiate handshake. The real handshake was verified manually against a
/// live Kestrel server with a real Windows account.
/// </summary>
public sealed class AuthorizationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _stateDirectory =
        Path.Combine(Path.GetTempPath(), "sb-auth-state-" + Guid.NewGuid().ToString("n"));

    private readonly WebApplicationFactory<Program> _factory;

    public AuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");

            // This boots the real Program, and RepositoryRegistry persists its
            // list. Left at the default it would write into the machine's
            // %ProgramData% — a test must never touch the operator's real state.
            builder.UseSetting($"{ServerBackupOptions.SectionName}:DataDirectory", _stateDirectory);

            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            });
        });
    }

    [Fact]
    public async Task Anonymous_requests_to_the_dashboard_are_rejected()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authenticated_requests_to_the_dashboard_are_allowed()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthenticatedHeader, "true");

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_health_endpoint_is_reachable_without_authentication()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/plans")]
    [InlineData("/repositories")]
    [InlineData("/jobs")]
    [InlineData("/snapshots")]
    [InlineData("/restore")]
    public async Task Every_management_page_requires_authentication(string path)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"'{path}' must not be reachable anonymously");
    }

    public void Dispose()
    {
        if (Directory.Exists(_stateDirectory))
        {
            Directory.Delete(_stateDirectory, recursive: true);
        }
    }
}
