using Konscious.Security.Cryptography;

namespace ServerBackup.Core.Crypto;

/// <summary>
/// Production Argon2id parameters for deriving a key-encryption key (KEK) from a
/// user password. These are the fixed v1 parameters documented in
/// docs/format-spec.md — changing them requires a format version bump.
/// </summary>
public static class Argon2idParameters
{
    public const int SaltSizeBytes = 16;
    public const int OutputSizeBytes = 32;
    public const int MemorySizeKiB = 64 * 1024;
    public const int Iterations = 3;
    public const int DegreeOfParallelism = 4;
}

public static class Argon2idKeyDerivation
{
    /// <summary>
    /// Derives a 32-byte key-encryption key from a password using the fixed
    /// production Argon2id parameters. Use this for all repository key wrapping.
    /// </summary>
    public static byte[] DeriveKey(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt)
    {
        if (salt.Length != Argon2idParameters.SaltSizeBytes)
        {
            throw new ArgumentException(
                $"Salt must be {Argon2idParameters.SaltSizeBytes} bytes, got {salt.Length}.", nameof(salt));
        }

        return DeriveKeyCore(
            password,
            salt,
            secret: default,
            associatedData: default,
            memorySizeKiB: Argon2idParameters.MemorySizeKiB,
            iterations: Argon2idParameters.Iterations,
            degreeOfParallelism: Argon2idParameters.DegreeOfParallelism,
            outputSizeBytes: Argon2idParameters.OutputSizeBytes);
    }

    /// <summary>
    /// Full-parameter Argon2id entry point. Exists so the algorithm can be
    /// verified against published test vectors (e.g. RFC 9106 §5.3), which use
    /// parameters unrelated to production. Do not use this for repository keys —
    /// use <see cref="DeriveKey"/> instead.
    /// </summary>
    internal static byte[] DeriveKeyCore(
        ReadOnlySpan<byte> password,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> secret,
        ReadOnlySpan<byte> associatedData,
        int memorySizeKiB,
        int iterations,
        int degreeOfParallelism,
        int outputSizeBytes)
    {
        using var argon2 = new Argon2id(password.ToArray())
        {
            Salt = salt.ToArray(),
            DegreeOfParallelism = degreeOfParallelism,
            Iterations = iterations,
            MemorySize = memorySizeKiB,
            KnownSecret = secret.IsEmpty ? null : secret.ToArray(),
            AssociatedData = associatedData.IsEmpty ? null : associatedData.ToArray(),
        };

        return argon2.GetBytes(outputSizeBytes);
    }
}
