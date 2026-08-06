namespace ServerBackup.Engine.Restore;

internal sealed record PlannedFile(
    string RelativePath,
    string TargetPath,
    long Size,
    long ModifiedAtFileTimeUtc,
    int Attributes,
    string? Sddl,
    IReadOnlyList<string> ChunkBlobIdsHex);

internal sealed record PlannedDirectory(
    string TargetPath,
    long ModifiedAtFileTimeUtc,
    int Attributes,
    string? Sddl);
