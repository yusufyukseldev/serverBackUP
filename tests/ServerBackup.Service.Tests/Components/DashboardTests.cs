using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ServerBackup.Engine.Backup;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Scanning;
using ServerBackup.Service.Components.Pages;
using ServerBackup.Service.Scheduling;
using Xunit;

namespace ServerBackup.Service.Tests.Components;

public sealed class DashboardTests : BunitContext, IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly string _repoPath = Path.Combine(Path.GetTempPath(), "sb-ui-dash-repo-" + Guid.NewGuid().ToString("n"));
    private readonly string _sourcePath = Path.Combine(Path.GetTempPath(), "sb-ui-dash-src-" + Guid.NewGuid().ToString("n"));

    [Fact]
    public void Shows_a_helpful_message_when_no_repositories_are_configured()
    {
        Services.AddSingleton(Options.Create(new ServerBackupOptions { Repositories = [] }));

        var cut = Render<Dashboard>();

        cut.Markup.Should().Contain("Yapılandırılmış depo yok");
    }

    [Fact]
    public async Task Shows_the_snapshot_count_for_a_real_repository()
    {
        Directory.CreateDirectory(_sourcePath);
        File.WriteAllBytes(Path.Combine(_sourcePath, "a.txt"), "hello"u8.ToArray());

        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);
        var engine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);
        await engine.RunAsync([_sourcePath]);

        Services.AddSingleton(Options.Create(new ServerBackupOptions { Repositories = [_repoPath] }));

        var cut = Render<Dashboard>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain(_repoPath), TimeSpan.FromSeconds(5));

        cut.Markup.Should().Contain(">1<", "exactly one snapshot exists in this repository");
    }

    public new void Dispose()
    {
        foreach (var dir in new[] { _repoPath, _sourcePath })
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        base.Dispose();
    }
}
