using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using ServerBackup.Core.Chunking;
using ServerBackup.Core.Crypto;
using ServerBackup.Core.Repository;
using ServerBackup.Core.Trees;
using ServerBackup.Data;
using ServerBackup.Data.Entities;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Scanning;

namespace ServerBackup.Engine.Backup;

/// <summary>
/// Drives a full backup run: walks the source tree (building blob ids as it
/// goes — pure hashing, no I/O wait), while a decoupled background pipeline
/// compresses new blobs in parallel and a single writer stage encrypts and
/// places them into packs. See plan Faz 5.
/// </summary>
public sealed class BackupEngine
{
    private readonly ISourceProvider _source;
    private readonly string _repoPath;
    private readonly byte[] _masterKey;
    private readonly ScanFilter? _filter;
    private readonly IProgress<BackupProgress>? _progress;
    private readonly FastCdcChunker _chunker;
    private readonly byte[] _idKey;
    private readonly int _compressionParallelism;

    public BackupEngine(
        ISourceProvider source,
        string repoPath,
        byte[] masterKey,
        ScanFilter? filter = null,
        IProgress<BackupProgress>? progress = null,
        int? compressionParallelism = null)
    {
        _source = source;
        _repoPath = repoPath;
        _masterKey = masterKey;
        _filter = filter;
        _progress = progress;
        _idKey = SubKeys.Derive(masterKey, SubKeys.ChunkIdInfo);
        _chunker = new FastCdcChunker(GearTableFactory.Derive(masterKey));
        _compressionParallelism = compressionParallelism ?? Math.Max(1, Environment.ProcessorCount - 1);
    }

    /// <summary>Per-run counters. Kept off the engine instance so a single BackupEngine can safely run multiple backups sequentially.</summary>
    private sealed class RunCounters
    {
        public long FilesUnchanged;
        public long FilesChanged;
        public long NewBlobsWritten;
        public long NewBytesWritten;
    }

    public async Task<string> RunAsync(IReadOnlyList<string> sourcePaths, string? parentSnapshotId = null, CancellationToken ct = default)
    {
        if (sourcePaths.Count == 0)
        {
            throw new ArgumentException("A backup needs at least one source path.", nameof(sourcePaths));
        }

        var counters = new RunCounters();
        var startedAtUtc = DateTimeOffset.UtcNow;
        using var repoLock = RepositoryLock.Acquire(_repoPath);

        var dbPath = Path.Combine(_repoPath, "catalog.db");
        await using var db = CatalogDbContextFactory.Create(dbPath);

        var packSubKey = SubKeys.Derive(_masterKey, SubKeys.PackKeyInfo);
        var existingBlobIds = await db.Blobs.Select(b => b.BlobId).ToListAsync(ct);
        var claimed = new ConcurrentDictionary<string, byte>(
            existingBlobIds.Select(id => new KeyValuePair<string, byte>(id, 0)));

        ParentSnapshotCache? parentCache = null;
        if (parentSnapshotId is not null)
        {
            var parentSnapshot = await db.Snapshots.AsNoTracking()
                .FirstOrDefaultAsync(s => s.SnapshotId == parentSnapshotId, ct)
                ?? throw new InvalidOperationException($"Parent snapshot '{parentSnapshotId}' not found.");

            using var parentBlobStore = new BlobStore(_repoPath, packSubKey, db);
            parentCache = await ParentSnapshotCache.LoadAsync(parentBlobStore, parentSnapshot.RootTreeBlobId, ct);
        }

        var compressionChannel = Channel.CreateBounded<PendingBlob>(
            new BoundedChannelOptions(256) { SingleReader = false, SingleWriter = true });
        var writeChannel = Channel.CreateBounded<PreparedBlob>(
            new BoundedChannelOptions(256) { SingleReader = true, SingleWriter = false });

        await using var packFileSet = new PackFileSet(_repoPath, packSubKey, db);

        var compressionWorkers = Enumerable.Range(0, _compressionParallelism)
            .Select(_ => Task.Run(
                () => CompressionWorkerAsync(compressionChannel.Reader, writeChannel.Writer, claimed, counters, ct), ct))
            .ToArray();
        var writerTask = Task.Run(() => WriterLoopAsync(writeChannel.Reader, packFileSet, ct), ct);

        try
        {
            var rootNodes = new List<TreeNode>();
            foreach (var path in sourcePaths)
            {
                var entry = _source.GetEntry(path);
                rootNodes.Add(entry.IsDirectory
                    ? await BuildDirectoryNodeAsync(entry, entry.Name, parentCache, compressionChannel.Writer, counters, ct)
                    : await BuildFileNodeAsync(entry, entry.Name, parentCache, compressionChannel.Writer, counters, ct));
            }

            compressionChannel.Writer.Complete();
            await Task.WhenAll(compressionWorkers);
            writeChannel.Writer.Complete();
            await writerTask;

            var rootTree = new Tree(rootNodes);
            var rootTreeBlobIdHex = await WriteTreeIfNewAsync(rootTree, packFileSet, claimed, counters, ct);

            await packFileSet.FlushAsync(ct);

            var snapshotId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            db.Snapshots.Add(new SnapshotEntity
            {
                SnapshotId = snapshotId,
                PlanId = null,
                ParentSnapshotId = parentSnapshotId,
                StartedAtUtc = startedAtUtc,
                FinishedAtUtc = DateTimeOffset.UtcNow,
                RootTreeBlobId = rootTreeBlobIdHex,
            });
            foreach (var path in sourcePaths)
            {
                db.SnapshotPaths.Add(new SnapshotPathEntity { SnapshotId = snapshotId, SourcePath = path });
            }

            await db.SaveChangesAsync(ct);

            return snapshotId;
        }
        catch
        {
            compressionChannel.Writer.TryComplete();
            try
            {
                await Task.WhenAll(compressionWorkers);
            }
            catch
            {
                // The original exception (or cancellation) is what we report below.
            }

            writeChannel.Writer.TryComplete();
            try
            {
                await writerTask;
            }
            catch
            {
                // Same as above — avoid masking the real failure.
            }

            throw;
        }
    }

