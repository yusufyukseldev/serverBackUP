using System.Security.AccessControl;
using Microsoft.EntityFrameworkCore;
using ServerBackup.Core.Crypto;
using ServerBackup.Core.Repository;
using ServerBackup.Core.Trees;
using ServerBackup.Data;
using ServerBackup.Engine.Audit;
using ServerBackup.Engine.Backup;
using ServerBackup.Engine.Repository;

namespace ServerBackup.Engine.Restore;

/// <summary>
/// Restores a snapshot to disk. Reads are grouped by pack (each pack's
/// header is parsed once, and its needed blobs are decrypted in on-disk
/// order) rather than following file order, since a single file's chunks
/// can be scattered across many packs — see plan Faz 6.
///
/// Content is written to a sibling <see cref="TempSuffix"/> file and renamed
/// onto the final name only once that file's last chunk has landed, so a
/// cancelled or killed restore can never leave a half-written file sitting at
/// a real name where an operator would read it as restored. ACLs and
/// timestamps are applied per file right after its rename; directory metadata
/// still comes last, since writing children into a directory would otherwise
/// overwrite the timestamp we just restored.
/// </summary>
public sealed class RestoreEngine
{
    /// <summary>
    /// Suffix of the in-progress copy of a file being restored. Anything with
    /// this suffix is scratch: a leftover from a crashed run is truncated and
    /// reused, never read back.
    /// </summary>
    internal const string TempSuffix = ".sbrestore-tmp";

    private readonly string _repoPath;
    private readonly byte[] _masterKey;
    private readonly IProgress<RestoreProgress>? _progress;

    public RestoreEngine(string repoPath, byte[] masterKey, IProgress<RestoreProgress>? progress = null)
    {
        _repoPath = repoPath;
        _masterKey = masterKey;
        _progress = progress;
    }

