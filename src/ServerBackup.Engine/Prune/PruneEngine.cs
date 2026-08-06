using Microsoft.EntityFrameworkCore;
using ServerBackup.Core.Crypto;
using ServerBackup.Core.Repository;
using ServerBackup.Core.Trees;
using ServerBackup.Data;
using ServerBackup.Engine.Audit;
using ServerBackup.Engine.Backup;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Retention;

namespace ServerBackup.Engine.Prune;

/// <summary>
/// Mark-and-sweep garbage collection: walks every KEPT snapshot's tree to
/// find the live blob set, then removes packs that are entirely dead and
/// repacks packs that are mostly dead — see plan Faz 8. Every destructive
/// step commits the new/updated state to the catalog before deleting
/// anything on disk (write-then-delete, per CLAUDE.md rule 5): a crash at
/// any point leaves the repository either unchanged or already-consistent,
/// never half-updated.
/// </summary>
public sealed class PruneEngine
{
    private const double RepackLivenessThreshold = 0.70;

    private readonly string _repoPath;
    private readonly byte[] _masterKey;

    public PruneEngine(string repoPath, byte[] masterKey)
    {
        _repoPath = repoPath;
        _masterKey = masterKey;
    }

    /// <summary>dryRun defaults to true — pruning is destructive and must be an explicit opt-in.</summary>
    public async Task<PruneResult> RunAsync(RetentionPolicy policy, bool dryRun = true, CancellationToken ct = default)
    {
        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));

        var allSnapshots = await db.Snapshots.AsNoTracking().ToListAsync(ct);
        var summaries = allSnapshots
            .Select(s => new SnapshotSummary(s.SnapshotId, s.StartedAtUtc, ParseTags(s.Tags)))
            .ToList();
        var keepIds = RetentionEvaluator.SelectSnapshotsToKeep(summaries, policy, DateTimeOffset.UtcNow);

        // Immutability window (plan Faz 11): a snapshot younger than this can
        // never be deleted, regardless of what the retention policy says —
        // no override, not even from the panel. This is what makes prune
        // ransomware-resistant: an attacker with repo access can't just
        // shorten the policy and immediately wipe everything.
        var config = await RepositoryManager.ReadConfigAsync(_repoPath, ct);
        if (config.AppendOnly)
        {
            // Strictly stronger than the immutability window: nothing is
            // ever eligible for deletion, full stop.
            foreach (var snapshot in allSnapshots)
            {
                keepIds.Add(snapshot.SnapshotId);
            }
        }
        else if (config.ImmutabilityWindowDays is { } windowDays)
        {
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(windowDays);
            foreach (var snapshot in allSnapshots.Where(s => s.StartedAtUtc >= cutoff))
            {
                keepIds.Add(snapshot.SnapshotId);
            }
        }

        var snapshotsToDelete = allSnapshots.Where(s => !keepIds.Contains(s.SnapshotId)).ToList();

        var packSubKey = SubKeys.Derive(_masterKey, SubKeys.PackKeyInfo);
        var liveBlobIds = new HashSet<string>();
        using (var blobStore = new BlobStore(_repoPath, packSubKey, db))
        {
            foreach (var snapshot in allSnapshots.Where(s => keepIds.Contains(s.SnapshotId)))
            {
                ct.ThrowIfCancellationRequested();
                await MarkReachableAsync(blobStore, snapshot.RootTreeBlobId, liveBlobIds, ct);
            }
        }

        var allBlobs = await db.Blobs.AsNoTracking().ToListAsync(ct);
        var packs = await db.Packs.AsNoTracking().ToListAsync(ct);
        var blobsByPack = allBlobs.GroupBy(b => b.PackId).ToDictionary(g => g.Key, g => g.ToList());

        var packsToDelete = new List<string>();
        var packsToRepack = new List<string>();

        foreach (var pack in packs)
        {
            var blobsInPack = blobsByPack.TryGetValue(pack.PackId, out var list) ? list : [];
            var liveCount = blobsInPack.Count(b => liveBlobIds.Contains(b.BlobId));

            if (blobsInPack.Count == 0 || liveCount == 0)
            {
                packsToDelete.Add(pack.PackId);
            }
            else if ((double)liveCount / blobsInPack.Count < RepackLivenessThreshold)
            {
                packsToRepack.Add(pack.PackId);
            }
        }

        if (dryRun)
        {
            return new PruneResult(true, snapshotsToDelete.Select(s => s.SnapshotId).ToList(), packsToDelete, packsToRepack, 0);
        }

        var bytesFreed = 0L;

        if (packsToRepack.Count > 0)
        {
            bytesFreed += await RepackAsync(db, packSubKey, packsToRepack, liveBlobIds, ct);
        }

        foreach (var packId in packsToDelete)
        {
            ct.ThrowIfCancellationRequested();
            var pack = packs.First(p => p.PackId == packId);
            bytesFreed += pack.SizeBytes;

            db.Blobs.RemoveRange(db.Blobs.Where(b => b.PackId == packId));
            db.Packs.Remove(await db.Packs.FindAsync([packId], ct) ?? throw new InvalidOperationException());
            await db.SaveChangesAsync(ct);

            var path = Path.Combine(_repoPath, PackId.RelativePath(packId));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        foreach (var snapshot in snapshotsToDelete)
        {
            ct.ThrowIfCancellationRequested();
            db.SnapshotPaths.RemoveRange(db.SnapshotPaths.Where(sp => sp.SnapshotId == snapshot.SnapshotId));
            db.Snapshots.Remove(await db.Snapshots.FindAsync([snapshot.SnapshotId], ct) ?? throw new InvalidOperationException());
            await db.SaveChangesAsync(ct);
        }

        if (snapshotsToDelete.Count > 0 || packsToDelete.Count > 0 || packsToRepack.Count > 0)
        {
            await AuditLogger.RecordAsync(
                db,
                "prune",
                $"{snapshotsToDelete.Count} snapshot silindi, {packsToDelete.Count} pack silindi, " +
                $"{packsToRepack.Count} pack repack edildi, {bytesFreed:N0} bayt boşaltıldı. " +
                $"Silinen snapshot'lar: {string.Join(", ", snapshotsToDelete.Select(s => s.SnapshotId))}",
                ct);
        }

        return new PruneResult(
            false,
            snapshotsToDelete.Select(s => s.SnapshotId).ToList(),
            packsToDelete,
            packsToRepack,
            bytesFreed);
    }

    private async Task<long> RepackAsync(
        CatalogDbContext db, byte[] packSubKey, List<string> packIdsToRepack, HashSet<string> liveBlobIds, CancellationToken ct)
    {
        await using (var newPacks = new PackFileSet(_repoPath, packSubKey, db))
        {
            foreach (var packId in packIdsToRepack)
            {
                ct.ThrowIfCancellationRequested();

                var packPath = Path.Combine(_repoPath, PackId.RelativePath(packId));
                using var stream = File.OpenRead(packPath);
                var reader = new PackReader(stream, packSubKey);

                for (var i = 0; i < reader.Entries.Count; i++)
                {
                    var entry = reader.Entries[i];
                    var blobIdHex = Convert.ToHexStringLower(entry.BlobId);
                    if (!liveBlobIds.Contains(blobIdHex))
                    {
                        continue; // dead — do not carry forward
                    }

                    var plaintext = reader.ReadBlob(i);
                    var (compressed, codec) = CompressionCodec.Compress(plaintext);
                    await newPacks.WriteAsync(entry.BlobId, entry.Kind, compressed, codec, plaintext.Length, ct);
                }
            }

            // Commits the new pack(s) and repoints every live blob's catalog
            // row to them BEFORE any old pack is touched.
            await newPacks.FlushAsync(ct);
        }

        ct.ThrowIfCancellationRequested();

        var bytesFreed = 0L;
        foreach (var packId in packIdsToRepack)
        {
            var oldPack = await db.Packs.FindAsync([packId], ct);
            if (oldPack is null)
            {
                continue; // already gone somehow — nothing to free
            }

            bytesFreed += oldPack.SizeBytes;

            // Only dead blobs can still reference this pack — live ones were
            // already repointed above by PackFileSet's upsert.
            db.Blobs.RemoveRange(db.Blobs.Where(b => b.PackId == packId));
            db.Packs.Remove(oldPack);
            await db.SaveChangesAsync(ct);

            var path = Path.Combine(_repoPath, PackId.RelativePath(packId));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return bytesFreed;
    }

    private static async Task MarkReachableAsync(BlobStore blobStore, string treeBlobIdHex, HashSet<string> live, CancellationToken ct)
    {
        if (!live.Add(treeBlobIdHex))
        {
            return; // already visited — dedup'd subtree shared by another snapshot
        }

        var bytes = await blobStore.ReadBlobAsync(treeBlobIdHex, ct);
        var tree = Tree.Deserialize(bytes);

        foreach (var node in tree.Nodes)
        {
            if (node.Kind == TreeNodeKind.File && node.ChunkBlobIdsHex is not null)
            {
                foreach (var chunkId in node.ChunkBlobIdsHex)
                {
                    live.Add(chunkId);
                }
            }
            else if (node.Kind == TreeNodeKind.Directory && node.SubTreeBlobIdHex is not null)
            {
                await MarkReachableAsync(blobStore, node.SubTreeBlobIdHex, live, ct);
            }
        }
    }

    private static List<string> ParseTags(string? tags) =>
        string.IsNullOrEmpty(tags) ? [] : tags.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
}