    private async Task<Tree> BuildTreeAsync(
        string directoryPath, string relativePath, ParentSnapshotCache? parentCache,
        ChannelWriter<PendingBlob> writer, RunCounters counters, CancellationToken ct)
    {
        var nodes = new List<TreeNode>();

        foreach (var child in _source.EnumerateChildren(directoryPath).OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            if (_filter?.IsExcluded(child) == true)
            {
                continue;
            }

            var childRelativePath = relativePath.Length == 0 ? child.Name : $"{relativePath}/{child.Name}";
            nodes.Add(child.IsDirectory
                ? await BuildDirectoryNodeAsync(child, childRelativePath, parentCache, writer, counters, ct)
                : await BuildFileNodeAsync(child, childRelativePath, parentCache, writer, counters, ct));
        }

        return new Tree(nodes);
    }

    private async Task<TreeNode> BuildDirectoryNodeAsync(
        SourceEntry entry, string relativePath, ParentSnapshotCache? parentCache,
        ChannelWriter<PendingBlob> writer, RunCounters counters, CancellationToken ct)
    {
        string? subTreeBlobIdHex = null;
        if (!entry.IsReparsePoint)
        {
            var subTree = await BuildTreeAsync(entry.FullPath, relativePath, parentCache, writer, counters, ct);
            var subTreeBytes = subTree.Serialize();
            var subTreeBlobId = BlobId.Compute(_idKey, subTreeBytes);
            await writer.WriteAsync(new PendingBlob(subTreeBlobId, BlobKind.Tree, subTreeBytes), ct);
            subTreeBlobIdHex = Convert.ToHexStringLower(subTreeBlobId);
        }

        return new TreeNode(
            Name: entry.Name,
            Kind: TreeNodeKind.Directory,
            Size: 0,
            ModifiedAtFileTimeUtc: entry.LastWriteTimeUtc.ToFileTimeUtc(),
            Attributes: (int)entry.Attributes,
            Sddl: _source.TryGetSddl(entry.FullPath),
            ChunkBlobIdsHex: null,
            SubTreeBlobIdHex: subTreeBlobIdHex);
    }

