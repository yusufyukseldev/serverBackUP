using ServerBackup.Core.Trees;
using ServerBackup.Engine.Repository;

namespace ServerBackup.Engine.Backup;

/// <summary>
/// Flattens a previous snapshot's tree into relativePath → <see cref="TreeNode"/>
/// so the backup engine can skip re-reading/re-chunking files whose
/// (size, mtime, attributes) haven't changed — this is where most of an
/// incremental backup's speed comes from.
/// </summary>
public sealed class ParentSnapshotCache
{
    public IReadOnlyDictionary<string, TreeNode> FilesByRelativePath { get; }

    private ParentSnapshotCache(Dictionary<string, TreeNode> map) => FilesByRelativePath = map;

    public static async Task<ParentSnapshotCache> LoadAsync(
        BlobStore blobStore, string rootTreeBlobIdHex, CancellationToken ct = default)
    {
        var map = new Dictionary<string, TreeNode>();
        await WalkAsync(blobStore, rootTreeBlobIdHex, prefix: "", map, ct);
        return new ParentSnapshotCache(map);
    }

    private static async Task WalkAsync(
        BlobStore blobStore, string treeBlobIdHex, string prefix, Dictionary<string, TreeNode> map, CancellationToken ct)
    {
        var bytes = await blobStore.ReadBlobAsync(treeBlobIdHex, ct);
        var tree = Tree.Deserialize(bytes);

        foreach (var node in tree.Nodes)
        {
            var relativePath = prefix.Length == 0 ? node.Name : $"{prefix}/{node.Name}";
            map[relativePath] = node;

            if (node.Kind == TreeNodeKind.Directory && node.SubTreeBlobIdHex is not null)
            {
                await WalkAsync(blobStore, node.SubTreeBlobIdHex, relativePath, map, ct);
            }
        }
    }

    public bool TryGetUnchangedFile(string relativePath, long size, long modifiedAtFileTimeUtc, int attributes, out TreeNode node)
    {
        if (FilesByRelativePath.TryGetValue(relativePath, out var candidate)
            && candidate.Kind == TreeNodeKind.File
            && candidate.Size == size
            && candidate.ModifiedAtFileTimeUtc == modifiedAtFileTimeUtc
            && candidate.Attributes == attributes)
        {
            node = candidate;
            return true;
        }

        node = null!;
        return false;
    }
}
