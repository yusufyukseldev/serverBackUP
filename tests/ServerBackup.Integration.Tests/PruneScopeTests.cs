using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServerBackup.Data;
using ServerBackup.Engine.Backup;
using ServerBackup.Engine.Prune;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Restore;
using ServerBackup.Engine.Retention;
using ServerBackup.Engine.Scanning;
using Xunit;

namespace ServerBackup.Integration.Tests;

/// <summary>
/// One repository normally holds several plans. The scheduler prunes with the
/// finished plan's own retention policy, so if prune judged the whole
/// repository a "keep last 1" plan for a scratch folder would silently delete
/// the year of monthly archives belonging to a different plan.
/// </summary>
public sealed class PruneScopeTests : IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly string _repoPath = Path.Combine(Path.GetTempPath(), "sb-scope-repo-" + Guid.NewGuid().ToString("n"));
    private readonly string _archiveSource = Path.Combine(Path.GetTempPath(), "sb-scope-arsiv-" + Guid.NewGuid().ToString("n"));
    private readonly string _scratchSource = Path.Combine(Path.GetTempPath(), "sb-scope-gecici-" + Guid.NewGuid().ToString("n"));
    private readonly string _restorePath = Path.Combine(Path.GetTempPath(), "sb-scope-out-" + Guid.NewGuid().ToString("n"));

    public PruneScopeTests()
    {
        Directory.CreateDirectory(_archiveSource);
        Directory.CreateDirectory(_scratchSource);
    }

    [Fact]
    public async Task Pruning_for_one_plan_leaves_another_plans_snapshots_alone()
    {
        var masterKey = await SetUpRepositoryAsync();
        var engine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);

        var archiveIds = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            File.WriteAllText(Path.Combine(_archiveSource, "arsiv.txt"), $"sürüm {i}");
            archiveIds.Add(await engine.RunAsync([_archiveSource], planId: "plan-arsiv"));
            await Task.Delay(15);
        }

        var scratchIds = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            File.WriteAllText(Path.Combine(_scratchSource, "gecici.txt"), $"sürüm {i}");
            scratchIds.Add(await engine.RunAsync([_scratchSource], planId: "plan-gecici"));
            await Task.Delay(15);
        }

        var result = await new PruneEngine(_repoPath, masterKey)
            .RunAsync(new RetentionPolicy(KeepLast: 1), dryRun: false, planId: "plan-gecici");

        result.SnapshotsToDelete.Should().BeEquivalentTo(scratchIds.Take(2));

        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));
        var remaining = await db.Snapshots.Select(s => s.SnapshotId).ToListAsync();
        remaining.Should().BeEquivalentTo([.. archiveIds, scratchIds[^1]]);
    }

    [Fact]
    public async Task An_out_of_scope_snapshot_is_still_restorable_after_a_scoped_prune()
    {
        var masterKey = await SetUpRepositoryAsync();
        var engine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);

        File.WriteAllText(Path.Combine(_archiveSource, "arsiv.txt"), "korunmalı");
        var archiveId = await engine.RunAsync([_archiveSource], planId: "plan-arsiv");
        await Task.Delay(15);

        for (var i = 0; i < 2; i++)
        {
            File.WriteAllText(Path.Combine(_scratchSource, "gecici.txt"), $"sürüm {i}");
            await engine.RunAsync([_scratchSource], planId: "plan-gecici");
            await Task.Delay(15);
        }

        await new PruneEngine(_repoPath, masterKey)
            .RunAsync(new RetentionPolicy(KeepLast: 1), dryRun: false, planId: "plan-gecici");

        // Keeping the row but garbage-collecting its blobs would be the same
        // data loss with extra steps, so the content has to come back too.
        await new RestoreEngine(_repoPath, masterKey).RestoreAsync(archiveId, _restorePath);

        var restored = Path.Combine(_restorePath, Path.GetFileName(_archiveSource), "arsiv.txt");
        File.ReadAllText(restored).Should().Be("korunmalı");
    }

    [Fact]
    public async Task Without_a_plan_id_the_whole_repository_is_still_evaluated()
    {
        var masterKey = await SetUpRepositoryAsync();
        var engine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);

        File.WriteAllText(Path.Combine(_archiveSource, "arsiv.txt"), "a");
        await engine.RunAsync([_archiveSource], planId: "plan-arsiv");
        await Task.Delay(15);
        File.WriteAllText(Path.Combine(_scratchSource, "gecici.txt"), "b");
        var newest = await engine.RunAsync([_scratchSource], planId: "plan-gecici");

        var result = await new PruneEngine(_repoPath, masterKey)
            .RunAsync(new RetentionPolicy(KeepLast: 1));

        result.SnapshotsToDelete.Should().ContainSingle(
            "an operator running prune by hand without --plan is asking for the whole repository");
        result.SnapshotsToDelete.Should().NotContain(newest);
    }

    private async Task<byte[]> SetUpRepositoryAsync()
    {
        await RepositoryManager.InitializeAsync(_repoPath, Password);
        return await RepositoryKeyStore.UnlockAsync(_repoPath, Password);
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _repoPath, _archiveSource, _scratchSource, _restorePath })
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
