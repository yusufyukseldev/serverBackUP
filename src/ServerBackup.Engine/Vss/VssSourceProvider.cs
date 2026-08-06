using ServerBackup.Engine.Scanning;

namespace ServerBackup.Engine.Vss;

/// <summary>
/// Decorates an <see cref="ISourceProvider"/> so every read happens against
/// the VSS shadow copy instead of the live volume, while every
/// <see cref="SourceEntry.FullPath"/> handed back is rewritten to the
/// original (non-VSS) path — the rest of the pipeline (tree building,
/// dedup, restore) never needs to know VSS was involved.
/// </summary>
public sealed class VssSourceProvider(ISourceProvider inner, VssSnapshotSession session) : ISourceProvider
{
    public SourceEntry GetEntry(string path)
    {
        var entry = inner.GetEntry(session.MapPath(path));
        return entry with { FullPath = path };
    }

    public IEnumerable<SourceEntry> EnumerateChildren(string directoryPath)
    {
        foreach (var child in inner.EnumerateChildren(session.MapPath(directoryPath)))
        {
            yield return child with { FullPath = session.UnmapPath(child.FullPath) };
        }
    }

    public Stream OpenRead(string filePath) => inner.OpenRead(session.MapPath(filePath));

    public string? TryGetSddl(string path) => inner.TryGetSddl(session.MapPath(path));
}
