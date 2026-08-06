namespace ServerBackup.Engine.Scanning;

/// <summary>One filesystem entry as reported by an <see cref="ISourceProvider"/>.</summary>
public sealed record SourceEntry(
    string FullPath,
    string Name,
    bool IsDirectory,
    long Size,
    DateTime LastWriteTimeUtc,
    FileAttributes Attributes,
    bool IsReparsePoint);
