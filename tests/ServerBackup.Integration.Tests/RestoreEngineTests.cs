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
