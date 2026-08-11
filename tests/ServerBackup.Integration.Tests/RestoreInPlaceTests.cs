using System.Text;
using FluentAssertions;
using ServerBackup.Engine.Backup;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Restore;
using ServerBackup.Engine.Scanning;
using Xunit;

namespace ServerBackup.Integration.Tests;

/// <summary>
/// "Revert to this backup" writes over the paths the snapshot was taken from,
/// so the mapping from a snapshot root back to its original path is the part
/// that must not drift: getting it wrong either restores nothing or restores
/// over the wrong directory.
/// </summary>
public sealed class RestoreInPlaceTests : IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly string _repoPath = Path.Combine(Path.GetTempPath(), "sb-inplace-repo-" + Guid.NewGuid().ToString("n"));
    private readonly string _sourcePath = Path.Combine(Path.GetTempPath(), "sb-inplace-src-" + Guid.NewGuid().ToString("n"));

    public RestoreInPlaceTests() => Directory.CreateDirectory(Path.Combine(_sourcePath, "alt"));

    [Fact]
    public async Task Reverting_restores_deleted_and_modified_files_over_the_original_source()
    {
        var deleted = Path.Combine(_sourcePath, "alt", "silinen.txt");
        var edited = Path.Combine(_sourcePath, "degisen.txt");
        File.WriteAllText(deleted, "orijinal içerik", Encoding.UTF8);
        File.WriteAllText(edited, "orijinal içerik", Encoding.UTF8);

        var (masterKey, snapshotId) = await BackUpAsync();

        File.Delete(deleted);
        File.WriteAllText(edited, "sonradan bozuldu", Encoding.UTF8);

        var written = await new RestoreEngine(_repoPath, masterKey).RestoreInPlaceAsync(snapshotId);

        written.Should().ContainSingle().Which.Should().Be(_sourcePath);
        File.ReadAllText(deleted, Encoding.UTF8).Should().Be("orijinal içerik");
        File.ReadAllText(edited, Encoding.UTF8).Should().Be("orijinal içerik");
    }

    [Fact]
    public async Task Reverting_writes_into_the_source_itself_not_a_nested_copy_of_it()
    {
        File.WriteAllText(Path.Combine(_sourcePath, "a.txt"), "içerik");

        var (masterKey, snapshotId) = await BackUpAsync();
        await new RestoreEngine(_repoPath, masterKey).RestoreInPlaceAsync(snapshotId);

        // The extract-to-a-directory path nests the root under the target; the
        // in-place path must not, or every revert would bury a fresh copy.
        Directory.Exists(Path.Combine(_sourcePath, Path.GetFileName(_sourcePath)))
            .Should().BeFalse("reverting must not create a copy of the source inside itself");
    }

    /// <summary>
    /// Windows will not let FileMode.Create truncate a read-only, hidden or
    /// system file, and a restore reapplies exactly those attributes — so
    /// without stripping them first, one such file aborts the whole run.
    /// </summary>
    [Theory]
    [InlineData(FileAttributes.ReadOnly)]
    [InlineData(FileAttributes.Hidden)]
    [InlineData(FileAttributes.Hidden | FileAttributes.System)]
    public async Task Reverting_overwrites_a_file_whose_attributes_would_block_it(FileAttributes attributes)
    {
        var path = Path.Combine(_sourcePath, "ozel.txt");
        File.WriteAllText(path, "orijinal", Encoding.UTF8);
        File.SetAttributes(path, attributes);

        var (masterKey, snapshotId) = await BackUpAsync();

        File.SetAttributes(path, FileAttributes.Normal);
        File.WriteAllText(path, "bozuldu", Encoding.UTF8);
        File.SetAttributes(path, attributes);

        await new RestoreEngine(_repoPath, masterKey).RestoreInPlaceAsync(snapshotId);

        File.ReadAllText(path, Encoding.UTF8).Should().Be("orijinal");
        File.GetAttributes(path).Should().HaveFlag(attributes, "the snapshot's own attributes must come back");
    }

    [Theory]
    [InlineData(@"C:\", "C")]
    [InlineData(@"C:\Veri", "Veri")]
    [InlineData(@"C:\Veri\", "Veri")]
    [InlineData(@"D:\bir\iki\Uc", "Uc")]
    public void Root_segment_matches_the_name_backup_stores_the_root_under(string sourcePath, string expected) =>
        RestoreEngine.RootSegmentOf(sourcePath).Should().Be(expected);

    [Fact]
    public void Two_sources_sharing_a_leaf_name_are_refused_rather_than_guessed()
    {
        var act = () => RestoreEngine.MapRootNamesToSourcePaths([@"C:\bir\Veri", @"D:\iki\Veri"]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ambiguous*", "restoring to the wrong volume is worse than refusing");
    }

    private async Task<(byte[] MasterKey, string SnapshotId)> BackUpAsync()
    {
        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        var snapshotId = await new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey).RunAsync([_sourcePath]);
        return (masterKey, snapshotId);
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _repoPath, _sourcePath })
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(dir, recursive: true);
        }
    }
}
