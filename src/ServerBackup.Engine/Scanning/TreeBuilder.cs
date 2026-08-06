using ServerBackup.Core.Chunking;
using ServerBackup.Core.Crypto;
using ServerBackup.Core.Trees;

namespace ServerBackup.Engine.Scanning;

/// <summary>
/// Builds a content-addressed <see cref="Tree"/> for a directory by walking
/// it depth-first and chunking file content. This only computes blob ids
/// (pure hashing, no encryption/storage) — actually writing new blobs into
/// packs is the backup engine's job (plan Faz 5), which can skip re-reading
/// files whose metadata is unchanged from the parent snapshot.
/// </summary>
public sealed class TreeBuilder
{
    private readonly byte[] _idKey;
    private readonly ScanFilter? _filter;

    public ISourceProvider Source { get; }

    public FastCdcChunker Chunker { get; }

    public TreeBuilder(ISourceProvider source, FastCdcChunker chunker, byte[] idKey, ScanFilter? filter = null)
    {
        Source = source;
        Chunker = chunker;
        _idKey = idKey;
        _filter = filter;
    }

    public Tree BuildTree(string directoryPath)
    {
        var nodes = new List<TreeNode>();

        foreach (var child in Source.EnumerateChildren(directoryPath).OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            if (_filter?.IsExcluded(child) == true)
            {
                continue;
            }

            nodes.Add(child.IsDirectory ? BuildDirectoryNode(child) : BuildFileNode(child));
        }

        return new Tree(nodes);
    }

    private TreeNode BuildDirectoryNode(SourceEntry entry)
    {
        // Reparse points/junctions are stored as a link placeholder (SubTreeBlobIdHex
        // stays null) rather than followed — see docs/format-spec.md.
        string? subTreeBlobIdHex = null;
        if (!entry.IsReparsePoint)
        {
            var subTree = BuildTree(entry.FullPath);
            subTreeBlobIdHex = Convert.ToHexStringLower(subTree.ComputeBlobId(_idKey));
        }

        return new TreeNode(
            Name: entry.Name,
            Kind: TreeNodeKind.Directory,
            Size: 0,
            ModifiedAtFileTimeUtc: entry.LastWriteTimeUtc.ToFileTimeUtc(),
            Attributes: (int)entry.Attributes,
            Sddl: Source.TryGetSddl(entry.FullPath),
            ChunkBlobIdsHex: null,
            SubTreeBlobIdHex: subTreeBlobIdHex);
    }

    private TreeNode BuildFileNode(SourceEntry entry)
    {
        var chunkIds = new List<string>();
        using (var stream = Source.OpenRead(entry.FullPath))
        {
            foreach (var chunk in Chunker.Chunk(stream))
            {
                chunkIds.Add(Convert.ToHexStringLower(BlobId.Compute(_idKey, chunk)));
            }
        }

        return new TreeNode(
            Name: entry.Name,
            Kind: TreeNodeKind.File,
            Size: entry.Size,
            ModifiedAtFileTimeUtc: entry.LastWriteTimeUtc.ToFileTimeUtc(),
            Attributes: (int)entry.Attributes,
            Sddl: Source.TryGetSddl(entry.FullPath),
            ChunkBlobIdsHex: chunkIds,
            SubTreeBlobIdHex: null);
    }
}
