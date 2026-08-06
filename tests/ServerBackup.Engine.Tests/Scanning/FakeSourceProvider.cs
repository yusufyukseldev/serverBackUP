using ServerBackup.Engine.Scanning;

namespace ServerBackup.Engine.Tests.Scanning;

/// <summary>
/// Entirely in-memory <see cref="ISourceProvider"/> for building synthetic
/// trees in tests (deep paths, unicode names, reparse points, controlled
/// SDDL) without touching the real filesystem — see plan Faz 4's test list.
/// </summary>
internal sealed class FakeSourceProvider : ISourceProvider
{
    private sealed record Entry(
        string Path,
        string Name,
        bool IsDirectory,
        byte[]? Content,
        DateTime LastWriteTimeUtc,
        FileAttributes Attributes,
        bool IsReparsePoint,
        string? Sddl);

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public void AddDirectory(
        string path,
        DateTime? mtime = null,
        bool isReparsePoint = false,
        string? sddl = "D:dir-sddl")
    {
        var normalized = Normalize(path);
        _entries[normalized] = new Entry(
            normalized,
            Name: NameOf(normalized),
            IsDirectory: true,
            Content: null,
            LastWriteTimeUtc: mtime ?? DateTime.UtcNow,
            Attributes: isReparsePoint ? FileAttributes.Directory | FileAttributes.ReparsePoint : FileAttributes.Directory,
            IsReparsePoint: isReparsePoint,
            Sddl: sddl);
    }

    public void AddFile(string path, byte[] content, DateTime? mtime = null, string? sddl = "D:file-sddl")
    {
        var normalized = Normalize(path);
        _entries[normalized] = new Entry(
            normalized,
            Name: NameOf(normalized),
            IsDirectory: false,
            Content: content,
            LastWriteTimeUtc: mtime ?? DateTime.UtcNow,
            Attributes: FileAttributes.Normal,
            IsReparsePoint: false,
            Sddl: sddl);
    }

    public SourceEntry GetEntry(string path) => ToSourceEntry(_entries[Normalize(path)]);

    public IEnumerable<SourceEntry> EnumerateChildren(string directoryPath)
    {
        var prefix = Normalize(directoryPath) + "/";
        foreach (var entry in _entries.Values)
        {
            if (!entry.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remainder = entry.Path[prefix.Length..];
            if (!remainder.Contains('/'))
            {
                yield return ToSourceEntry(entry);
            }
        }
    }

    public Stream OpenRead(string filePath) => new MemoryStream(_entries[Normalize(filePath)].Content ?? [], writable: false);

    public string? TryGetSddl(string path) => _entries[Normalize(path)].Sddl;

    private static SourceEntry ToSourceEntry(Entry entry) => new(
        FullPath: entry.Path,
        Name: entry.Name,
        IsDirectory: entry.IsDirectory,
        Size: entry.Content?.LongLength ?? 0,
        LastWriteTimeUtc: entry.LastWriteTimeUtc,
        Attributes: entry.Attributes,
        IsReparsePoint: entry.IsReparsePoint);

    private static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/');

    private static string NameOf(string normalizedPath)
    {
        var idx = normalizedPath.LastIndexOf('/');
        return idx < 0 ? normalizedPath : normalizedPath[(idx + 1)..];
    }
}
