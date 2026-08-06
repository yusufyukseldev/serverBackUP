namespace ServerBackup.Engine.Scanning;

/// <summary>
/// Flat, pre-order walk of a directory tree through an <see cref="ISourceProvider"/>.
/// Reparse points/junctions are reported but not descended into (default:
/// don't follow — see docs/format-spec.md and plan Faz 4).
/// </summary>
public sealed class FileSystemScanner(ISourceProvider source)
{
    public IEnumerable<SourceEntry> Scan(string rootPath, ScanFilter? filter = null)
    {
        var root = source.GetEntry(rootPath);
        return ScanRecursive(root, filter);
    }

    private IEnumerable<SourceEntry> ScanRecursive(SourceEntry entry, ScanFilter? filter)
    {
        if (filter?.IsExcluded(entry) == true)
        {
            yield break;
        }

        yield return entry;

        if (!entry.IsDirectory || entry.IsReparsePoint)
        {
            yield break;
        }

        foreach (var child in source.EnumerateChildren(entry.FullPath))
        {
            foreach (var descendant in ScanRecursive(child, filter))
            {
                yield return descendant;
            }
        }
    }
}
