using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using ServerBackup.Engine.Backup;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Restore;
using ServerBackup.Engine.Scanning;
using ServerBackup.Engine.Verify;
using Xunit;

namespace ServerBackup.Integration.Tests;

public sealed class RestoreEngineTests : IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly string _repoPath = Path.Combine(Path.GetTempPath(), "sb-restore-repo-" + Guid.NewGuid().ToString("n"));
    private readonly string _sourcePath = Path.Combine(Path.GetTempPath(), "sb-restore-src-" + Guid.NewGuid().ToString("n"));
    private readonly string _restorePath = Path.Combine(Path.GetTempPath(), "sb-restore-out-" + Guid.NewGuid().ToString("n"));

    public RestoreEngineTests() => Directory.CreateDirectory(_sourcePath);

    [Fact]
    public async Task Backup_then_restore_reproduces_a_complex_tree_byte_for_byte_with_metadata()
    {
        BuildComplexSourceTree(_sourcePath);

        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        var backupEngine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);
        var snapshotId = await backupEngine.RunAsync([_sourcePath]);

        var restoreEngine = new RestoreEngine(_repoPath, masterKey);
        var restoredRoot = Path.Combine(_restorePath, Path.GetFileName(_sourcePath));
        await restoreEngine.RestoreAsync(snapshotId, _restorePath);

        AssertTreesAreByteIdentical(_sourcePath, restoredRoot);
    }

    [Fact]
    public async Task Verify_full_detects_a_single_flipped_byte_in_a_pack_and_restore_fails_meaningfully()
    {
        BuildComplexSourceTree(_sourcePath);

        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        var backupEngine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);
        var snapshotId = await backupEngine.RunAsync([_sourcePath]);

        var packFile = Directory.EnumerateFiles(Path.Combine(_repoPath, "data"), "*.pack", SearchOption.AllDirectories).Single();
        var bytes = await File.ReadAllBytesAsync(packFile);
        bytes[40] ^= 0xFF; // inside the ciphertext region, well past the 16-byte salt
        await File.WriteAllBytesAsync(packFile, bytes);

        var verifyEngine = new VerifyEngine(_repoPath, masterKey);
        var issues = await verifyEngine.RunAsync(VerifyLevel.Full);

        issues.Should().Contain(i => i.Category == "full");

        var restoreEngine = new RestoreEngine(_repoPath, masterKey);
        var act = async () => await restoreEngine.RestoreAsync(snapshotId, _restorePath);

        // A meaningful, specific failure — not a silent corruption or a generic crash.
        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task A_truncated_pack_from_a_killed_process_does_not_break_rebuild_index_or_future_backups()
    {
        Directory.CreateDirectory(_sourcePath);
        File.WriteAllText(Path.Combine(_sourcePath, "a.txt"), "hello");

        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        // Simulate what a hard-killed backup process leaves behind: a pack file
        // that was opened and partially written but never closed (no valid
        // header/footer) — see PackFileSet.DisposeAsync's discard-on-abort logic,
        // which this test exercises the *absence* of (a real crash bypasses it entirely).
        var orphanDir = Path.Combine(_repoPath, "data", "ab");
        Directory.CreateDirectory(orphanDir);
        var orphanPack = Path.Combine(orphanDir, "ab" + new string('0', 30) + ".pack");
        await File.WriteAllBytesAsync(orphanPack, RandomNumberGenerator.GetBytes(1000));

        var result = await RepositoryManager.RebuildIndexAsync(_repoPath, masterKey);
        result.SkippedPacks.Should().ContainSingle().Which.Should().Be(orphanPack);
        result.PackCount.Should().Be(0);

        var backupEngine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);
        var snapshotId = await backupEngine.RunAsync([_sourcePath]);
        snapshotId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Restore_to_an_alternate_location_leaves_the_original_source_untouched()
    {
        BuildComplexSourceTree(_sourcePath);
        var originalHash = HashDirectory(_sourcePath);

        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        var backupEngine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);
        var snapshotId = await backupEngine.RunAsync([_sourcePath]);

        var restoreEngine = new RestoreEngine(_repoPath, masterKey);
        await restoreEngine.RestoreAsync(snapshotId, _restorePath);

        HashDirectory(_sourcePath).Should().Equal(originalHash, "restoring elsewhere must not touch the original source");
        Directory.Exists(Path.Combine(_restorePath, Path.GetFileName(_sourcePath))).Should().BeTrue();
    }

    [Fact]
    public async Task Selective_restore_of_a_single_file_restores_only_that_file()
    {
        Directory.CreateDirectory(Path.Combine(_sourcePath, "sub"));
        File.WriteAllText(Path.Combine(_sourcePath, "keep.txt"), "keep me");
        File.WriteAllText(Path.Combine(_sourcePath, "sub", "also-there.txt"), "also there");

        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        var backupEngine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);
        var snapshotId = await backupEngine.RunAsync([_sourcePath]);

        var sourceDirName = Path.GetFileName(_sourcePath);
        var restoreEngine = new RestoreEngine(_repoPath, masterKey);
        await restoreEngine.RestoreAsync(snapshotId, _restorePath, selectedRelativePaths: [$"{sourceDirName}/keep.txt"]);

        File.Exists(Path.Combine(_restorePath, sourceDirName, "keep.txt")).Should().BeTrue();
        File.Exists(Path.Combine(_restorePath, sourceDirName, "sub", "also-there.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task Restore_reports_progress_and_ends_with_every_planned_file_and_byte_completed()
    {
        BuildComplexSourceTree(_sourcePath);

        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        var backupEngine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);
        var snapshotId = await backupEngine.RunAsync([_sourcePath]);

        var reports = new List<RestoreProgress>();
        var restoreEngine = new RestoreEngine(_repoPath, masterKey, new SyncProgress<RestoreProgress>(reports.Add));
        await restoreEngine.RestoreAsync(snapshotId, _restorePath);

        var sourceFiles = Directory.GetFiles(_sourcePath, "*", SearchOption.AllDirectories);
        var expectedBytes = sourceFiles.Sum(f => new FileInfo(f).Length);

        reports.Should().NotBeEmpty();
        reports.Should().AllSatisfy(r =>
        {
            r.FilesPlanned.Should().Be(sourceFiles.Length);
            r.BytesPlanned.Should().Be(expectedBytes);
        });

        var final = reports[^1];
        final.FilesCompleted.Should().Be(sourceFiles.Length);
        final.BytesCompleted.Should().Be(expectedBytes);

        reports.Select(r => r.FilesCompleted).Should().BeInAscendingOrder();
        TempFilesUnder(_restorePath).Should().BeEmpty("a successful restore leaves no scratch files behind");
    }

    [Fact]
    public async Task A_cancelled_restore_never_leaves_a_wrong_file_at_its_final_name()
    {
        BuildComplexSourceTree(_sourcePath);

        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        var backupEngine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);
        var snapshotId = await backupEngine.RunAsync([_sourcePath]);

        // Cancelling from the progress callback pins the cancellation to a known
        // point — right after the first file with actual content was renamed into
        // place — so the half-restored state under test is the same on every run.
        using var cts = new CancellationTokenSource();
        var cancellingEngine = new RestoreEngine(
            _repoPath, masterKey, new SyncProgress<RestoreProgress>(p =>
            {
                if (p.BytesCompleted > 0)
                {
                    cts.Cancel();
                }
            }));

        var act = async () => await cancellingEngine.RestoreAsync(snapshotId, _restorePath, ct: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        var restoredRoot = Path.Combine(_restorePath, Path.GetFileName(_sourcePath));
        var finalNamed = Directory.GetFiles(_restorePath, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(RestoreEngine.TempSuffix, StringComparison.Ordinal))
            .ToList();

        finalNamed.Should().NotBeEmpty("the file that did complete should have been published");
        finalNamed.Count.Should().BeLessThan(
            Directory.GetFiles(_sourcePath, "*", SearchOption.AllDirectories).Length,
            "the restore was cancelled before it could finish");

        // The point of the whole fix: anything present under a real name is
        // fully correct, never zero-filled or half-written.
        foreach (var restored in finalNamed)
        {
            var relative = Path.GetRelativePath(restoredRoot, restored);
            var expected = Path.Combine(_sourcePath, relative);

            File.Exists(expected).Should().BeTrue($"'{relative}' should not exist unless it came from the snapshot");
            File.ReadAllBytes(restored).Should().Equal(
                File.ReadAllBytes(expected), $"'{relative}' is present at its final name, so it must be complete");
        }

        TempFilesUnder(_restorePath).Should().NotBeEmpty("the unfinished files should still be sitting under scratch names");

        // And a retry over that wreckage produces a correct tree.
        var retryEngine = new RestoreEngine(_repoPath, masterKey);
        await retryEngine.RestoreAsync(snapshotId, _restorePath);

        AssertTreesAreByteIdentical(_sourcePath, restoredRoot);
        TempFilesUnder(_restorePath).Should().BeEmpty();
    }

    [Fact]
    public async Task A_stale_temp_file_from_a_crashed_restore_does_not_corrupt_the_next_one()
    {
        var payload = RandomNumberGenerator.GetBytes(300_000);
        File.WriteAllBytes(Path.Combine(_sourcePath, "veri.bin"), payload);

        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        var backupEngine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);
        var snapshotId = await backupEngine.RunAsync([_sourcePath]);

        // Pre-seed the leftovers of a killed restore: a longer scratch file full
        // of bytes that belong to nothing, sitting exactly where this run will write.
        var restoredRoot = Path.Combine(_restorePath, Path.GetFileName(_sourcePath));
        Directory.CreateDirectory(restoredRoot);
        var stalePath = Path.Combine(restoredRoot, "veri.bin" + RestoreEngine.TempSuffix);
        await File.WriteAllBytesAsync(stalePath, RandomNumberGenerator.GetBytes(payload.Length * 2));

        var restoreEngine = new RestoreEngine(_repoPath, masterKey);
        await restoreEngine.RestoreAsync(snapshotId, _restorePath);

        var restoredFile = Path.Combine(restoredRoot, "veri.bin");
        new FileInfo(restoredFile).Length.Should().Be(payload.Length, "the stale file's extra bytes must not survive");
        File.ReadAllBytes(restoredFile).Should().Equal(payload);
        TempFilesUnder(_restorePath).Should().BeEmpty();
    }

    private static List<string> TempFilesUnder(string root) =>
        Directory.Exists(root)
            ? Directory.GetFiles(root, "*" + RestoreEngine.TempSuffix, SearchOption.AllDirectories).ToList()
            : [];

    /// <summary>
    /// <see cref="Progress{T}"/> posts to the synchronization context, which
    /// makes "how many reports arrived by now" untestable; these tests need the
    /// callback to run inline on the restore's own thread.
    /// </summary>
    private sealed class SyncProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }

    private static void BuildComplexSourceTree(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "a", "b", "c")); // deep path, empty leaf
        Directory.CreateDirectory(Path.Combine(root, "empty-dir"));
        File.WriteAllBytes(Path.Combine(root, "a", "b", "rapor-ü-ş-ç.xlsx"), Encoding.UTF8.GetBytes("içerik ve daha fazlası")); // unicode name
        File.WriteAllBytes(Path.Combine(root, "a", "empty.dat"), []); // 0-byte file
        File.WriteAllBytes(Path.Combine(root, "a", "sparse-ish.bin"), BuildSparseish(2_000_000)); // long runs of zero bytes
        File.WriteAllBytes(Path.Combine(root, "top-level.bin"), RandomNumberGenerator.GetBytes(500_000));
    }

    private static byte[] BuildSparseish(int length)
    {
        var data = new byte[length];
        var random = RandomNumberGenerator.GetBytes(length / 10);
        random.CopyTo(data, length / 4); // one small random region, the rest stays zero
        return data;
    }

    private static void AssertTreesAreByteIdentical(string expectedRoot, string actualRoot)
    {
        var expectedFiles = Directory.GetFiles(expectedRoot, "*", SearchOption.AllDirectories);
        foreach (var expectedFile in expectedFiles)
        {
            var relative = Path.GetRelativePath(expectedRoot, expectedFile);
            var actualFile = Path.Combine(actualRoot, relative);

            File.Exists(actualFile).Should().BeTrue($"'{relative}' should have been restored");

            var expectedBytes = File.ReadAllBytes(expectedFile);
            var actualBytes = File.ReadAllBytes(actualFile);
            actualBytes.Should().Equal(expectedBytes, $"content of '{relative}' must match byte-for-byte");

            var expectedInfo = new FileInfo(expectedFile);
            var actualInfo = new FileInfo(actualFile);
            actualInfo.LastWriteTimeUtc.Should().Be(expectedInfo.LastWriteTimeUtc, $"mtime of '{relative}' must be preserved");

            var sddl = actualInfo.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
            var act = () => new RawSecurityDescriptor(sddl);
            act.Should().NotThrow($"restored ACL for '{relative}' must be valid SDDL");
        }

        var expectedDirCount = Directory.GetDirectories(expectedRoot, "*", SearchOption.AllDirectories).Length;
        var actualDirCount = Directory.GetDirectories(actualRoot, "*", SearchOption.AllDirectories).Length;
        actualDirCount.Should().Be(expectedDirCount, "every directory, including empty ones, must be restored");
    }

    private static List<(string Relative, string HashHex)> HashDirectory(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => (Path.GetRelativePath(root, f), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f)))))
            .OrderBy(x => x.Item1, StringComparer.Ordinal)
            .ToList();

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
