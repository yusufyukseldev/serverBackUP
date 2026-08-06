using System.Security.Cryptography;

namespace ServerBackup.Core.Crypto;

/// <summary>
/// Wraps and unwraps a repository master key with a password-derived
/// key-encryption key (KEK). Produces/consumes <see cref="KeyFileV1"/>
/// records; actual file I/O to keys/&lt;id&gt;.json is a repository (Faz 3)
/// concern, not this class's.
/// </summary>
public static class MasterKeyFile
{
    /// <summary>Generates a brand new random master key and wraps it with the given password.</summary>
    public static (byte[] MasterKey, KeyFileV1 KeyFile) CreateNew(ReadOnlySpan<byte> password)
    {
        var masterKey = RandomNumberGenerator.GetBytes(AeadCipher.KeySizeBytes);
        var keyFile = Wrap(masterKey, password);
        return (masterKey, keyFile);
    }

    /// <summary>Wraps an existing master key with a (possibly additional) password, producing a new key file.</summary>
    public static KeyFileV1 Wrap(ReadOnlySpan<byte> masterKey, ReadOnlySpan<byte> password)
    {
        var salt = RandomNumberGenerator.GetBytes(Argon2idParameters.SaltSizeBytes);
        var kek = Argon2idKeyDerivation.DeriveKey(password, salt);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(AeadCipher.NonceSizeBytes);
            var wrapped = AeadCipher.Seal(kek, nonce, masterKey);
            var keyId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

            return new KeyFileV1(
                FormatVersion: KeyFileV1.CurrentFormatVersion,
                KeyId: keyId,
                Salt: Convert.ToBase64String(salt),
                Argon2MemoryKiB: Argon2idParameters.MemorySizeKiB,
                Argon2Iterations: Argon2idParameters.Iterations,
                Argon2Parallelism: Argon2idParameters.DegreeOfParallelism,
                Nonce: Convert.ToBase64String(nonce),
                WrappedMasterKey: Convert.ToBase64String(wrapped));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    /// <summary>
    /// Recovers the master key from a key file and password.
    /// Throws <see cref="AuthenticationTagMismatchException"/> for a wrong password.
    /// </summary>
    public static byte[] Unwrap(KeyFileV1 keyFile, ReadOnlySpan<byte> password)
    {
        if (keyFile.FormatVersion != KeyFileV1.CurrentFormatVersion)
        {
            throw new NotSupportedException(
                $"Unsupported key file format version {keyFile.FormatVersion} (expected {KeyFileV1.CurrentFormatVersion}).");
        }

        var salt = Convert.FromBase64String(keyFile.Salt);
        var kek = Argon2idKeyDerivation.DeriveKey(password, salt);
        try
        {
            var nonce = Convert.FromBase64String(keyFile.Nonce);
            var wrapped = Convert.FromBase64String(keyFile.WrappedMasterKey);
            return AeadCipher.Open(kek, nonce, wrapped);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }
}
