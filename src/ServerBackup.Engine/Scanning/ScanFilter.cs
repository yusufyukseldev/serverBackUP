using Microsoft.Extensions.FileSystemGlobbing;

namespace ServerBackup.Engine.Scanning;

/// <summary>
/// Decides which files/directories a scan should skip: a fixed list of
/// always-skip Windows system artifacts, plus user-supplied include/exclude
/// glob patterns (evaluated relative to the scan root).
/// </summary>
public sealed class ScanFilter
{
    private static readonly string[] AlwaysExcludedNames =
    [
        "pagefile.sys",
        "hiberfil.sys",
        "swapfile.sys",
        "System Volume Information",
        "$RECYCLE.BIN",
    ];

    private readonly Matcher? _matcher;
    private readonly string _rootPath;

    public ScanFilter(string rootPath, IReadOnlyList<string>? includeGlobs = null, IReadOnlyList<string>? excludeGlobs = null)
    {
        _rootPath = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var hasPatterns = (includeGlobs is { Count: > 0 }) || (excludeGlobs is { Count: > 0 });
        if (!hasPatterns)
        {
            _matcher = null;
            return;
        }

        _matcher = new Matcher();
        foreach (var pattern in includeGlobs ?? ["**/*"])
        {
            _matcher.AddInclude(pattern);
        }

        foreach (var pattern in excludeGlobs ?? [])
        {
            _matcher.AddExclude(pattern);
        }
    }

    /// <summary>True if this entry (and, for directories, its entire subtree) must be skipped.</summary>
    public bool IsExcluded(SourceEntry entry)
    {
        if (AlwaysExcludedNames.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (_matcher is null)
        {
            return false;
        }

        var relative = Path.GetRelativePath(_rootPath, entry.FullPath).Replace('\\', '/');
        var result = _matcher.Match(relative);
        return !result.HasMatches;
    }
}
