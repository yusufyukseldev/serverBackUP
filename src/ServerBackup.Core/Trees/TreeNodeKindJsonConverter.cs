using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServerBackup.Core.Trees;

internal sealed class TreeNodeKindJsonConverter : JsonConverter<TreeNodeKind>
{
    public override TreeNodeKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "file" => TreeNodeKind.File,
            "dir" => TreeNodeKind.Directory,
            var other => throw new JsonException($"Unknown tree node kind '{other}'."),
        };

    public override void Write(Utf8JsonWriter writer, TreeNodeKind value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value == TreeNodeKind.File ? "file" : "dir");
}
