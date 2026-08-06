using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ServerBackup.Core.Crypto;
using ServerBackup.Core.Repository;
using ServerBackup.Data;

namespace ServerBackup.Engine.Verify;

/// <summary>
/// Three escalating levels of repository integrity checking — see plan
/// Faz 6. Each level subsumes the ones below it.
/// </summary>
public sealed class VerifyEngine
{
    private readonly string _repoPath;
    private readonly byte[] _masterKey;

    public VerifyEngine(string repoPath, byte[] masterKey)
    {
        _repoPath = repoPath;
        _masterKey = masterKey;
    }

    public async Task<IReadOnlyList<VerifyIssue>> RunAsync(VerifyLevel level, CancellationToken ct = default)
    {
        var issues = new List<VerifyIssue>();
        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));

        var packs = await db.Packs.AsNoTracking().ToListAsync(ct);
        var packIds = packs.Select(p => p.PackId).ToHashSet();

        var missingPacks = new HashSet<string>();
        foreach (var pack in packs)
        {
            var path = Path.Combine(_repoPath, PackId.RelativePath(pack.PackId));
            if (!File.Exists(path))
            {
                missingPacks.Add(pack.PackId);
                issues.Add(new VerifyIssue("index", $"Pack '{pack.PackId}' katalogda var ama diskte yok: {path}"));
            }
        }

        var blobs = await db.Blobs.AsNoTracking().ToListAsync(ct);
        foreach (var blob in blobs)
        {
            if (!packIds.Contains(blob.PackId))
            {
                issues.Add(new VerifyIssue("index", $"Blob '{blob.BlobId}' bilinmeyen pack '{blob.PackId}' referans ediyor."));
            }
        }

        var blobIds = blobs.Select(b => b.BlobId).ToHashSet();
        var snapshots = await db.Snapshots.AsNoTracking().ToListAsync(ct);
        foreach (var snapshot in snapshots)
        {
            if (!blobIds.Contains(snapshot.RootTreeBlobId))
            {
                issues.Add(new VerifyIssue(
                    "index", $"Snapshot '{snapshot.SnapshotId}' bilinmeyen kök tree blob'u referans ediyor: {snapshot.RootTreeBlobId}"));
            }
        }

        if (level == VerifyLevel.Index)
        {
            return issues;
        }

        foreach (var pack in packs)
        {
            ct.ThrowIfCancellationRequested();
            if (missingPacks.Contains(pack.PackId))
            {
                continue;
            }

            var path = Path.Combine(_repoPath, PackId.RelativePath(pack.PackId));
            var actualSha256 = SHA256.HashData(await File.ReadAllBytesAsync(path, ct));
            if (!actualSha256.AsSpan().SequenceEqual(pack.Sha256))
            {
                issues.Add(new VerifyIssue("packs", $"Pack '{pack.PackId}' SHA-256 uyuşmuyor (dosya bozulmuş olabilir)."));
            }
        }

        if (level == VerifyLevel.Packs)
        {
            return issues;
        }

        var packSubKey = SubKeys.Derive(_masterKey, SubKeys.PackKeyInfo);
        var idKey = SubKeys.Derive(_masterKey, SubKeys.ChunkIdInfo);

        foreach (var pack in packs)
        {
            ct.ThrowIfCancellationRequested();
            if (missingPacks.Contains(pack.PackId))
            {
                continue;
            }

            var path = Path.Combine(_repoPath, PackId.RelativePath(pack.PackId));

            PackReader reader;
            try
            {
                using var stream = File.OpenRead(path);
                reader = new PackReader(stream, packSubKey);
                VerifyPackBlobs(pack.PackId, reader, idKey, issues, ct);
            }
            catch (CryptographicException)
            {
                issues.Add(new VerifyIssue("full", $"Pack '{pack.PackId}' header'ı açılamadı (kimlik doğrulama başarısız — bozulmuş olabilir)."));
            }
        }

        return issues;
    }

    private static void VerifyPackBlobs(string packId, PackReader reader, byte[] idKey, List<VerifyIssue> issues, CancellationToken ct)
    {
        for (var i = 0; i < reader.Entries.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = reader.Entries[i];
            var storedId = Convert.ToHexStringLower(entry.BlobId);

            byte[] plaintext;
            try
            {
                plaintext = reader.ReadBlob(i);
            }
            catch (CryptographicException)
            {
                issues.Add(new VerifyIssue("full", $"Pack '{packId}' içindeki blob '{storedId}' çözülemedi (kimlik doğrulama başarısız)."));
                continue;
            }

            var recomputedId = Convert.ToHexStringLower(BlobId.Compute(idKey, plaintext));
            if (recomputedId != storedId)
            {
                issues.Add(new VerifyIssue(
                    "full", $"Pack '{packId}' blob {i}: içerik kimliği uyuşmuyor (beklenen {storedId}, hesaplanan {recomputedId})."));
            }
        }
    }
}
