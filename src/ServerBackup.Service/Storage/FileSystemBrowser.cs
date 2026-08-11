namespace ServerBackup.Service.Storage;

/// <summary>
/// Backs the source-path picker. Every call is best-effort: a directory the
/// panel's account cannot read yields an empty list rather than an exception,
/// because browsing a whole volume always crosses paths like
/// <c>C:\$Recycle.Bin</c> or another user's profile.
/// </summary>
public static class FileSystemBrowser
{
    public sealed record DriveItem(string RootPath, string Label, long TotalBytes, long FreeBytes);

    public static IReadOnlyList<DriveItem> GetFixedDrives()
    {
        var drives = new List<DriveItem>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                {
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? drive.Name
                    : $"{drive.Name} ({drive.VolumeLabel})";

                drives.Add(new DriveItem(drive.RootDirectory.FullName, label, drive.TotalSize, drive.AvailableFreeSpace));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A drive that disappears mid-enumeration is simply not offered.
            }
        }

        return drives;
    }

    public static IReadOnlyList<string> GetSubdirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path)
                .Where(IsBrowsable)
                .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>Hidden+system directories are OS bookkeeping, not backup sources the user picks by hand.</summary>
    private static bool IsBrowsable(string directoryPath)
    {
        try
        {
            var attributes = File.GetAttributes(directoryPath);
            return !(attributes.HasFlag(FileAttributes.Hidden) && attributes.HasFlag(FileAttributes.System));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool DirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Ancestor chain from the volume root down to <paramref name="path"/>, for the breadcrumb.</summary>
    public static IReadOnlyList<string> GetAncestors(string path)
    {
        var chain = new List<string>();

        try
        {
            var current = new DirectoryInfo(path);
            while (current is not null)
            {
                chain.Add(current.FullName);
                current = current.Parent;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return chain;
        }

        chain.Reverse();
        return chain;
    }
}
