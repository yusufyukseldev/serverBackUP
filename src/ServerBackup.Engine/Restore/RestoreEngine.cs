using System.Security.AccessControl;
using Microsoft.EntityFrameworkCore;
using ServerBackup.Core.Crypto;
using ServerBackup.Core.Repository;
using ServerBackup.Core.Trees;
using ServerBackup.Data;
using ServerBackup.Engine.Audit;
using ServerBackup.Engine.Repository;

namespace ServerBackup.Engine.Restore;

/// <summary>
/// Restores a snapshot to disk. Reads are grouped by pack (each pack's
/// header is parsed once, and its needed blobs are decrypted in on-disk
/// order) rather than following file order, since a single file's chunks
/// can be scattered across many packs — see plan Faz 6. ACLs and timestamps
/// are applied only after every byte has been written.
/// </summary>
public sealed class RestoreEngine
{
    private readonly string _repoPath;
    private readonly byte[] _masterKey;

    public RestoreEngine(string repoPath, byte[] masterKey)
    {
        _repoPath = repoPath;
        _masterKey = masterKey;
    }

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

        foreach (var dir in plannedDirs)
        {
            Directory.CreateDirectory(dir.TargetPath);
        }

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

            using (var fs = File.Create(file.TargetPath))
            {
                fs.SetLength(file.Size);
            }

            filesToWrite.Add(file);
        }

        await WriteChunksGroupedByPackAsync(db, _repoPath, packSubKey, filesToWrite, ct);

        foreach (var file in filesToWrite)
        {
            ApplyMetadata(file.TargetPath, file.ModifiedAtFileTimeUtc, file.Attributes, file.Sddl, isDirectory: false);
        }

        foreach (var dir in plannedDirs)
        {
            ApplyMetadata(dir.TargetPath, dir.ModifiedAtFileTimeUtc, dir.Attributes, dir.Sddl, isDirectory: true);
        }

        await AuditLogger.RecordAsync(
            db,
            "restore",
            $"Snapshot '{snapshotId}' → '{targetPath}' ({filesToWrite.Count} dosya).",
            ct);
    }

    private static async Task WriteChunksGroupedByPackAsync(
        CatalogDbContext db, string repoPath, byte[] packSubKey, List<PlannedFile> filesToWrite, CancellationToken ct)
    {
        var neededIds = filesToWrite.SelectMany(f => f.ChunkBlobIdsHex).ToHashSet();
        if (neededIds.Count == 0)
        {
            return;
        }

        var blobInfo = await db.Blobs.AsNoTracking()
            .Select(b => new { b.BlobId, b.PackId, b.LenPlain })
            .ToDictionaryAsync(b => b.BlobId, ct);

        var chunkPlan = new List<(string BlobIdHex, string TargetPath, long Offset, string PackId)>();
        foreach (var file in filesToWrite)
        {
            long offset = 0;
            foreach (var blobIdHex in file.ChunkBlobIdsHex)
            {
                if (!blobInfo.TryGetValue(blobIdHex, out var info))
                {
                    throw new InvalidOperationException($"Blob '{blobIdHex}' referenced by '{file.RelativePath}' is missing from the catalog.");
                }

                chunkPlan.Add((blobIdHex, file.TargetPath, offset, info.PackId));
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
                using var fs = new FileStream(item.TargetPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                fs.Seek(item.Offset, SeekOrigin.Begin);
                fs.Write(plaintext);
            }
        }
    }

    private static void ApplyMetadata(string path, long modifiedAtFileTimeUtc, int attributes, string? sddl, bool isDirectory)
    {
        try
        {
            var mtimeUtc = DateTime.FromFileTimeUtc(modifiedAtFileTimeUtc);
            File.SetLastWriteTimeUtc(path, mtimeUtc);

            if (sddl is not null)
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
            }

            File.SetAttributes(path, (FileAttributes)attributes);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
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

    private static async Task WalkAsync(
        BlobStore blobStore, string treeBlobIdHex, string relativePath, string targetRoot,
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

            var childTargetPath = Path.Combine(targetRoot, childRelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (node.Kind == TreeNodeKind.Directory)
            {
                plannedDirs.Add(new PlannedDirectory(childTargetPath, node.ModifiedAtFileTimeUtc, node.Attributes, node.Sddl));

                if (node.SubTreeBlobIdHex is not null)
                {
                    await WalkAsync(blobStore, node.SubTreeBlobIdHex, childRelativePath, targetRoot, selectedRelativePaths, plannedFiles, plannedDirs, ct);
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
