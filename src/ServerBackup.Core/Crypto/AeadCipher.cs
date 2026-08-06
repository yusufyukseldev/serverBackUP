using System.Security.Cryptography;

namespace ServerBackup.Core.Crypto;

/// <summary>
/// AES-256-GCM authenticated encryption used for every ciphertext in the
/// repository (wrapped master keys, pack blobs, encrypted pack headers).
/// Callers are responsible for nonce uniqueness — see docs/format-spec.md
/// "Nonce Yönetimi" and <see cref="PackNonce"/>.
/// </summary>
public static class AeadCipher
{
    public const int KeySizeBytes = 32;
    public const int NonceSizeBytes = 12;
    public const int TagSizeBytes = 16;

    public static void Encrypt(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> plaintext,
        Span<byte> ciphertext,
        Span<byte> tag,
        ReadOnlySpan<byte> associatedData = default)
    {
        using var aesGcm = new AesGcm(key, TagSizeBytes);
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
    }

    /// <summary>
    /// Throws <see cref="AuthenticationTagMismatchException"/> if the tag does
    /// not match — this is how a wrong key or tampered ciphertext is detected.
    /// </summary>
    public static void Decrypt(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag,
        Span<byte> plaintext,
        ReadOnlySpan<byte> associatedData = default)
    {
        using var aesGcm = new AesGcm(key, TagSizeBytes);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
    }

    /// <summary>Encrypts and returns ciphertext with the tag appended.</summary>
    public static byte[] Seal(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> associatedData = default)
    {
        var result = new byte[plaintext.Length + TagSizeBytes];
        Encrypt(key, nonce, plaintext, result.AsSpan(0, plaintext.Length), result.AsSpan(plaintext.Length), associatedData);
        return result;
    }

    /// <summary>Decrypts data produced by <see cref="Seal"/> (ciphertext with tag appended).</summary>
    public static byte[] Open(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> sealedData,
        ReadOnlySpan<byte> associatedData = default)
    {
        if (sealedData.Length < TagSizeBytes)
        {
            throw new ArgumentException("Sealed data is too short to contain an authentication tag.", nameof(sealedData));
        }

        var ciphertext = sealedData[..^TagSizeBytes];
        var tag = sealedData[^TagSizeBytes..];
        var plaintext = new byte[ciphertext.Length];
        Decrypt(key, nonce, ciphertext, tag, plaintext, associatedData);
        return plaintext;
    }
}
