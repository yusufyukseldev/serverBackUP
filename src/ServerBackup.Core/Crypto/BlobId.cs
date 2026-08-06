using System.Security.Cryptography;

namespace ServerBackup.Core.Crypto;

/// <summary>
/// Computes content-addressed blob identifiers as HMAC-SHA256(K_id, plaintext),
/// not a plain hash. Keying the identifier prevents an attacker without the
/// repository key from confirming whether a known file exists in the
/// repository by comparing hashes (chunking/fingerprinting attack).
/// </summary>
public static class BlobId
{
    public const int SizeBytes = 32;

    public static byte[] Compute(ReadOnlySpan<byte> idKey, ReadOnlySpan<byte> data)
    {
        Span<byte> result = stackalloc byte[SizeBytes];
        HMACSHA256.HashData(idKey, data, result);
        return result.ToArray();
    }

    public static string ToHex(ReadOnlySpan<byte> blobId) => Convert.ToHexStringLower(blobId);
}
