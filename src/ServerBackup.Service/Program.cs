using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using ServerBackup.Data;
using ServerBackup.Engine.Notifications;
using ServerBackup.Engine.Scheduling;
using ServerBackup.Service.Components;
using ServerBackup.Service.Scheduling;
using ServerBackup.Service.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(AppContext.BaseDirectory, "logs", "serverbackup-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30));

builder.Services.Configure<ServerBackupOptions>(builder.Configuration.GetSection(ServerBackupOptions.SectionName));
builder.Services.AddSingleton(sp => new RepositoryRegistry(sp.GetRequiredService<IOptions<ServerBackupOptions>>().Value));
builder.Services.AddSingleton<JobQueue>();
builder.Services.AddSingleton<INotifier, WindowsEventLogNotifier>();
builder.Services.AddHostedService<BackupSchedulerService>();

// Windows Authentication (Negotiate/Kerberos) — the natural fit for a
// domain-joined server; every page requires an authenticated Windows
// identity by default (FallbackPolicy) except the health check.
//
// Negotiate implements IAuthenticationRequestHandler, so ASP.NET Core's
// AuthenticationMiddleware invokes it on every request regardless of which
// scheme is "default" — and its handler requires Kestrel's
// IConnectionItemsFeature, which WebApplicationFactory's in-memory
// TestServer doesn't provide. So it's skipped entirely in the "Testing"
// environment; ServerBackup.Service.Tests.AuthorizationTests registers a
// stand-in scheme instead to verify the authorization policy itself. The
// real Negotiate handshake is verified manually against a live Kestrel
// server with a real Windows account.
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
}

builder.Services.AddAuthorization(options => options.FallbackPolicy = options.DefaultPolicy);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

// A repository registered before this build added a migration otherwise
// stays on its old schema forever — nothing else in the codebase applies
// pending migrations to a repo that already existed (only creating a new one
// does). Runs once at startup so every page opens an up-to-date catalog.db.
await MigrateRegisteredRepositoriesAsync(app.Services);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// MapStaticAssets, not UseStaticFiles: it serves every asset under a
// content-hashed URL, so a stylesheet change can never be masked by a
// browser cache holding the previous build's file.
app.MapStaticAssets();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestampUtc = DateTimeOffset.UtcNow })).AllowAnonymous();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

/// <summary>
/// Best-effort per repo: one repository with a locked or corrupt catalog.db
/// must not stop the service from serving every other repository.
/// </summary>
static async Task MigrateRegisteredRepositoriesAsync(IServiceProvider services)
{
    var registry = services.GetRequiredService<RepositoryRegistry>();
    var logger = services.GetRequiredService<ILogger<Program>>();

    foreach (var repoPath in registry.Paths)
    {
        var dbPath = Path.Combine(repoPath, "catalog.db");
        if (!File.Exists(dbPath))
        {
            continue;
        }

        try
        {
            await using var db = CatalogDbContextFactory.Create(dbPath);
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to migrate catalog.db for repository {RepoPath}.", repoPath);
        }
    }
}

namespace ServerBackup.Service
{
    /// <summary>Marker so tests can reference the entry point assembly (WebApplicationFactory/TestServer style).</summary>
    public partial class Program;
}
