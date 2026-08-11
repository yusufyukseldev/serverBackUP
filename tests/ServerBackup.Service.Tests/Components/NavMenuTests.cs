using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ServerBackup.Data;
using ServerBackup.Data.Entities;
using ServerBackup.Engine.Repository;
using ServerBackup.Service.Components.Layout;
using ServerBackup.Service.Scheduling;
using Xunit;

namespace ServerBackup.Service.Tests.Components;

public sealed class NavMenuTests : BunitContext, IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly string _repoPath = Path.Combine(Path.GetTempPath(), "sb-ui-nav-repo-" + Guid.NewGuid().ToString("n"));

    public NavMenuTests()
    {
        // The footer's theme switch reads the browser's stored preference.
        JSInterop.Setup<string>("sbTheme.get").SetResult("system");
    }

    [Fact]
    public void Renders_a_link_for_every_top_level_page()
    {
        var cut = Render<NavMenu>();

        cut.Markup.Should().Contain("Genel bakış");
        cut.Markup.Should().Contain("Depolar");
        cut.Markup.Should().Contain("Planlar");
        cut.Markup.Should().Contain("İş geçmişi");
        cut.Markup.Should().Contain("Uyarılar");
        cut.Markup.Should().Contain("Snapshot'lar");
        cut.Markup.Should().Contain("Geri yükleme");
    }

    [Fact]
    public void Every_link_points_to_a_distinct_route()
    {
        var cut = Render<NavMenu>();

        var hrefs = cut.FindAll("a").Select(a => a.GetAttribute("href")).ToList();

        hrefs.Should().OnlyHaveUniqueItems();
        hrefs.Should().HaveCount(7);
    }

    [Fact]
    public async Task Shows_the_repo_name_in_the_storage_strip_and_a_danger_badge_for_failed_jobs()
    {
        await RepositoryManager.InitializeAsync(_repoPath, Password);

        await using (var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db")))
        {
            db.Jobs.Add(new JobEntity { JobId = "failed-1", Kind = "backup", Status = "Failed" });
            await db.SaveChangesAsync();
        }

        Services.AddSingleton(Options.Create(new ServerBackupOptions { Repositories = [_repoPath] }));

        var cut = Render<NavMenu>();

        // Generous: the strip is filled by an async load, and when the whole
        // suite runs in parallel a tight bound fails on scheduling, not on the
        // behaviour under test.
        cut.WaitForAssertion(() => cut.Markup.Should().Contain(Path.GetFileName(_repoPath)), TimeSpan.FromSeconds(30));
        cut.Markup.Should().Contain("sb-nav-count--err");
    }

    public new void Dispose()
    {
        if (Directory.Exists(_repoPath))
        {
            Directory.Delete(_repoPath, recursive: true);
        }

        base.Dispose();
    }
}
