namespace ServerBackup.Engine.Vss;

/// <summary>
/// Pure path-rewriting logic (real path ↔ shadow-copy device path), factored
/// out of <see cref="VssSnapshotSession"/> so it's unit-testable without a
/// real (elevated) VSS session.
/// </summary>
public sealed class VolumeMapper
{
    private readonly Dictionary<string, string> _deviceObjectByVolumeRoot;

    public VolumeMapper(IReadOnlyDictionary<string, string> deviceObjectByVolumeRoot) =>
        _deviceObjectByVolumeRoot = new Dictionary<string, string>(deviceObjectByVolumeRoot, StringComparer.OrdinalIgnoreCase);

    /// <summary>e.g. "D:\Data\file.txt" → "\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopyN\Data\file.txt".</summary>
    public string MapPath(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root) || !_deviceObjectByVolumeRoot.TryGetValue(root, out var deviceObject))
        {
            return fullPath;
        }

        var relative = fullPath[root.Length..];
        var separator = deviceObject.EndsWith('\\') ? "" : "\\";
        return deviceObject + separator + relative;
    }

    /// <summary>The inverse of <see cref="MapPath"/>.</summary>
    public string UnmapPath(string snapshotPath)
    {
        foreach (var (volumeRoot, deviceObject) in _deviceObjectByVolumeRoot)
        {
            if (snapshotPath.StartsWith(deviceObject, StringComparison.OrdinalIgnoreCase))
            {
                var relative = snapshotPath[deviceObject.Length..].TrimStart('\\');
                return Path.Combine(volumeRoot, relative);
            }
        }

        return snapshotPath;
    }
}