    private async Task<TreeNode> BuildFileNodeAsync(
        SourceEntry entry, string relativePath, ParentSnapshotCache? parentCache,
        ChannelWriter<PendingBlob> writer, RunCounters counters, CancellationToken ct)
    {
        var mtimeFileTime = entry.LastWriteTimeUtc.ToFileTimeUtc();

        if (parentCache is not null &&
            parentCache.TryGetUnchangedFile(relativePath, entry.Size, mtimeFileTime, (int)entry.Attributes, out var cached))
        {
            Interlocked.Increment(ref counters.FilesUnchanged);
            ReportProgress(counters);
            return cached;
        }

        Interlocked.Increment(ref counters.FilesChanged);

        var chunkIds = new List<string>();
        using (var stream = _source.OpenRead(entry.FullPath))
        {
            foreach (var chunk in _chunker.Chunk(stream))
            {
                ct.ThrowIfCancellationRequested();
                var blobId = BlobId.Compute(_idKey, chunk);
                chunkIds.Add(Convert.ToHexStringLower(blobId));
                await writer.WriteAsync(new PendingBlob(blobId, BlobKind.Data, chunk), ct);
            }
        }

        ReportProgress(counters);

        return new TreeNode(
            Name: entry.Name,
            Kind: TreeNodeKind.File,
            Size: entry.Size,
            ModifiedAtFileTimeUtc: mtimeFileTime,
            Attributes: (int)entry.Attributes,
            Sddl: _source.TryGetSddl(entry.FullPath),
            ChunkBlobIdsHex: chunkIds,
            SubTreeBlobIdHex: null);
    }

    /// <summary>The pipeline is already drained by the time the root tree is known, so this writes it directly.</summary>
    private async Task<string> WriteTreeIfNewAsync(
        Tree tree, PackFileSet packFileSet, ConcurrentDictionary<string, byte> claimed, RunCounters counters, CancellationToken ct)
    {
        var bytes = tree.Serialize();
        var blobId = BlobId.Compute(_idKey, bytes);
        var blobIdHex = Convert.ToHexStringLower(blobId);

        if (claimed.TryAdd(blobIdHex, 0))
        {
            var (compressed, codec) = CompressionCodec.Compress(bytes);
            await packFileSet.WriteAsync(blobId, BlobKind.Tree, compressed, codec, bytes.Length, ct);
            Interlocked.Increment(ref counters.NewBlobsWritten);
            Interlocked.Add(ref counters.NewBytesWritten, bytes.Length);
        }

        return blobIdHex;
    }

    private async Task CompressionWorkerAsync(
        ChannelReader<PendingBlob> reader, ChannelWriter<PreparedBlob> writer,
        ConcurrentDictionary<string, byte> claimed, RunCounters counters, CancellationToken ct)
    {
        await foreach (var pending in reader.ReadAllAsync(ct))
        {
            var blobIdHex = Convert.ToHexStringLower(pending.BlobId);
            if (!claimed.TryAdd(blobIdHex, 0))
            {
                continue;
            }

            var (compressed, codec) = CompressionCodec.Compress(pending.Plaintext);
            Interlocked.Increment(ref counters.NewBlobsWritten);
            Interlocked.Add(ref counters.NewBytesWritten, pending.Plaintext.Length);

            await writer.WriteAsync(
                new PreparedBlob(pending.BlobId, pending.Kind, compressed, codec, pending.Plaintext.Length), ct);
        }
    }

    private static async Task WriterLoopAsync(ChannelReader<PreparedBlob> reader, PackFileSet packFileSet, CancellationToken ct)
    {
        await foreach (var prepared in reader.ReadAllAsync(ct))
        {
            await packFileSet.WriteAsync(prepared.BlobId, prepared.Kind, prepared.CompressedData, prepared.Codec, prepared.LenPlain, ct);
        }
    }

    private void ReportProgress(RunCounters counters)
    {
        if (_progress is null)
        {
            return;
        }

        var unchanged = Interlocked.Read(ref counters.FilesUnchanged);
        var changed = Interlocked.Read(ref counters.FilesChanged);
        _progress.Report(new BackupProgress(
            FilesScanned: unchanged + changed,
            FilesUnchanged: unchanged,
            FilesChanged: changed,
            NewBlobsWritten: Interlocked.Read(ref counters.NewBlobsWritten),
            NewBytesWritten: Interlocked.Read(ref counters.NewBytesWritten)));
    }
}
