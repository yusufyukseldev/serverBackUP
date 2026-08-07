namespace ServerBackup.Service.Storage;

/// <summary>Thresholds are design-system.md §12.1: %85 warning, %95 danger.</summary>
public static class DiskUsage
{
    public static int? GetUsedPercent(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady || drive.TotalSize == 0)
            {
                return null;
            }

            var used = drive.TotalSize - drive.AvailableFreeSpace;
            return (int)Math.Round(used * 100.0 / drive.TotalSize);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool IsWarning(int percent) => percent is >= 85 and < 95;

    public static bool IsDanger(int percent) => percent >= 95;
}
