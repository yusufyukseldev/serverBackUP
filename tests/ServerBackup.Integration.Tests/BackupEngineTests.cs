using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServerBackup.Core.Crypto;
using ServerBackup.Core.Repository;
using ServerBackup.Data;
using ServerBackup.Engine.Backup;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Scanning;
using Xunit;

namespace ServerBackup.Integration.Tests;

public sealed class BackupEngineTests : IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly string _repoPath = Path.Combine(Path.GetTempPath(), "sb-backup-repo-" + Guid.NewGuid().ToString("n"));
    private readonly string _sourcePath = Path.Combine(Path.GetTempPath(), "sb-backup-src-" + Guid.NewGuid().ToString("n"));

    public BackupEngineTests() => Directory.CreateDirectory(_sourcePath);

    [Fact]
    public async Task Backing_up_identical_content_twice_writes_zero_new_blobs()
    {
        WriteRandomFiles(_sourcePath, fileCount: 4, bytesPerFile: 600_000);

        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);
        var engine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);

        await engine.RunAsync([_sourcePath]);
        var blobCountAfterFirst = await CountBlobsAsync();
        blobCountAfterFirst.Should().BeGreaterThan(0);

        // No parent given — this forces a full rescan, proving dedup works
        // purely from content-addressed ids, independent of the parent-cache
        // fast path tested separately below.
        await engine.RunAsync([_sourcePath]);
        var blobCountAfterSecond = await CountBlobsAsync();

        blobCountAfterSecond.Should().Be(blobCountAfterFirst, "identical content must not produce any new blobs");
    }

    [Fact]
    public async Task Incremental_backup_skips_unchanged_files_and_writes_little_new_data()
    {
        const int fileCount = 4;
        const int bytesPerFile = 3_000_000;
        var files = WriteRandomFiles(_sourcePath, fileCount, bytesPerFile);

        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        List<BackupProgress> progressReports = [];
        var progress = new Progress<BackupProgress>(progressReports.Add);
        var engine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey, progress: progress);

        var snapshot1 = await engine.RunAsync([_sourcePath]);
        var totalBytesAfterFirst = await TotalPackBytesAsync();

        // Change exactly one file's content (and therefore its mtime).
        await Task.Delay(50); // ensure a distinct, detectable mtime on filesystems with coarse resolution
        File.WriteAllBytes(files[0], RandomBytes(bytesPerFile));

        progressReports.Clear();
        await engine.RunAsync([_sourcePath], parentSnapshotId: snapshot1);
        var totalBytesAfterSecond = await TotalPackBytesAsync();

        var finalReport = progressReports[^1];
        finalReport.FilesUnchanged.Should().Be(fileCount - 1);
        finalReport.FilesChanged.Should().Be(1);

        var newBytesWritten = totalBytesAfterSecond - totalBytesAfterFirst;
        newBytesWritten.Should().BeLessThan(bytesPerFile * 2,
            "an incremental backup must not re-store the untouched files");
    }

    [Fact]
    public async Task Concurrent_backups_against_the_same_repository_are_rejected()
    {
        WriteRandomFiles(_sourcePath, fileCount: 1, bytesPerFile: 10_000);

        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        using var heldLock = RepositoryLock.Acquire(_repoPath);
        var engine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);

        var act = async () => await engine.RunAsync([_sourcePath]);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task A_cancelled_backup_leaves_the_repository_consistent()
    {
        WriteRandomFiles(_sourcePath, fileCount: 50, bytesPerFile: 200_000);

        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        using var cts = new CancellationTokenSource();
        var progress = new Progress<BackupProgress>(p =>
        {
            if (p.FilesScanned >= 1)
            {
                cts.Cancel();
            }
        });
        var engine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey, progress: progress);

        var act = async () => await engine.RunAsync([_sourcePath], ct: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // The lock must be released.
        File.Exists(Path.Combine(_repoPath, "locks", "repo.lock")).Should().BeFalse();

        // Every pack file that DOES exist on disk must be a fully valid,
        // parseable pack — no half-written pack left looking legitimate.
        var packSubKey = SubKeys.Derive(masterKey, SubKeys.PackKeyInfo);
        var dataDir = Path.Combine(_repoPath, "data");
        if (Directory.Exists(dataDir))
        {
            foreach (var packFile in Directory.EnumerateFiles(dataDir, "*.pack", SearchOption.AllDirectories))
            {
                using var stream = File.OpenRead(packFile);
                var reading = () => new PackReader(stream, packSubKey);
                reading.Should().NotThrow($"'{packFile}' must either not exist or be a complete, valid pack");
            }
        }

        // The repository must still be fully usable afterward.
        var recovery = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);
        var snapshotId = await recovery.RunAsync([_sourcePath]);
        snapshotId.Should().NotBeNullOrEmpty();
    }

    private async Task<int> CountBlobsAsync()
    {
        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));
        return await db.Blobs.CountAsync();
    }

    private async Task<long> TotalPackBytesAsync()
    {
        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));
        return await db.Packs.SumAsync(p => (long?)p.SizeBytes) ?? 0;
    }

    private static List<string> WriteRandomFiles(string directory, int fileCount, int bytesPerFile)
    {
        var paths = new List<string>();
        for (var i = 0; i < fileCount; i++)
        {
            var path = Path.Combine(directory, $"file{i}.bin");
            File.WriteAllBytes(path, RandomBytes(bytesPerFile));
            paths.Add(path);
        }

        return paths;
    }

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }

    public void Dispose()
    {
        if (Directory.Exists(_repoPath))
        {
            Directory.Delete(_repoPath, recursive: true);
        }

        if (Directory.Exists(_sourcePath))
        {
            Directory.Delete(_sourcePath, recursive: true);
        }
    }
}
