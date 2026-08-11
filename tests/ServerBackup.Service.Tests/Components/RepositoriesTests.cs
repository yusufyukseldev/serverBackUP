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
    private readonly TestRepositoryRegistry _registry = new();

    [Fact]
    public void Shows_a_helpful_message_when_no_repositories_are_configured()
    {
        Services.AddSingleton(_registry.Create());

        var cut = Render<Repositories>();

        cut.Markup.Should().Contain("Henüz depo yok");
    }

    [Fact]
    public async Task Attaching_an_existing_repository_registers_it_without_a_restart()
    {
        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var registry = _registry.Create();
        Services.AddSingleton(registry);

        var cut = Render<Repositories>();
        ClickText(cut, "Depo ekle");
        cut.Find("#f-add-path").Change(_repoPath);
        ClickText(cut, "Depoyu bağla");

        cut.WaitForAssertion(() => registry.Contains(_repoPath).Should().BeTrue(), TimeSpan.FromSeconds(30));
        cut.FindAll("tbody tr").Should().ContainSingle();
    }

    [Fact]
    public void A_folder_without_a_config_file_is_refused_rather_than_listed_as_a_broken_row()
    {
        Directory.CreateDirectory(_repoPath);
        var registry = _registry.Create();
        Services.AddSingleton(registry);

        var cut = Render<Repositories>();
        ClickText(cut, "Depo ekle");
        cut.Find("#f-add-path").Change(_repoPath);
        ClickText(cut, "Depoyu bağla");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("config.json bulunamadı"), TimeSpan.FromSeconds(30));
        registry.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task Creating_a_repository_initializes_it_and_opens_it_to_the_service()
    {
        var parent = Path.GetDirectoryName(_repoPath)!;
        var name = Path.GetFileName(_repoPath);
        var registry = _registry.Create();
        Services.AddSingleton(registry);

        var cut = Render<Repositories>();
        ClickText(cut, "Depo ekle");
        cut.Find("input[value='create']").Change(true);
        cut.Find("#f-add-parent").Change(parent);
        cut.Find("#f-add-name").Change(name);
        cut.Find("#f-add-pwd").Change(Password);
        cut.Find("#f-add-pwd2").Change(Password);
        ClickText(cut, "Depoyu oluştur");

        cut.WaitForAssertion(() => registry.Contains(_repoPath).Should().BeTrue(), TimeSpan.FromSeconds(60));

        File.Exists(Path.Combine(_repoPath, "config.json")).Should().BeTrue();
        UnattendedKeyStore.IsEnabled(_repoPath).Should().BeTrue(
            "scheduled backups cannot run on a repository the service has to ask a human to unlock");

        // The password must still be the one that was typed, not something derived differently.
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);
        masterKey.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Mismatched_passwords_do_not_create_anything_on_disk()
    {
        var registry = _registry.Create();
        Services.AddSingleton(registry);

        var cut = Render<Repositories>();
        ClickText(cut, "Depo ekle");
        cut.Find("input[value='create']").Change(true);
        cut.Find("#f-add-parent").Change(Path.GetDirectoryName(_repoPath)!);
        cut.Find("#f-add-name").Change(Path.GetFileName(_repoPath));
        cut.Find("#f-add-pwd").Change(Password);
        cut.Find("#f-add-pwd2").Change(Password + "!");
        ClickText(cut, "Depoyu oluştur");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Parolalar birbirini tutmuyor"), TimeSpan.FromSeconds(30));

        Directory.Exists(_repoPath).Should().BeFalse();
        registry.Paths.Should().BeEmpty();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Detaching_a_repository_unregisters_it_and_leaves_every_backup_on_disk()
    {
        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var registry = _registry.Create(_repoPath);
        Services.AddSingleton(registry);

        var cut = Render<Repositories>();
        cut.WaitForAssertion(() => cut.Find("tbody tr"), TimeSpan.FromSeconds(30));

        ClickLabelled(cut, $"'{Path.GetFileName(_repoPath)}' deposunun bağlantısını kaldır");
        cut.Markup.Should().Contain("Diskteki veriler silinmez");

        ClickText(cut, "Bağlantıyı kaldır");
        cut.WaitForAssertion(() => registry.Paths.Should().BeEmpty(), TimeSpan.FromSeconds(30));

        File.Exists(Path.Combine(_repoPath, "config.json")).Should().BeTrue(
            "detaching is unregistering; deleting the backups is never a side effect of it");
    }

    private static void ClickText(IRenderedComponent<Repositories> cut, string text) =>
        cut.FindAll("button").First(b => b.TextContent.Trim().StartsWith(text, StringComparison.Ordinal)).Click();

    /// <summary>A Windows path in a CSS attribute selector reads as escape sequences, so match in C# instead.</summary>
    private static void ClickLabelled(IRenderedComponent<Repositories> cut, string ariaLabel) =>
        cut.FindAll("button").Single(b => b.GetAttribute("aria-label") == ariaLabel).Click();

    [Fact]
    public async Task Clicking_a_row_opens_a_drawer_with_the_repository_config()
    {
        await RepositoryManager.InitializeAsync(_repoPath, Password);
        Services.AddSingleton(_registry.Create(_repoPath));

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
        Services.AddSingleton(_registry.Create(_repoPath));

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

        _registry.Dispose();
        base.Dispose();
    }
}
