namespace ServerBackup.Core.Repository;

/// <summary>
/// Plaintext repository config (repo root/config.json). Never contains key
/// material — just enough to know how to read the repository at all, before
/// any password is supplied. See docs/format-spec.md.
/// </summary>
public sealed record RepositoryConfig(
    int FormatVersion,
    string RepositoryId,
    DateTimeOffset CreatedAtUtc,
    int? ImmutabilityWindowDays = null,
    bool AppendOnly = false)
{
    public const int CurrentFormatVersion = 1;

    public static RepositoryConfig CreateNew(int? immutabilityWindowDays = null, bool appendOnly = false) => new(
        FormatVersion: CurrentFormatVersion,
        RepositoryId: Guid.NewGuid().ToString("n"),
        CreatedAtUtc: DateTimeOffset.UtcNow,
        ImmutabilityWindowDays: immutabilityWindowDays,
        AppendOnly: appendOnly);
}
