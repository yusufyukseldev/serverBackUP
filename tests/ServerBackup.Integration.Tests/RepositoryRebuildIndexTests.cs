using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServerBackup.Core.Crypto;
using ServerBackup.Core.Repository;
using ServerBackup.Data;
using ServerBackup.Engine.Repository;
using Xunit;

namespace ServerBackup.Integration.Tests;

/// <summary>
/// End-to-end proof of docs/format-spec.md's core promise: pack files are
/// self-describing, so losing catalog.db is recoverable, not a disaster.
/// </summary>
public sealed class RepositoryRebuildIndexTests : IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly string _repoPath =
        Path.Combine(Path.GetTempPath(), "sb-test-" + Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task RebuildIndex_after_catalog_loss_recovers_every_pack_and_blob()
    {
        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);
        var packSubKey = SubKeys.Derive(masterKey, SubKeys.PackKeyInfo);

        var expectedBlobIds = new List<string>();
        var expectedPackIds = new List<string>();

        // Simulate what a future BackupEngine (Faz 5) will do: write pack
        // files directly under data/<xx>/<packId>.pack.
        for (var packIndex = 0; packIndex < 3; packIndex++)
        {
            var packId = PackId.NewId();
            var packDir = Path.Combine(_repoPath, "data", packId[..2]);
            Directory.CreateDirectory(packDir);
            var packPath = Path.Combine(packDir, packId + ".pack");

            using (var stream = File.Create(packPath))
            {
                var writer = new PackWriter(stream, packSubKey);
                for (var blobIndex = 0; blobIndex < 4; blobIndex++)
                {
                    var payload = System.Text.Encoding.UTF8.GetBytes($"pack {packIndex} blob {blobIndex}");
                    var blobId = BlobId.Compute(SubKeys.Derive(masterKey, SubKeys.ChunkIdInfo), payload);
                    writer.AddBlob(blobId, blobIndex % 2 == 0 ? BlobKind.Data : BlobKind.Tree, payload);
                    expectedBlobIds.Add(Convert.ToHexStringLower(blobId));
                }

                writer.Close();
                writer.Dispose();
            }

            expectedPackIds.Add(packId);
        }

        // Simulate catalog loss.
        var catalogPath = Path.Combine(_repoPath, "catalog.db");
        File.Exists(catalogPath).Should().BeTrue("InitializeAsync must have created an empty catalog");
        File.Delete(catalogPath);

        var result = await RepositoryManager.RebuildIndexAsync(_repoPath, masterKey);

        result.PackCount.Should().Be(3);
        result.BlobCount.Should().Be(12);

        await using var db = CatalogDbContextFactory.Create(catalogPath);
        var packIdsInCatalog = await db.Packs.Select(p => p.PackId).ToListAsync();
        var blobIdsInCatalog = await db.Blobs.Select(b => b.BlobId).ToListAsync();

        packIdsInCatalog.Should().BeEquivalentTo(expectedPackIds);
        blobIdsInCatalog.Should().BeEquivalentTo(expectedBlobIds);

        // And the recovered catalog is actually usable, not just populated:
        // every blob it points to must still decrypt correctly.
        foreach (var pack in await db.Packs.Include(p => p.Blobs).ToListAsync())
        {
            var packPath = Path.Combine(_repoPath, PackId.RelativePath(pack.PackId));
            await using var packStream = File.OpenRead(packPath);
            var reader = new PackReader(packStream, packSubKey);

            for (var i = 0; i < reader.Entries.Count; i++)
            {
                var plaintext = reader.ReadBlob(i);
                plaintext.Should().NotBeEmpty();
            }
        }
    }

    [Fact]
    public async Task RebuildIndex_on_a_repository_with_no_packs_yields_an_empty_catalog()
    {
        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);

        var result = await RepositoryManager.RebuildIndexAsync(_repoPath, masterKey);

        result.PackCount.Should().Be(0);
        result.BlobCount.Should().Be(0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_repoPath))
        {
            Directory.Delete(_repoPath, recursive: true);
        }
    }
}
