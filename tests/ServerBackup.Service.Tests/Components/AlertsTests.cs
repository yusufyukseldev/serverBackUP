using Bunit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ServerBackup.Data;
using ServerBackup.Data.Entities;
using ServerBackup.Engine.Repository;
using ServerBackup.Service.Components.Pages;
using ServerBackup.Service.Scheduling;
using Xunit;

namespace ServerBackup.Service.Tests.Components;

public sealed class AlertsTests : BunitContext, IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly string _repoPath = Path.Combine(Path.GetTempPath(), "sb-ui-alerts-repo-" + Guid.NewGuid().ToString("n"));
    private readonly TestRepositoryRegistry _registry = new();

    [Fact]
    public void Shows_no_open_alerts_message_when_no_repositories_are_configured()
    {
        Services.AddSingleton(_registry.Create());

        var cut = Render<Alerts>();

        cut.Markup.Should().Contain("Açık uyarı yok");
    }

    [Fact]
    public async Task Lists_failed_jobs_but_not_succeeded_ones()
    {
        await RepositoryManager.InitializeAsync(_repoPath, Password);

        await using (var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db")))
        {
            db.Jobs.Add(new JobEntity { JobId = "failed-1", Kind = "backup", Status = "Failed", ErrorMessage = "disk doldu" });
            db.Jobs.Add(new JobEntity { JobId = "ok-1", Kind = "backup", Status = "Succeeded" });
            await db.SaveChangesAsync();
        }

        Services.AddSingleton(_registry.Create(_repoPath));

        var cut = Render<Alerts>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("failed-1"), TimeSpan.FromSeconds(5));

        cut.Markup.Should().Contain("disk doldu");
        cut.Markup.Should().NotContain("ok-1");
    }

    public new void Dispose()
    {
        if (Directory.Exists(_repoPath))
        {
            Directory.Delete(_repoPath, recursive: true);
        }

        _registry.Dispose();
        base.Dispose();
    }
}
