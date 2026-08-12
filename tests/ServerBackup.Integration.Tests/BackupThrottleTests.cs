using System.Diagnostics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServerBackup.Core.Crypto;
using ServerBackup.Data;
using ServerBackup.Engine.Backup;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Scanning;
using Xunit;

namespace ServerBackup.Integration.Tests;

/// <summary>
/// End-to-end proof that <c>maxBytesPerSecond</c> caps the real throughput of
/// a backup run, and that leaving it unset changes nothing.
/// </summary>
public sealed class BackupThrottleTests : IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly string _sourcePath = Path.Combine(Path.GetTempPath(), "sb-throttle-src-" + Guid.NewGuid().ToString("n"));
    private readonly string _unthrottledRepoPath = Path.Combine(Path.GetTempPath(), "sb-throttle-repo-a-" + Guid.NewGuid().ToString("n"));
    private readonly string _throttledRepoPath = Path.Combine(Path.GetTempPath(), "sb-throttle-repo-b-" + Guid.NewGuid().ToString("n"));

    public BackupThrottleTests() => Directory.CreateDirectory(_sourcePath);

    /// <summary>
    /// Asserts the ABSOLUTE rate, not merely "throttled is slower": the run is
    /// expected to take (bytes actually moved) / (configured rate) seconds,
    /// where "bytes moved" is the source bytes read plus the compressed bytes
    /// written — the two things that are charged against the shared bucket.
    /// The unthrottled run of the same source is measured too and must land
    /// clearly below the tolerance floor, so the assertion cannot be satisfied
    /// by incidental pipeline overhead.
    /// </summary>
    [Fact]
    public async Task A_configured_rate_limit_paces_the_run_at_that_rate()
    {
        const int fileCount = 4;
        const int bytesPerFile = 1024 * 1024;
        const long maxBytesPerSecond = 3_000_000;
        WriteRandomFiles(_sourcePath, fileCount, bytesPerFile);

        var unthrottledElapsed = await RunAsync(_unthrottledRepoPath, maxBytesPerSecond: null);
        var throttledElapsed = await RunAsync(_throttledRepoPath, maxBytesPerSecond);

        // Read side: the chunker walks every source byte exactly once.
        long chargedBytes = (long)fileCount * bytesPerFile;
        // Write side: what PackFileSet charged is the pre-encryption compressed
        // length, i.e. the stored length minus the fixed AEAD tag per blob.
        await using (var db = CatalogDbContextFactory.Create(Path.Combine(_throttledRepoPath, "catalog.db")))
        {
            chargedBytes += await db.Blobs.SumAsync(b => (long)b.LenStored);
            chargedBytes -= AeadCipher.TagSizeBytes * await db.Blobs.CountAsync();
        }

        var expectedSeconds = chargedBytes / (double)maxBytesPerSecond;

        throttledElapsed.TotalSeconds.Should().BeGreaterThan(expectedSeconds * 0.5,
            $"{chargedBytes:N0} bytes at {maxBytesPerSecond:N0} B/s cannot be moved in much under {expectedSeconds:F2} s");
        throttledElapsed.TotalSeconds.Should().BeLessThan((expectedSeconds * 1.7) + 0.5,
            $"the throttle must not hold the run far beyond {expectedSeconds:F2} s");

        unthrottledElapsed.TotalSeconds.Should().BeLessThan(expectedSeconds * 0.5,
            "the same backup without a limit must finish below the throttled tolerance floor — " +
            "otherwise the assertions above would pass on pipeline overhead alone");
    }

    /// <summary>
    /// The zero-cost guarantee, checked structurally rather than by timing: an
    /// unconfigured engine holds no throttle at all, so every guarded call site
    /// is skipped and the unthrottled code path is byte-for-byte the one that
    /// existed before the limit was added.
    /// </summary>
    [Fact]
    public async Task An_unconfigured_engine_holds_no_throttle_instance()
    {
        await RepositoryManager.InitializeAsync(_unthrottledRepoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_unthrottledRepoPath, Password);

        var unlimited = new BackupEngine(new LocalSourceProvider(), _unthrottledRepoPath, masterKey);
        var limited = new BackupEngine(new LocalSourceProvider(), _unthrottledRepoPath, masterKey, maxBytesPerSecond: 1_000_000);

        unlimited.Throttle.Should().BeNull();
        limited.Throttle.Should().NotBeNull();
        limited.Throttle!.BytesPerSecond.Should().Be(1_000_000);
    }

    /// <summary>
    /// Throttling is a pacing concern only: it must not alter a single stored
    /// byte. Both runs go into the SAME repository — blob ids are derived from
    /// the repository's master key, so ids from two independently initialized
    /// repositories are incomparable by construction.
    /// </summary>
    [Fact]
    public async Task Throttling_produces_the_same_snapshot_content_as_no_throttling()
    {
        WriteRandomFiles(_sourcePath, fileCount: 3, bytesPerFile: 300_000);

        await RepositoryManager.InitializeAsync(_unthrottledRepoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_unthrottledRepoPath, Password);

        var unthrottledSnapshot = await new BackupEngine(new LocalSourceProvider(), _unthrottledRepoPath, masterKey)
            .RunAsync([_sourcePath]);
        var blobsAfterUnthrottled = await BlobIdsAsync(_unthrottledRepoPath);

        // No parent snapshot: a full rescan, so the throttled run re-chunks and
        // re-hashes every byte rather than taking the unchanged-file fast path.
        var throttledSnapshot = await new BackupEngine(
                new LocalSourceProvider(), _unthrottledRepoPath, masterKey, maxBytesPerSecond: 50_000_000)
            .RunAsync([_sourcePath]);
        var blobsAfterThrottled = await BlobIdsAsync(_unthrottledRepoPath);

        (await RootTreeBlobIdAsync(_unthrottledRepoPath, throttledSnapshot))
            .Should().Be(await RootTreeBlobIdAsync(_unthrottledRepoPath, unthrottledSnapshot));
        blobsAfterThrottled.Should().Equal(blobsAfterUnthrottled,
            "a throttled run must store exactly the same content as an unthrottled one");
    }

    private async Task<TimeSpan> RunAsync(string repoPath, long? maxBytesPerSecond)
    {
        await RepositoryManager.InitializeAsync(repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(repoPath, Password);
        var engine = new BackupEngine(
            new LocalSourceProvider(), repoPath, masterKey, maxBytesPerSecond: maxBytesPerSecond);

        var sw = Stopwatch.StartNew();
        await engine.RunAsync([_sourcePath]);
        sw.Stop();

        return sw.Elapsed;
    }

    private static async Task<string> RootTreeBlobIdAsync(string repoPath, string snapshotId)
    {
        await using var db = CatalogDbContextFactory.Create(Path.Combine(repoPath, "catalog.db"));
        var snapshot = await db.Snapshots.AsNoTracking().SingleAsync(s => s.SnapshotId == snapshotId);
        return snapshot.RootTreeBlobId;
    }

    private static async Task<List<string>> BlobIdsAsync(string repoPath)
    {
        await using var db = CatalogDbContextFactory.Create(Path.Combine(repoPath, "catalog.db"));
        return await db.Blobs.AsNoTracking().Select(b => b.BlobId).OrderBy(id => id).ToListAsync();
    }

    private static void WriteRandomFiles(string directory, int fileCount, int bytesPerFile)
    {
        for (var i = 0; i < fileCount; i++)
        {
            var bytes = new byte[bytesPerFile];
            Random.Shared.NextBytes(bytes);
            File.WriteAllBytes(Path.Combine(directory, $"file{i}.bin"), bytes);
        }
    }

    public void Dispose()
    {
        foreach (var path in new[] { _sourcePath, _unthrottledRepoPath, _throttledRepoPath })
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }
}
