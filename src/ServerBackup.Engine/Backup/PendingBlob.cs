using ServerBackup.Core.Repository;

namespace ServerBackup.Engine.Backup;

/// <summary>A newly-discovered blob queued for compression (stage 1 of the write pipeline).</summary>
internal sealed record PendingBlob(byte[] BlobId, BlobKind Kind, byte[] Plaintext);

/// <summary>A compressed blob queued for encryption + pack placement (stage 2, single writer).</summary>
internal sealed record PreparedBlob(byte[] BlobId, BlobKind Kind, byte[] CompressedData, byte Codec, int LenPlain);
