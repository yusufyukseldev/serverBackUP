namespace ServerBackup.Data.Entities;

public sealed class PackEntity
{
    public required string PackId { get; set; }
    public required byte[] Sha256 { get; set; }
    public required long SizeBytes { get; set; }
    public required DateTimeOffset CreatedAtUtc { get; set; }

    public List<BlobEntity> Blobs { get; set; } = [];
}
