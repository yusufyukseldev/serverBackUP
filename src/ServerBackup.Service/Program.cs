using Microsoft.AspNetCore.Authentication.Negotiate;
using Serilog;
using ServerBackup.Engine.Notifications;
using ServerBackup.Engine.Scheduling;
using ServerBackup.Service.Components;
using ServerBackup.Service.Scheduling;

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

app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestampUtc = DateTimeOffset.UtcNow })).AllowAnonymous();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

namespace ServerBackup.Service
{
    /// <summary>Marker so tests can reference the entry point assembly (WebApplicationFactory/TestServer style).</summary>
    public partial class Program;
}
