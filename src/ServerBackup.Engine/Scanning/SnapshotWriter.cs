using ServerBackup.Core.Crypto;
using ServerBackup.Core.Trees;

namespace ServerBackup.Engine.Scanning;

/// <summary>
/// Combines one or more source paths (a backup plan can name several disks
/// or folders) into a single root <see cref="Tree"/> for one snapshot.
/// </summary>
public sealed class SnapshotWriter(TreeBuilder treeBuilder, byte[] idKey)
{
    public SnapshotDraft BuildSnapshot(IReadOnlyList<string> sourcePaths, DateTimeOffset startedAtUtc)
    {
        if (sourcePaths.Count == 0)
        {
            throw new ArgumentException("A snapshot needs at least one source path.", nameof(sourcePaths));
        }

        var rootNodes = sourcePaths.Select(BuildRootNode).ToList();
        var rootTree = new Tree(rootNodes);
        var rootTreeBlobId = rootTree.ComputeBlobId(idKey);

        return new SnapshotDraft(sourcePaths, rootTree, rootTreeBlobId, startedAtUtc);
    }

    private TreeNode BuildRootNode(string path)
    {
        var source = treeBuilder.Source;
        var entry = source.GetEntry(path);

        if (entry.IsDirectory)
        {
            var subTree = treeBuilder.BuildTree(path);
            return new TreeNode(
                Name: entry.Name,
                Kind: TreeNodeKind.Directory,
                Size: 0,
                ModifiedAtFileTimeUtc: entry.LastWriteTimeUtc.ToFileTimeUtc(),
                Attributes: (int)entry.Attributes,
                Sddl: source.TryGetSddl(path),
                ChunkBlobIdsHex: null,
                SubTreeBlobIdHex: Convert.ToHexStringLower(subTree.ComputeBlobId(idKey)));
        }

        var chunkIds = new List<string>();
        using (var stream = source.OpenRead(path))
        {
            foreach (var chunk in treeBuilder.Chunker.Chunk(stream))
            {
                chunkIds.Add(Convert.ToHexStringLower(BlobId.Compute(idKey, chunk)));
            }
        }

        return new TreeNode(
            Name: entry.Name,
            Kind: TreeNodeKind.File,
            Size: entry.Size,
            ModifiedAtFileTimeUtc: entry.LastWriteTimeUtc.ToFileTimeUtc(),
            Attributes: (int)entry.Attributes,
            Sddl: source.TryGetSddl(path),
            ChunkBlobIdsHex: chunkIds,
            SubTreeBlobIdHex: null);
    }
}
