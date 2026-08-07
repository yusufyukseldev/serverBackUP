using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ServerBackup.Engine.Repository;
using ServerBackup.Service.Components.Pages;
using ServerBackup.Service.Scheduling;
using Xunit;

namespace ServerBackup.Service.Tests.Components;

public sealed class RepositoriesTests : BunitContext, IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly string _repoPath = Path.Combine(Path.GetTempPath(), "sb-ui-repos-repo-" + Guid.NewGuid().ToString("n"));

    [Fact]
    public void Shows_a_helpful_message_when_no_repositories_are_configured()
    {
        Services.AddSingleton(Options.Create(new ServerBackupOptions { Repositories = [] }));

        var cut = Render<Repositories>();

        cut.Markup.Should().Contain("Yapılandırılmış depo yok");
    }

    [Fact]
    public async Task Clicking_a_row_opens_a_drawer_with_the_repository_config()
    {
        await RepositoryManager.InitializeAsync(_repoPath, Password);
        Services.AddSingleton(Options.Create(new ServerBackupOptions { Repositories = [_repoPath] }));

        var cut = Render<Repositories>();
        cut.WaitForAssertion(() => cut.Find("tr.is-clickable"), TimeSpan.FromSeconds(5));

        cut.Find("tr.is-clickable").Click();

        cut.Markup.Should().Contain("sb-drawer");
        cut.Markup.Should().Contain("Argon2id");
    }

    [Fact]
    public async Task Verify_dialog_reports_no_issues_for_a_freshly_initialized_repository()
    {
        await RepositoryManager.InitializeAsync(_repoPath, Password);
        Services.AddSingleton(Options.Create(new ServerBackupOptions { Repositories = [_repoPath] }));

        var cut = Render<Repositories>();
        cut.WaitForAssertion(() => cut.Find("tr.is-clickable"), TimeSpan.FromSeconds(5));
        cut.Find("tr.is-clickable").Click();

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Doğrula").Click();
        cut.Find("input[type='password']").Change(Password);
        cut.FindAll("button").Single(b => b.TextContent.Contains("Doğrulamayı başlat")).Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Sorun bulunamadı"), TimeSpan.FromSeconds(5));
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
