namespace ServerBackup.Core.Crypto;

/// <summary>
/// Persisted, password-wrapped form of a repository master key
/// (keys/&lt;KeyId&gt;.json — see docs/format-spec.md). A repository can have
/// several of these, one per password/recovery key, all wrapping the same
/// master key. Values are base64 so the record serializes directly to JSON.
/// </summary>
public sealed record KeyFileV1(
    int FormatVersion,
    string KeyId,
    string Salt,
    int Argon2MemoryKiB,
    int Argon2Iterations,
    int Argon2Parallelism,
    string Nonce,
    string WrappedMasterKey)
{
    public const int CurrentFormatVersion = 1;
}
