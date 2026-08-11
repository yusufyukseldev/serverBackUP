using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServerBackup.Data;
using ServerBackup.Engine.Backup;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Scanning;
using Xunit;

namespace ServerBackup.Integration.Tests;

/// <summary>
/// A whole-volume source always contains paths the running account cannot
/// open (other users' profiles, protected system directories). Those must be
/// skipped and recorded, never abort the run.
/// </summary>
public sealed class BackupSkipsUnreadableEntriesTests : IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly string _repoPath = Path.Combine(Path.GetTempPath(), "sb-skip-repo-" + Guid.NewGuid().ToString("n"));
    private readonly string _sourcePath = Path.Combine(Path.GetTempPath(), "sb-skip-src-" + Guid.NewGuid().ToString("n"));

    public BackupSkipsUnreadableEntriesTests() => Directory.CreateDirectory(_sourcePath);

    /// <summary>
    /// Denies access through the source provider rather than through real
    /// filesystem ACLs, so the test is deterministic and needs no elevation.
    /// </summary>
    private sealed class DenyingSourceProvider(ISourceProvider inner, string deniedName) : ISourceProvider
    {
        public SourceEntry GetEntry(string path) => inner.GetEntry(path);

        public IEnumerable<SourceEntry> EnumerateChildren(string directoryPath)
        {
            if (Path.GetFileName(directoryPath) == deniedName)
            {
                throw new UnauthorizedAccessException($"Access to the path '{directoryPath}' is denied.");
            }

            return inner.EnumerateChildren(directoryPath);
        }

        public Stream OpenRead(string filePath) =>
            Path.GetFileName(filePath) == deniedName
                ? throw new UnauthorizedAccessException($"Access to the path '{filePath}' is denied.")
                : inner.OpenRead(filePath);

        public string? TryGetSddl(string path) => inner.TryGetSddl(path);
    }

    [Fact]
    public async Task Unreadable_file_is_skipped_and_the_rest_is_still_backed_up()
    {
        File.WriteAllText(Path.Combine(_sourcePath, "readable.txt"), "keep me");
        File.WriteAllText(Path.Combine(_sourcePath, "denied.txt"), "cannot read");

        var masterKey = await InitAndUnlockAsync();
        BackupProgress? last = null;
        var engine = new BackupEngine(
            new DenyingSourceProvider(new LocalSourceProvider(), "denied.txt"),
            _repoPath,
            masterKey,
            progress: new Progress<BackupProgress>(p => last = p));

        var snapshotId = await engine.RunAsync([_sourcePath]);

        snapshotId.Should().NotBeNullOrEmpty("an inaccessible file must not fail the whole backup");

        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));
        var audit = await db.AuditLogs.AsNoTracking().Where(a => a.Action == "backup-skipped").ToListAsync();
        audit.Should().ContainSingle("the skip must be recorded, not silent");
        audit[0].Details.Should().Contain("denied.txt");
    }

    [Fact]
    public async Task Unreadable_directory_is_skipped_and_the_rest_is_still_backed_up()
    {
        var deniedDir = Path.Combine(_sourcePath, "locked");
        Directory.CreateDirectory(deniedDir);
        File.WriteAllText(Path.Combine(deniedDir, "inside.txt"), "unreachable");
        File.WriteAllText(Path.Combine(_sourcePath, "readable.txt"), "keep me");

        var masterKey = await InitAndUnlockAsync();
        var engine = new BackupEngine(
            new DenyingSourceProvider(new LocalSourceProvider(), "locked"), _repoPath, masterKey);

        var snapshotId = await engine.RunAsync([_sourcePath]);

        snapshotId.Should().NotBeNullOrEmpty();

        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));
        var audit = await db.AuditLogs.AsNoTracking().Where(a => a.Action == "backup-skipped").ToListAsync();
        audit.Should().ContainSingle();
        audit[0].Details.Should().Contain("locked");
    }

    [Fact]
    public async Task A_clean_run_records_no_skip_entry()
    {
        File.WriteAllText(Path.Combine(_sourcePath, "readable.txt"), "keep me");

        var masterKey = await InitAndUnlockAsync();
        var engine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);

        await engine.RunAsync([_sourcePath]);

        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));
        (await db.AuditLogs.AsNoTracking().CountAsync(a => a.Action == "backup-skipped")).Should().Be(0);
    }

    [Theory]
    [InlineData("C:\\", "C")]
    [InlineData("D:\\", "D")]
    [InlineData("Veri", "Veri")]
    [InlineData("Muhasebe", "Muhasebe")]
    public void Volume_root_names_are_reduced_to_a_single_safe_segment(string name, string expected) =>
        // A rooted name would make Path.Combine on restore discard the target
        // directory and write straight back onto the live volume.
        BackupEngine.RootNodeName(name).Should().Be(expected);

    private async Task<byte[]> InitAndUnlockAsync()
    {
        await RepositoryManager.InitializeAsync(_repoPath, Password);
        return await RepositoryKeyStore.UnlockAsync(_repoPath, Password);
    }

    public void Dispose()
    {
        TryDelete(_repoPath);
        TryDelete(_sourcePath);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temp directory cleanup is best-effort.
        }
    }
}
