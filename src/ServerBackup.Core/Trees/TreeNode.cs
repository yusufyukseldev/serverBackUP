using System.Text.Json.Serialization;

namespace ServerBackup.Core.Trees;

/// <summary>
/// One entry in a tree blob's node list — see docs/format-spec.md "Tree
/// Nesneleri" for the JSON shape. <see cref="ModifiedAtFileTimeUtc"/> is a
/// .NET UTC file time (100ns ticks since 1601-01-01), matching Win32
/// FILETIME semantics used elsewhere for NTFS metadata.
/// </summary>
public sealed record TreeNode(
    [property: JsonPropertyName("n")] string Name,
    [property: JsonPropertyName("t")] TreeNodeKind Kind,
    [property: JsonPropertyName("sz")] long Size,
    [property: JsonPropertyName("mt")] long ModifiedAtFileTimeUtc,
    [property: JsonPropertyName("attr")] int Attributes,
    [property: JsonPropertyName("sddl")] string? Sddl,
    [property: JsonPropertyName("c")] IReadOnlyList<string>? ChunkBlobIdsHex,
    [property: JsonPropertyName("sub")] string? SubTreeBlobIdHex);
