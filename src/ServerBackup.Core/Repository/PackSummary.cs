namespace ServerBackup.Core.Repository;

/// <summary>Result of closing a <see cref="PackWriter"/>: what to record in the catalog.</summary>
public sealed record PackSummary(
    IReadOnlyList<BlobEntry> Entries,
    long TotalLengthBytes,
    byte[] Sha256);
