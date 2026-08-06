namespace ServerBackup.Data.Entities;

/// <summary>
/// One catalog row per content-addressed blob. <see cref="BlobId"/> (hex of
/// the HMAC-SHA256 blob id) is the primary key — it is unique by
/// construction, since a blob is only ever written once (dedup).
/// </summary>
public sealed class BlobEntity
{
    public required string BlobId { get; set; }
    public required string PackId { get; set; }
    public required byte Kind { get; set; }
    public required long Offset { get; set; }
    public required int LenStored { get; set; }
    public required int LenPlain { get; set; }
    public required byte Compression { get; set; }

    public PackEntity Pack { get; set; } = null!;
}
