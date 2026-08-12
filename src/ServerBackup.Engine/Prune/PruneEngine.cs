using Microsoft.EntityFrameworkCore;
using ServerBackup.Core.Crypto;
using ServerBackup.Core.Repository;
using ServerBackup.Core.Trees;
using ServerBackup.Data;
using ServerBackup.Data.Entities;
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

    /// <summary>
    /// dryRun defaults to true — pruning is destructive and must be an explicit opt-in.
    /// </summary>
    /// <param name="planId">
    /// When given, only that plan's snapshots are judged by the policy;
    /// everything else in the repository is kept untouched. A plan's retention
    /// rules describe that plan's own history, so applying them repository-wide
    /// lets a short-lived plan delete another plan's archive.
    /// </param>
    public async Task<PruneResult> RunAsync(
        RetentionPolicy policy, bool dryRun = true, string? planId = null, CancellationToken ct = default)
    {
        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));

        var allSnapshots = await db.Snapshots.AsNoTracking().ToListAsync(ct);
        var candidates = planId is null
            ? allSnapshots
            : allSnapshots.Where(s => s.PlanId == planId).ToList();

        var summaries = candidates
            .Select(s => new SnapshotSummary(s.SnapshotId, s.StartedAtUtc, ParseTags(s.Tags)))
            .ToList();
        var keepIds = RetentionEvaluator.SelectSnapshotsToKeep(summaries, policy, DateTimeOffset.UtcNow);

        // Out-of-scope snapshots are kept outright, which also keeps their
        // blobs reachable during the mark phase below.
        foreach (var snapshot in allSnapshots.Where(s => !candidates.Contains(s)))
        {
            keepIds.Add(snapshot.SnapshotId);
        }

        var config = await RepositoryManager.ReadConfigAsync(_repoPath, ct);
        ApplyProtections(config, allSnapshots, keepIds);

        var sweep = await SweepAsync(db, allSnapshots, keepIds, dryRun, "prune", ct);

        return new PruneResult(
            sweep.DryRun, sweep.Deleted, sweep.PacksToDelete, sweep.PacksToRepack, sweep.BytesFreed);
    }

    /// <summary>
    /// Deletes exactly the snapshots an operator asked for, independent of any
    /// retention policy: the keep-set is "everything except these ids", after
    /// which the very same mark-and-sweep/repack core as
    /// <see cref="RunAsync"/> runs — same write-then-delete ordering, same
    /// crash safety.
    /// <para>
    /// The repository's protections still win: with <c>AppendOnly</c> nothing
    /// can be deleted at all, and a snapshot inside the immutability window is
    /// kept even when it was named explicitly. Because the request here is
    /// explicit (unlike a policy run, where "not selected" is normal), every
    /// id that was asked for but not deleted is reported in
    /// <see cref="ManualPruneResult.Refused"/> with a
    /// <see cref="ManualPruneRefusalReason"/>, so the caller can tell the
    /// operator why nothing happened. Unknown ids are refused as
    /// <see cref="ManualPruneRefusalReason.NotFound"/> rather than throwing.
    /// </para>
    /// dryRun defaults to true — deletion is destructive and must be an explicit opt-in.
    /// </summary>
    public async Task<ManualPruneResult> RunManualAsync(
        IReadOnlyList<string> snapshotIdsToDelete, bool dryRun = true, CancellationToken ct = default)
    {
        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));

        var allSnapshots = await db.Snapshots.AsNoTracking().ToListAsync(ct);
        var known = allSnapshots.Select(s => s.SnapshotId).ToHashSet(StringComparer.Ordinal);

        var requested = snapshotIdsToDelete.Distinct(StringComparer.Ordinal).ToList();
        var refusals = requested
            .Where(id => !known.Contains(id))
            .Select(id => new ManualPruneRefusal(id, ManualPruneRefusalReason.NotFound))
            .ToList();

        var targeted = requested.Where(known.Contains).ToHashSet(StringComparer.Ordinal);
        var keepIds = known.Where(id => !targeted.Contains(id)).ToHashSet(StringComparer.Ordinal);

        var config = await RepositoryManager.ReadConfigAsync(_repoPath, ct);
        var forcedKeeps = ApplyProtections(config, allSnapshots, keepIds);

        refusals.AddRange(targeted
            .Where(forcedKeeps.ContainsKey)
            .Select(id => new ManualPruneRefusal(id, forcedKeeps[id])));

        var sweep = await SweepAsync(db, allSnapshots, keepIds, dryRun, "prune-manual", ct);

        return new ManualPruneResult(
            sweep.DryRun, sweep.Deleted, refusals, sweep.PacksToDelete, sweep.PacksToRepack, sweep.BytesFreed);
    }

    /// <summary>
    /// Immutability window (plan Faz 11): a snapshot younger than this can
    /// never be deleted, regardless of what the retention policy — or an
    /// operator clicking "sil" in the panel — says. This is what makes prune
    /// ransomware-resistant: an attacker with repo access can't just shorten
    /// the policy and immediately wipe everything. Adds every protected id to
    /// <paramref name="keepIds"/> and returns the ids it forced, with the
    /// reason, so an explicit caller can report them back.
    /// </summary>
    private static Dictionary<string, ManualPruneRefusalReason> ApplyProtections(
        RepositoryConfig config, List<SnapshotEntity> allSnapshots, HashSet<string> keepIds)
    {
        var forced = new Dictionary<string, ManualPruneRefusalReason>(StringComparer.Ordinal);

        if (config.AppendOnly)
        {
            // Strictly stronger than the immutability window: nothing is
            // ever eligible for deletion, full stop.
            foreach (var snapshot in allSnapshots)
            {
                keepIds.Add(snapshot.SnapshotId);
                forced[snapshot.SnapshotId] = ManualPruneRefusalReason.AppendOnly;
            }
        }
        else if (config.ImmutabilityWindowDays is { } windowDays)
        {
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(windowDays);
            foreach (var snapshot in allSnapshots.Where(s => s.StartedAtUtc >= cutoff))
            {
                keepIds.Add(snapshot.SnapshotId);
                forced[snapshot.SnapshotId] = ManualPruneRefusalReason.ImmutabilityWindow;
            }
        }

        return forced;
    }

    /// <summary>
    /// The shared mark-and-sweep core: everything outside <paramref name="keepIds"/>
    /// dies. Both the policy-driven and the manual entry points funnel through
    /// here so there is exactly one implementation of the write-then-delete
    /// ordering (CLAUDE.md rule 5).
    /// </summary>
    private async Task<SweepOutcome> SweepAsync(
        CatalogDbContext db,
        List<SnapshotEntity> allSnapshots,
        HashSet<string> keepIds,
        bool dryRun,
        string auditAction,
        CancellationToken ct)
    {
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
            return new SweepOutcome(
                true, snapshotsToDelete.Select(s => s.SnapshotId).ToList(), packsToDelete, packsToRepack, 0);
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
                auditAction,
                $"{snapshotsToDelete.Count} snapshot silindi, {packsToDelete.Count} pack silindi, " +
                $"{packsToRepack.Count} pack repack edildi, {bytesFreed:N0} bayt boşaltıldı. " +
                $"Silinen snapshot'lar: {string.Join(", ", snapshotsToDelete.Select(s => s.SnapshotId))}",
                ct);
        }

        return new SweepOutcome(
            false,
            snapshotsToDelete.Select(s => s.SnapshotId).ToList(),
            packsToDelete,
            packsToRepack,
            bytesFreed);
    }

    private sealed record SweepOutcome(
        bool DryRun,
        IReadOnlyList<string> Deleted,
        IReadOnlyList<string> PacksToDelete,
        IReadOnlyList<string> PacksToRepack,
        long BytesFreed);

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
