using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServerBackup.Core.Crypto;
using ServerBackup.Core.Repository;
using ServerBackup.Data;
using ServerBackup.Engine.Backup;
using ServerBackup.Engine.Prune;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Restore;
using ServerBackup.Engine.Retention;
using ServerBackup.Engine.Scanning;
using ServerBackup.Engine.Verify;
using Xunit;

namespace ServerBackup.Integration.Tests;

public sealed class PruneEngineTests : IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly string _repoPath = Path.Combine(Path.GetTempPath(), "sb-prune-repo-" + Guid.NewGuid().ToString("n"));
    private readonly string _sourcePath = Path.Combine(Path.GetTempPath(), "sb-prune-src-" + Guid.NewGuid().ToString("n"));
    private readonly string _restorePath = Path.Combine(Path.GetTempPath(), "sb-prune-out-" + Guid.NewGuid().ToString("n"));

    public PruneEngineTests() => Directory.CreateDirectory(_sourcePath);

    [Fact]
    public async Task Dry_run_reports_what_would_happen_without_changing_anything()
    {
        var snapshots = await CreateChain(count: 4);

        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        var snapshotCountBefore = await db.Snapshots.CountAsync();
        var blobCountBefore = await db.Blobs.CountAsync();

        var pruneEngine = new PruneEngine(_repoPath, masterKey);
        var result = await pruneEngine.RunAsync(new RetentionPolicy(KeepLast: 1)); // dryRun defaults to true

        result.DryRun.Should().BeTrue();
        result.SnapshotsToDelete.Should().HaveCount(3);

        (await db.Snapshots.CountAsync()).Should().Be(snapshotCountBefore);
        (await db.Blobs.CountAsync()).Should().Be(blobCountBefore);
    }

    [Fact]
    public async Task After_pruning_every_remaining_snapshot_is_still_fully_restorable()
    {
        var snapshots = await CreateChain(count: 4);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        var pruneEngine = new PruneEngine(_repoPath, masterKey);
        var result = await pruneEngine.RunAsync(new RetentionPolicy(KeepLast: 1), dryRun: false);

        result.DryRun.Should().BeFalse();
        result.SnapshotsToDelete.Should().HaveCount(3);

        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));
        var remaining = await db.Snapshots.Select(s => s.SnapshotId).ToListAsync();
        remaining.Should().BeEquivalentTo([snapshots[^1]]);

        // The critical guarantee: what survives must still be byte-for-byte restorable.
        var restoreEngine = new RestoreEngine(_repoPath, masterKey);
        await restoreEngine.RestoreAsync(snapshots[^1], _restorePath);

        var expected = ExpectedContentFor(3);
        var restoredFile = Path.Combine(_restorePath, Path.GetFileName(_sourcePath), "changing.bin");
        File.ReadAllBytes(restoredFile).Should().Equal(expected);

        // Pruned snapshots are genuinely gone, not just hidden.
        var restoreOld = async () => await restoreEngine.RestoreAsync(snapshots[0], Path.Combine(_restorePath, "old"));
        await restoreOld.Should().ThrowAsync<InvalidOperationException>();

        // Every surviving pack must still be structurally valid.
        var verifyEngine = new VerifyEngine(_repoPath, masterKey);
        var issues = await verifyEngine.RunAsync(VerifyLevel.Full);
        issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Fully_dead_packs_are_deleted_and_partially_live_packs_are_repacked()
    {
        var snapshots = await CreateChain(count: 4);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));
        var packCountBefore = await db.Packs.CountAsync();

        var pruneEngine = new PruneEngine(_repoPath, masterKey);
        var result = await pruneEngine.RunAsync(new RetentionPolicy(KeepLast: 1), dryRun: false);

        (result.PacksToDelete.Count + result.PacksToRepack.Count).Should().BeGreaterThan(0,
            "with 3 of 4 snapshots pruned, at least some pack must be affected");

        // Everything the constant.bin content needed must have survived
        // (repacked, not deleted, since it's still referenced by the kept snapshot).
        var restoreEngine = new RestoreEngine(_repoPath, masterKey);
        await restoreEngine.RestoreAsync(snapshots[^1], _restorePath);
        var constantFile = Path.Combine(_restorePath, Path.GetFileName(_sourcePath), "constant.bin");
        File.Exists(constantFile).Should().BeTrue();
    }

    [Fact]
    public async Task An_interrupted_prune_leaves_kept_snapshots_restorable_and_a_second_run_finishes_cleanly()
    {
        var snapshots = await CreateChain(count: 12);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(1));

        var pruneEngine = new PruneEngine(_repoPath, masterKey);
        var act = async () => await pruneEngine.RunAsync(new RetentionPolicy(KeepLast: 1), dryRun: false, ct: cts.Token);

        try
        {
            await act();
        }
        catch (OperationCanceledException)
        {
            // Expected in the common case — but not guaranteed if the run
            // finished before the 1ms cancellation landed. Either way, the
            // invariants below must hold.
        }

        // Whatever the retention policy says must be kept must still be
        // fully restorable, regardless of how far the interrupted prune got.
        var restoreEngine = new RestoreEngine(_repoPath, masterKey);
        await restoreEngine.RestoreAsync(snapshots[^1], _restorePath);
        var restoredFile = Path.Combine(_restorePath, Path.GetFileName(_sourcePath), "changing.bin");
        File.ReadAllBytes(restoredFile).Should().Equal(ExpectedContentFor(11));

        // Every pack file that exists on disk must still be structurally
        // valid, whatever state the interrupted prune left behind. (Not
        // using RepositoryManager.RebuildIndexAsync here — that call is
        // destructively documented to discard Snapshots/SnapshotPaths, which
        // would defeat this test's own "still restorable" premise.)
        var packSubKey = SubKeys.Derive(masterKey, SubKeys.PackKeyInfo);
        var dataDir = Path.Combine(_repoPath, "data");
        if (Directory.Exists(dataDir))
        {
            foreach (var packFile in Directory.EnumerateFiles(dataDir, "*.pack", SearchOption.AllDirectories))
            {
                using var packStream = File.OpenRead(packFile);
                var reading = () => new PackReader(packStream, packSubKey);
                reading.Should().NotThrow($"'{packFile}' must be a complete, valid pack");
            }
        }

        // A clean second run must be able to finish whatever the interrupted
        // first run left undone (it may have completed nothing, some of it,
        // or all of it — any of those are a safe intermediate state).
        await pruneEngine.RunAsync(new RetentionPolicy(KeepLast: 1), dryRun: false);

        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));
        var remaining = await db.Snapshots.Select(s => s.SnapshotId).ToListAsync();
        remaining.Should().BeEquivalentTo([snapshots[^1]], "only the snapshot the policy keeps should remain once pruning fully completes");
    }

    /// <summary>Creates a chain of `count` backups: one file that never changes, one that changes every time.</summary>
    private async Task<List<string>> CreateChain(int count)
    {
        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        File.WriteAllBytes(Path.Combine(_sourcePath, "constant.bin"), RandomBytesFor(-1));

        var engine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);
        var snapshotIds = new List<string>();
        string? parent = null;

        for (var i = 0; i < count; i++)
        {
            File.WriteAllBytes(Path.Combine(_sourcePath, "changing.bin"), ExpectedContentFor(i));
            var snapshotId = await engine.RunAsync([_sourcePath], parent);
            snapshotIds.Add(snapshotId);
            parent = snapshotId;

            // Guarantee strictly increasing StartedAtUtc — these tiny backups
            // can otherwise complete within the same clock tick, and retention
            // must be able to tell "most recent" apart deterministically.
            await Task.Delay(15);
        }

        return snapshotIds;
    }

    private static byte[] ExpectedContentFor(int index)
    {
        var data = new byte[50_000];
        new Random(index).NextBytes(data);
        return data;
    }

    private static byte[] RandomBytesFor(int seed)
    {
        var data = new byte[50_000];
        new Random(seed).NextBytes(data);
        return data;
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _repoPath, _sourcePath, _restorePath })
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
