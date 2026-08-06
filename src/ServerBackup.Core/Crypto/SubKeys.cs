using System.Security.Cryptography;
using System.Text;

namespace ServerBackup.Core.Crypto;

/// <summary>
/// Derives purpose-specific sub-keys from the repository master key via
/// HKDF-SHA256, so a single master key never gets reused directly for
/// different cryptographic purposes. Info strings are fixed by
/// docs/format-spec.md.
/// </summary>
public static class SubKeys
{
    public const string ChunkIdInfo = "chunk-id-v1";
    public const string PackKeyInfo = "pack-key-v1";
    public const string GearSeedInfo = "gear-seed-v1";
    public const string MetaInfo = "meta-v1";

    public static byte[] Derive(ReadOnlySpan<byte> masterKey, string info, int outputLengthBytes = 32)
    {
        var infoBytes = Encoding.ASCII.GetBytes(info);
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey.ToArray(), outputLengthBytes, salt: null, info: infoBytes);
    }

    /// <summary>
    /// Derives the per-pack encryption key from the pack sub-key, salted with
    /// that pack's random packSalt. Every pack therefore gets an independent
    /// key, which is what makes the deterministic per-blob nonce counter in
    /// <see cref="PackNonce"/> safe.
    /// </summary>
    public static byte[] DerivePackKey(ReadOnlySpan<byte> packSubKey, ReadOnlySpan<byte> packSalt)
    {
        var info = Encoding.ASCII.GetBytes("pack");
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, packSubKey.ToArray(), AeadCipher.KeySizeBytes, salt: packSalt.ToArray(), info: info);
    }
}
