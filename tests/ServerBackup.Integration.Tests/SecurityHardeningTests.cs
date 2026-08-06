using System.Security.AccessControl;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServerBackup.Data;
using ServerBackup.Engine.Backup;
using ServerBackup.Engine.Prune;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Retention;
using ServerBackup.Engine.Scanning;
using Xunit;

namespace ServerBackup.Integration.Tests;

public sealed class SecurityHardeningTests : IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly string _repoPath = Path.Combine(Path.GetTempPath(), "sb-sec-repo-" + Guid.NewGuid().ToString("n"));
    private readonly string _sourcePath = Path.Combine(Path.GetTempPath(), "sb-sec-src-" + Guid.NewGuid().ToString("n"));

    public SecurityHardeningTests() => Directory.CreateDirectory(_sourcePath);

    [Fact]
    public async Task Repository_directory_gets_its_own_protected_ACL_on_init()
    {
        await RepositoryManager.InitializeAsync(_repoPath, Password);

        var security = new DirectoryInfo(_repoPath).GetAccessControl();

        security.AreAccessRulesProtected.Should().BeTrue("inherited rules from the parent folder must not leak in");
    }

    [Fact]
    public async Task Immutability_window_protects_recent_snapshots_even_when_retention_says_delete_them()
    {
        await RepositoryManager.InitializeAsync(_repoPath, Password, immutabilityWindowDays: 30);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        File.WriteAllBytes(Path.Combine(_sourcePath, "a.txt"), "v1"u8.ToArray());
        var engine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);
        var snapshotId = await engine.RunAsync([_sourcePath]);

        // A policy that would normally delete everything (keep nothing).
        var pruneEngine = new PruneEngine(_repoPath, masterKey);
        var result = await pruneEngine.RunAsync(new RetentionPolicy(KeepLast: 0), dryRun: false);

        result.SnapshotsToDelete.Should().BeEmpty("the snapshot is inside the 30-day immutability window");

        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));
        (await db.Snapshots.AnyAsync(s => s.SnapshotId == snapshotId)).Should().BeTrue();
    }

    [Fact]
    public async Task Append_only_repositories_never_delete_any_snapshot()
    {
        await RepositoryManager.InitializeAsync(_repoPath, Password, appendOnly: true);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        var engine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);
        var ids = new List<string>();
        string? parent = null;
        for (var i = 0; i < 3; i++)
        {
            File.WriteAllBytes(Path.Combine(_sourcePath, "f.bin"), Guid.NewGuid().ToByteArray());
            var id = await engine.RunAsync([_sourcePath], parent);
            ids.Add(id);
            parent = id;
        }

        var pruneEngine = new PruneEngine(_repoPath, masterKey);
        var result = await pruneEngine.RunAsync(new RetentionPolicy(KeepLast: 1), dryRun: false);

        result.SnapshotsToDelete.Should().BeEmpty("append-only must override any retention policy");

        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));
        (await db.Snapshots.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task An_abort_on_detection_anomaly_policy_stops_the_backup_before_committing_a_snapshot()
    {
        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        // A baseline of enough files to clear MinimumParentFileCount.
        for (var i = 0; i < 25; i++)
        {
            File.WriteAllBytes(Path.Combine(_sourcePath, $"file{i}.txt"), System.Text.Encoding.UTF8.GetBytes($"content {i}"));
        }

        var engine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);
        var baseline = await engine.RunAsync([_sourcePath]);

        // Simulate ransomware: every file gets replaced with different content.
        for (var i = 0; i < 25; i++)
        {
            File.WriteAllBytes(Path.Combine(_sourcePath, $"file{i}.txt"), Guid.NewGuid().ToByteArray());
        }

        var protectedEngine = new BackupEngine(
            new LocalSourceProvider(), _repoPath, masterKey,
            anomalyPolicy: new AnomalyPolicy(ChangedOrDeletedRatioThreshold: 0.5, AbortOnDetection: true));

        var act = async () => await protectedEngine.RunAsync([_sourcePath], baseline);

        await act.Should().ThrowAsync<AnomalyDetectedException>();

        // No new snapshot was committed — only the baseline exists.
        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));
        (await db.Snapshots.CountAsync()).Should().Be(1);

        // And it's audited.
        var auditEntries = await db.AuditLogs.Where(a => a.Action == "anomaly-abort").ToListAsync();
        auditEntries.Should().ContainSingle();
    }

    [Fact]
    public async Task An_anomaly_policy_in_warn_mode_still_commits_the_snapshot_but_logs_it()
    {
        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        for (var i = 0; i < 25; i++)
        {
            File.WriteAllBytes(Path.Combine(_sourcePath, $"file{i}.txt"), System.Text.Encoding.UTF8.GetBytes($"content {i}"));
        }

        var engine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);
        var baseline = await engine.RunAsync([_sourcePath]);

        for (var i = 0; i < 25; i++)
        {
            File.WriteAllBytes(Path.Combine(_sourcePath, $"file{i}.txt"), Guid.NewGuid().ToByteArray());
        }

        var warnEngine = new BackupEngine(
            new LocalSourceProvider(), _repoPath, masterKey,
            anomalyPolicy: new AnomalyPolicy(ChangedOrDeletedRatioThreshold: 0.5, AbortOnDetection: false));

        var secondSnapshot = await warnEngine.RunAsync([_sourcePath], baseline);
        secondSnapshot.Should().NotBeNullOrEmpty();

        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));
        (await db.Snapshots.CountAsync()).Should().Be(2);
        (await db.AuditLogs.CountAsync(a => a.Action == "anomaly-warning")).Should().Be(1);
    }

    [Fact]
    public async Task Prune_and_restore_are_recorded_in_the_audit_log()
    {
        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);
        File.WriteAllBytes(Path.Combine(_sourcePath, "a.txt"), "hi"u8.ToArray());

        var engine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);
        var snapshotId = await engine.RunAsync([_sourcePath]);

        var restoreEngine = new ServerBackup.Engine.Restore.RestoreEngine(_repoPath, masterKey);
        var targetPath = Path.Combine(Path.GetTempPath(), "sb-sec-out-" + Guid.NewGuid().ToString("n"));
        try
        {
            await restoreEngine.RestoreAsync(snapshotId, targetPath);

            await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));
            (await db.AuditLogs.CountAsync(a => a.Action == "restore")).Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath, recursive: true);
            }
        }
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _repoPath, _sourcePath })
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
