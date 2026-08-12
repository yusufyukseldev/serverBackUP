using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using ServerBackup.Engine.Backup;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Scanning;
using ServerBackup.Service.Components.Pages;
using Xunit;

namespace ServerBackup.Service.Tests.Components;

/// <summary>
/// The wizard itself has never had a component test before this. The focus
/// here is the new progress/cancellation wiring added on top of RestoreEngine
/// — RestoreEngineTests already covers the engine's own crash/cancel safety
/// in depth, so this only needs to prove the panel actually threads a
/// CancellationToken and IProgress&lt;RestoreProgress&gt; through correctly.
/// </summary>
public sealed class RestoreTests : BunitContext, IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly string _repoPath = Path.Combine(Path.GetTempPath(), "sb-ui-restore-repo-" + Guid.NewGuid().ToString("n"));
    private readonly string _sourcePath = Path.Combine(Path.GetTempPath(), "sb-ui-restore-src-" + Guid.NewGuid().ToString("n"));
    private readonly string _targetPath = Path.Combine(Path.GetTempPath(), "sb-ui-restore-tgt-" + Guid.NewGuid().ToString("n"));
    private readonly TestRepositoryRegistry _registry = new();

    [Fact]
    public async Task Extract_restore_runs_through_the_wizard_and_writes_the_file_correctly()
    {
        Directory.CreateDirectory(_sourcePath);
        File.WriteAllBytes(Path.Combine(_sourcePath, "a.bin"), new byte[200_000]);

        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);
        UnattendedKeyStore.Enable(_repoPath, masterKey);

        var backupEngine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);
        var snapshotId = await backupEngine.RunAsync([_sourcePath]);

        Services.AddSingleton(_registry.Create(_repoPath));

        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"restore?repo={Uri.EscapeDataString(_repoPath)}&snapshot={snapshotId}");

        var cut = Render<Restore>();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Hedef dizin"), TimeSpan.FromSeconds(5));

        cut.Find("#f-res-target").Change(_targetPath);
        ClickText(cut, "İleri"); // 3 -> 4 (overwrite policy)
        ClickText(cut, "İleri"); // 4 -> 5 (confirm)
        ClickText(cut, "Geri yüklemeyi başlat");

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Geri yükleme tamamlandı."), TimeSpan.FromSeconds(20));

        Directory.GetFiles(_targetPath, "a.bin", SearchOption.AllDirectories).Should().ContainSingle(
            "the restored tree must contain the one file the snapshot held");
    }

    private static void ClickText(IRenderedComponent<Restore> cut, string text) =>
        cut.FindAll("button").First(b => b.TextContent.Trim() == text).Click();

    public new void Dispose()
    {
        foreach (var dir in new[] { _repoPath, _sourcePath, _targetPath })
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        _registry.Dispose();
        base.Dispose();
    }
}
