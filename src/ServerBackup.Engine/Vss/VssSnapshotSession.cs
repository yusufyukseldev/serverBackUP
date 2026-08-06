using System.Security.Principal;
using Alphaleonis.Win32.Vss;

namespace ServerBackup.Engine.Vss;

/// <summary>
/// Wraps a single VSS shadow copy set spanning every distinct volume among a
/// backup's source paths, so all volumes are captured at the same instant —
/// see docs/format-spec.md and plan Faz 7. Requires an elevated process;
/// callers should catch <see cref="InvalidOperationException"/> and fall
/// back to reading files directly (--no-vss) if VSS isn't available.
/// </summary>
public sealed class VssSnapshotSession : IDisposable
{
    private readonly IVssBackupComponents _backup;
    private readonly VolumeMapper _mapper;
    private readonly Guid _snapshotSetId;
    private bool _disposed;

    private VssSnapshotSession(IVssBackupComponents backup, VolumeMapper mapper, Guid snapshotSetId)
    {
        _backup = backup;
        _mapper = mapper;
        _snapshotSetId = snapshotSetId;
    }

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Every volume root (e.g. "C:\") referenced across the given source paths, snapshotted together.</summary>
    public static VssSnapshotSession Create(IReadOnlyList<string> sourcePaths)
    {
        if (!IsElevated())
        {
            throw new InvalidOperationException(
                "VSS snapshot creation requires an elevated (Administrator) process. Retry elevated, or pass --no-vss.");
        }

        var volumeRoots = sourcePaths
            .Select(p => Path.GetPathRoot(Path.GetFullPath(p)))
            .Where(root => !string.IsNullOrEmpty(root))
            .Select(root => root!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (volumeRoots.Count == 0)
        {
            throw new InvalidOperationException("Could not determine a volume root for any of the given source paths.");
        }

        IVssFactory vss = VssFactoryProvider.Default.GetVssFactory();
        var backup = vss.CreateVssBackupComponents();

        try
        {
            backup.InitializeForBackup(null);
            backup.SetContext(VssSnapshotContext.Backup);
            backup.GatherWriterMetadata();
            backup.FreeWriterMetadata();

            var setId = backup.StartSnapshotSet();
            var snapshotIdByVolume = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

            foreach (var volumeRoot in volumeRoots)
            {
                if (!backup.IsVolumeSupported(volumeRoot))
                {
                    throw new NotSupportedException($"Volume '{volumeRoot}' does not support shadow copies.");
                }

                snapshotIdByVolume[volumeRoot] = backup.AddToSnapshotSet(volumeRoot);
            }

            backup.SetBackupState(selectComponents: false, backupBootableSystemState: false, backupType: VssBackupType.Full, partialFileSupport: false);
            backup.PrepareForBackup();
            backup.DoSnapshotSet(); // all volumes in the set are frozen at the same instant here

            var deviceObjectByVolumeRoot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (volumeRoot, snapshotId) in snapshotIdByVolume)
            {
                var props = backup.GetSnapshotProperties(snapshotId);
                deviceObjectByVolumeRoot[volumeRoot] = props.SnapshotDeviceObject;
            }

            return new VssSnapshotSession(backup, new VolumeMapper(deviceObjectByVolumeRoot), setId);
        }
        catch
        {
            backup.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Maps a real path to its shadow-copy equivalent. Paths outside any
    /// snapshotted volume are returned unchanged (defensive — should not
    /// happen for paths under the original source list).
    /// </summary>
    public string MapPath(string fullPath) => _mapper.MapPath(fullPath);

    public string UnmapPath(string snapshotPath) => _mapper.UnmapPath(snapshotPath);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _backup.DeleteSnapshotSet(_snapshotSetId, forceDelete: false);
        }
        catch (VssException)
        {
            // Best-effort cleanup — a failed delete leaves an orphaned shadow
            // copy (recoverable via vssadmin), not a data-safety issue.
        }

        try
        {
            _backup.BackupComplete();
        }
        catch (VssBadStateException)
        {
            // Documented as benign on some OS versions by the AlphaVSS samples.
        }
        finally
        {
            _backup.Dispose();
        }
    }
}