    /// <summary>
    /// Extracts a snapshot into <paramref name="targetPath"/>. Nothing outside
    /// that directory is touched — see <see cref="ResolveUnderRoot"/>.
    /// </summary>
    public async Task RestoreAsync(
        string snapshotId,
        string targetPath,
        IReadOnlyList<string>? selectedRelativePaths = null,
        OverwritePolicy overwritePolicy = OverwritePolicy.Overwrite,
        CancellationToken ct = default)
    {
        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));

        var snapshot = await db.Snapshots.AsNoTracking().FirstOrDefaultAsync(s => s.SnapshotId == snapshotId, ct)
            ?? throw new InvalidOperationException($"Snapshot '{snapshotId}' not found.");

        var packSubKey = SubKeys.Derive(_masterKey, SubKeys.PackKeyInfo);
        using var blobStore = new BlobStore(_repoPath, packSubKey, db);

        var plannedFiles = new List<PlannedFile>();
        var plannedDirs = new List<PlannedDirectory>();
        await WalkAsync(blobStore, snapshot.RootTreeBlobId, "", targetPath, selectedRelativePaths, plannedFiles, plannedDirs, ct);

        await ApplyPlanAsync(db, plannedFiles, plannedDirs, overwritePolicy, snapshotId, targetPath, ct);
    }

    /// <summary>
    /// Puts a snapshot back where it was taken from: every root is written over
    /// its original source path instead of into a chosen directory. This is the
    /// "revert to this backup" operation, so it overwrites by default — the
    /// caller is expected to have confirmed the target paths first.
    /// </summary>
    /// <returns>The original paths that were written to.</returns>
    public async Task<IReadOnlyList<string>> RestoreInPlaceAsync(
        string snapshotId,
        IReadOnlyList<string>? selectedRelativePaths = null,
        OverwritePolicy overwritePolicy = OverwritePolicy.Overwrite,
        CancellationToken ct = default)
    {
        await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));

        var snapshot = await db.Snapshots.AsNoTracking().FirstOrDefaultAsync(s => s.SnapshotId == snapshotId, ct)
            ?? throw new InvalidOperationException($"Snapshot '{snapshotId}' not found.");

        var sourcePaths = await db.SnapshotPaths.AsNoTracking()
            .Where(p => p.SnapshotId == snapshotId)
            .Select(p => p.SourcePath)
            .ToListAsync(ct);

        var originalByRootName = MapRootNamesToSourcePaths(sourcePaths);

        var packSubKey = SubKeys.Derive(_masterKey, SubKeys.PackKeyInfo);
        using var blobStore = new BlobStore(_repoPath, packSubKey, db);

        var rootTree = Tree.Deserialize(await blobStore.ReadBlobAsync(snapshot.RootTreeBlobId, ct));

        var plannedFiles = new List<PlannedFile>();
        var plannedDirs = new List<PlannedDirectory>();
        var written = new List<string>();

        foreach (var node in rootTree.Nodes)
        {
            ct.ThrowIfCancellationRequested();

            if (!originalByRootName.TryGetValue(node.Name, out var originalPath))
            {
                throw new InvalidOperationException(
                    $"Snapshot root '{node.Name}' has no recorded source path, so its original location is unknown.");
            }

            if (!IsInScope(node.Name, selectedRelativePaths))
            {
                continue;
            }

            written.Add(originalPath);

            if (node.Kind == TreeNodeKind.Directory)
            {
                plannedDirs.Add(new PlannedDirectory(originalPath, node.ModifiedAtFileTimeUtc, node.Attributes, node.Sddl));

                if (node.SubTreeBlobIdHex is not null)
                {
                    await WalkAsync(
                        blobStore, node.SubTreeBlobIdHex, node.Name, originalPath,
                        selectedRelativePaths, plannedFiles, plannedDirs, ct);
                }
            }
            else
            {
                plannedFiles.Add(new PlannedFile(
                    node.Name, originalPath, node.Size, node.ModifiedAtFileTimeUtc,
                    node.Attributes, node.Sddl, node.ChunkBlobIdsHex ?? []));
            }
        }

        await ApplyPlanAsync(db, plannedFiles, plannedDirs, overwritePolicy, snapshotId, "orijinal konum", ct);
        return written;
    }

    /// <summary>
    /// Re-derives the single path segment a source root is stored under, so a
    /// snapshot root can be matched back to the path it came from. Matching is
    /// by name rather than by position because an unreadable root is dropped
    /// from the tree, which would shift every index after it.
    /// </summary>
    internal static string RootSegmentOf(string sourcePath)
    {
        // Pure string work: the original path may no longer exist on disk.
        var full = Path.GetFullPath(sourcePath);
        var name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return BackupEngine.RootNodeName(name.Length == 0 ? full : name);
    }

    internal static Dictionary<string, string> MapRootNamesToSourcePaths(IReadOnlyList<string> sourcePaths)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in sourcePaths)
        {
            var segment = RootSegmentOf(path);
            if (map.TryGetValue(segment, out var existing))
            {
                // Two sources whose last segment collides (C:\a\Veri and D:\b\Veri)
                // are indistinguishable in the tree; guessing could restore over
                // the wrong volume, so refuse instead.
                throw new InvalidOperationException(
                    $"Source paths '{existing}' and '{path}' share the snapshot root name '{segment}'; "
                    + "restoring to the original location would be ambiguous.");
            }

            map[segment] = path;
        }

        return map;
    }

    private async Task ApplyPlanAsync(
        CatalogDbContext db, List<PlannedFile> plannedFiles, List<PlannedDirectory> plannedDirs,
        OverwritePolicy overwritePolicy, string snapshotId, string targetLabel, CancellationToken ct)
    {
        foreach (var dir in plannedDirs)
        {
            Directory.CreateDirectory(dir.TargetPath);
        }

        // The whole plan is settled before the first byte is written so the
        // progress totals a caller renders a percentage against never move.
        var filesToWrite = new List<PlannedFile>();
        foreach (var file in plannedFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file.TargetPath)!);

            if (File.Exists(file.TargetPath))
            {
                if (overwritePolicy == OverwritePolicy.Skip)
                {
                    continue;
                }

                if (overwritePolicy == OverwritePolicy.Fail)
                {
                    throw new IOException($"Target file already exists: {file.TargetPath}");
                }
            }

            filesToWrite.Add(file);
        }

        var filesPlanned = filesToWrite.Count;
        var bytesPlanned = filesToWrite.Sum(f => f.Size);
        long filesCompleted = 0;
        long bytesCompleted = 0;

        void Report() => _progress?.Report(new RestoreProgress(filesPlanned, filesCompleted, bytesPlanned, bytesCompleted));

        void CompleteFile(FileWriteState state)
        {
            PromoteTempFile(state.TempPath, state.File.TargetPath);
            ApplyMetadata(state.File.TargetPath, state.File.ModifiedAtFileTimeUtc, state.File.Attributes, state.File.Sddl, isDirectory: false);

            filesCompleted++;
            bytesCompleted += state.File.Size;
            Report();
        }

        Report();

        var pendingFiles = new List<FileWriteState>(filesToWrite.Count);
        foreach (var file in filesToWrite)
        {
            ct.ThrowIfCancellationRequested();

            var state = new FileWriteState(file, file.TargetPath + TempSuffix);
            CreateTempFile(state.TempPath, file.Size);

            if (state.ChunksRemaining == 0)
            {
                // No chunks will ever land for an empty file, so its rename has
                // to happen here or it would stay a temp file forever.
                CompleteFile(state);
                continue;
            }

            pendingFiles.Add(state);
        }

        await WriteChunksGroupedByPackAsync(
            db, _repoPath, SubKeys.Derive(_masterKey, SubKeys.PackKeyInfo), pendingFiles, CompleteFile, ct);

        foreach (var dir in plannedDirs)
        {
            ApplyMetadata(dir.TargetPath, dir.ModifiedAtFileTimeUtc, dir.Attributes, dir.Sddl, isDirectory: true);
        }

        await AuditLogger.RecordAsync(
            db,
            "restore",
            $"Snapshot '{snapshotId}' → '{targetLabel}' ({filesToWrite.Count} dosya).",
            ct);
    }

    /// <summary>
    /// Windows refuses <c>FileMode.Create</c> over a file that is read-only,
    /// hidden or system, so overwriting one of those needs the attribute
    /// stripped first. A restore reapplies the snapshot's own attributes at the
    /// end, so nothing is lost by clearing them here — and without this, a
    /// single hidden file (desktop.ini, Thumbs.db) aborts an entire restore.
    /// </summary>
    private static void ClearAttributesBlockingOverwrite(string path)
    {
        const FileAttributes Blocking = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System;

        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & Blocking) != 0)
            {
                File.SetAttributes(path, attributes & ~Blocking);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // If we can't clear it, File.Create reports the real problem below.
        }
    }

    /// <summary>
    /// Creates the scratch file a restore writes into. A leftover from a
    /// crashed run is truncated by <see cref="File.Create(string)"/> before
    /// <c>SetLength</c> re-expands it, so its old bytes can never survive into
    /// a hole this run does not write over.
    /// </summary>
    private static void CreateTempFile(string tempPath, long size)
    {
        if (File.Exists(tempPath))
        {
            ClearAttributesBlockingOverwrite(tempPath);
        }

        using var fs = File.Create(tempPath);
        fs.SetLength(size);
    }

    /// <summary>
    /// Publishes a fully written file under its real name. Same-volume moves
    /// are atomic on NTFS, which is what makes "present at the final name"
    /// mean "correct" for a crash that lands anywhere in this restore.
    /// </summary>
    private static void PromoteTempFile(string tempPath, string targetPath)
    {
        if (File.Exists(targetPath))
        {
            ClearAttributesBlockingOverwrite(targetPath);
        }

        File.Move(tempPath, targetPath, overwrite: true);
    }

    private static async Task WriteChunksGroupedByPackAsync(
        CatalogDbContext db, string repoPath, byte[] packSubKey,
        List<FileWriteState> filesToWrite, Action<FileWriteState> onFileCompleted, CancellationToken ct)
    {
        if (filesToWrite.Count == 0)
        {
            return;
        }

        var blobInfo = await db.Blobs.AsNoTracking()
            .Select(b => new { b.BlobId, b.PackId, b.LenPlain })
            .ToDictionaryAsync(b => b.BlobId, ct);

        var chunkPlan = new List<(string BlobIdHex, FileWriteState State, long Offset, string PackId)>();
        foreach (var state in filesToWrite)
        {
            long offset = 0;
            foreach (var blobIdHex in state.File.ChunkBlobIdsHex)
            {
                if (!blobInfo.TryGetValue(blobIdHex, out var info))
                {
                    throw new InvalidOperationException($"Blob '{blobIdHex}' referenced by '{state.File.RelativePath}' is missing from the catalog.");
                }

                chunkPlan.Add((blobIdHex, state, offset, info.PackId));
                offset += info.LenPlain;
            }
        }

        foreach (var packGroup in chunkPlan.GroupBy(c => c.PackId))
        {
            ct.ThrowIfCancellationRequested();

            var packPath = Path.Combine(repoPath, PackId.RelativePath(packGroup.Key));
            using var packStream = File.OpenRead(packPath);
            var reader = new PackReader(packStream, packSubKey);

            var indexByBlobId = new Dictionary<string, int>();
            for (var i = 0; i < reader.Entries.Count; i++)
            {
                indexByBlobId[Convert.ToHexStringLower(reader.Entries[i].BlobId)] = i;
            }

            foreach (var item in packGroup.OrderBy(c => indexByBlobId[c.BlobIdHex]))
            {
                ct.ThrowIfCancellationRequested();

                var plaintext = reader.ReadBlob(indexByBlobId[item.BlobIdHex]);
                using (var fs = new FileStream(item.State.TempPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                {
                    fs.Seek(item.Offset, SeekOrigin.Begin);
                    fs.Write(plaintext);
                }

                if (--item.State.ChunksRemaining == 0)
                {
                    onFileCompleted(item.State);

                    // Checked per completed file as well as per chunk, so a
                    // cancel lands promptly on a plan of many tiny files too.
                    ct.ThrowIfCancellationRequested();
                }
            }
        }
    }

    /// <summary>
    /// A file being restored, and how many of its chunks are still outstanding.
    /// Chunks are written in pack order rather than file order, so this counter
    /// is what tells us the moment a file is whole and can take its real name.
    /// </summary>
    private sealed class FileWriteState(PlannedFile file, string tempPath)
    {
        public PlannedFile File { get; } = file;

        public string TempPath { get; } = tempPath;

        public int ChunksRemaining { get; set; } = file.ChunkBlobIdsHex.Count;
    }

    /// <summary>
    /// Reapplies timestamp, ACL and attributes. Each is attempted separately:
    /// writing an owner back usually needs SeRestorePrivilege, and when that
    /// step is allowed to abort the others the file quietly comes back without
    /// its attributes. Attributes go last because the read-only flag would
    /// otherwise block the writes above.
    /// </summary>
    private static void ApplyMetadata(string path, long modifiedAtFileTimeUtc, int attributes, string? sddl, bool isDirectory)
    {
        TryMetadataStep(() => File.SetLastWriteTimeUtc(path, DateTime.FromFileTimeUtc(modifiedAtFileTimeUtc)));

        if (sddl is not null)
        {
            TryMetadataStep(() =>
            {
                if (isDirectory)
                {
                    var security = new DirectorySecurity();
                    security.SetSecurityDescriptorSddlForm(sddl);
                    new DirectoryInfo(path).SetAccessControl(security);
                }
                else
                {
                    var security = new FileSecurity();
                    security.SetSecurityDescriptorSddlForm(sddl);
                    new FileInfo(path).SetAccessControl(security);
                }
            });
        }

        TryMetadataStep(() => File.SetAttributes(path, (FileAttributes)attributes));
    }

    private static void TryMetadataStep(Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
                                      or PrivilegeNotHeldException or PlatformNotSupportedException)
        {
            // Best-effort — a metadata failure shouldn't fail an otherwise successful content restore.
        }
    }

    private static bool IsInScope(string relativePath, IReadOnlyList<string>? selected)
    {
        if (selected is null)
        {
            return true;
        }

        foreach (var sel in selected)
        {
            if (relativePath == sel
                || relativePath.StartsWith(sel + "/", StringComparison.Ordinal)
                || sel.StartsWith(relativePath + "/", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Joins a snapshot-relative path onto the restore target, refusing
    /// anything that would land outside it. A tree node name is data read back
    /// out of the repository, so a rooted (<c>C:\</c>) or traversing
    /// (<c>..</c>) name must never be able to steer a restore onto the live
    /// system — <see cref="Path.Combine(string, string)"/> would happily do so.
    /// </summary>
    internal static string ResolveUnderRoot(string targetRoot, string relativePath)
    {
        var rootFull = Path.GetFullPath(targetRoot);
        var combined = Path.GetFullPath(Path.Combine(rootFull, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        var rootPrefix = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Snapshot entry '{relativePath}' resolves outside the restore target '{targetRoot}'.");
        }

        return combined;
    }

    /// <summary>
    /// Plans one tree level. <paramref name="targetDirectory"/> is the folder
    /// this tree's entries land in and <paramref name="relativePath"/> is the
    /// snapshot-relative path they are known by; the two are only equal for an
    /// extract-to-a-directory restore. Keeping them apart is what lets an
    /// in-place restore point a root at the path it came from while selection
    /// filters still match snapshot-relative paths.
    /// </summary>
    private static async Task WalkAsync(
        BlobStore blobStore, string treeBlobIdHex, string relativePath, string targetDirectory,
        IReadOnlyList<string>? selectedRelativePaths,
        List<PlannedFile> plannedFiles, List<PlannedDirectory> plannedDirs, CancellationToken ct)
    {
        var bytes = await blobStore.ReadBlobAsync(treeBlobIdHex, ct);
        var tree = Tree.Deserialize(bytes);

        foreach (var node in tree.Nodes)
        {
            ct.ThrowIfCancellationRequested();

            var childRelativePath = relativePath.Length == 0 ? node.Name : $"{relativePath}/{node.Name}";
            if (!IsInScope(childRelativePath, selectedRelativePaths))
            {
                continue;
            }

            // Checked one segment at a time, so a traversing name at any depth
            // is caught rather than only one that escapes the outermost root.
            var childTargetPath = ResolveUnderRoot(targetDirectory, node.Name);

            if (node.Kind == TreeNodeKind.Directory)
            {
                plannedDirs.Add(new PlannedDirectory(childTargetPath, node.ModifiedAtFileTimeUtc, node.Attributes, node.Sddl));

                if (node.SubTreeBlobIdHex is not null)
                {
                    await WalkAsync(blobStore, node.SubTreeBlobIdHex, childRelativePath, childTargetPath, selectedRelativePaths, plannedFiles, plannedDirs, ct);
                }
            }
            else
            {
                plannedFiles.Add(new PlannedFile(
                    childRelativePath,
                    childTargetPath,
                    node.Size,
                    node.ModifiedAtFileTimeUtc,
                    node.Attributes,
                    node.Sddl,
                    node.ChunkBlobIdsHex ?? []));
            }
        }
    }
}
