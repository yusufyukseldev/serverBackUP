using System.Security.Cryptography;

namespace ServerBackup.Core.Repository;

/// <summary>
/// Pack files are named after a random 128-bit id and stored under
/// data/&lt;first-2-hex-chars&gt;/&lt;id&gt;.pack, so no single directory ends up
/// with too many entries — see docs/format-spec.md.
/// </summary>
public static class PackId
{
    public const int SizeBytes = 16;

    public static string NewId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SizeBytes));

    public static string RelativePath(string packId) =>
        Path.Combine("data", packId[..2], packId + ".pack");
}
