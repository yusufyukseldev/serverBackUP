namespace ServerBackup.Core.Repository;

/// <summary>One header entry describing a blob stored inside a pack file.</summary>
public sealed record BlobEntry(
    byte[] BlobId,
    BlobKind Kind,
    ulong Offset,
    uint LenStored,
    uint LenPlain,
    byte Compression);
