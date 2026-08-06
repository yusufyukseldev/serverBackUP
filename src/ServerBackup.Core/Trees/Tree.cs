using System.Text.Json;
using System.Text.Json.Serialization;
using ServerBackup.Core.Crypto;

namespace ServerBackup.Core.Trees;

/// <summary>
/// A directory listing, content-addressed like any other blob (see
/// docs/format-spec.md "Tree Nesneleri"). Two directories with identical
/// contents serialize to identical bytes and therefore get the same blob id
/// — dedup applies to unchanged subtrees, not just file chunks.
/// </summary>
public sealed record Tree(IReadOnlyList<TreeNode> Nodes)
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    /// <summary>Canonical serialized form — the bytes that get content-addressed and stored.</summary>
    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(new TreeDto(Nodes), JsonOptions);

    public static Tree Deserialize(ReadOnlySpan<byte> json)
    {
        var dto = JsonSerializer.Deserialize<TreeDto>(json, JsonOptions)
            ?? throw new InvalidDataException("Tree JSON deserialized to null.");
        return new Tree(dto.Nodes);
    }

    /// <summary>The content-addressed id this tree would have in the repository (see <see cref="BlobId"/>).</summary>
    public byte[] ComputeBlobId(ReadOnlySpan<byte> idKey) => BlobId.Compute(idKey, Serialize());

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new TreeNodeKindJsonConverter());
        return options;
    }

    private sealed record TreeDto([property: JsonPropertyName("nodes")] IReadOnlyList<TreeNode> Nodes);
}
