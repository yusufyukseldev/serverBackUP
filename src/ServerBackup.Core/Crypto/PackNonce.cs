using System.Buffers.Binary;

namespace ServerBackup.Core.Crypto;

/// <summary>
/// Builds the deterministic per-blob nonce used inside a pack file:
/// 4 zero bytes followed by the big-endian blob index. Because every pack
/// derives its own key from a random packSalt (see <see cref="SubKeys.DerivePackKey"/>)
/// and is never appended to after being closed, a given (key, nonce) pair can
/// never repeat — see docs/format-spec.md "Nonce Yönetimi".
/// </summary>
public static class PackNonce
{
    /// <summary>Reserved nonce for encrypting the pack's own header (see format-spec.md).</summary>
    public const ulong HeaderBlobIndex = ulong.MaxValue;

    public static byte[] ForBlobIndex(ulong blobIndex)
    {
        var nonce = new byte[AeadCipher.NonceSizeBytes];
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), blobIndex);
        return nonce;
    }
}
