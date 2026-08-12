using System.Security.AccessControl;

namespace ServerBackup.Engine.Scanning;

/// <summary>
/// Reads files/directories from the local filesystem of the machine the
/// backup runs on. .NET (Core/5+) apps are long-path aware by default on
/// Windows — no manual \\?\ prefixing is needed.
/// </summary>
public sealed class LocalSourceProvider : ISourceProvider
{
    // Not in System.IO.FileAttributes, but GetAttributes/EnumerateFileSystemInfos
    // still return the raw Win32 DWORD, so the bits are present even without a
    // named enum member. Cloud-sync providers (OneDrive Files On-Demand, etc.)
    // set these on placeholder files whose content isn't actually on disk yet —
    // reading one blocks synchronously until the OS hydrates it from the cloud,
    // which can hang a backup indefinitely on a slow connection or signed-out
    // account. See https://learn.microsoft.com/windows/win32/fileio/file-attribute-constants.
    private const int FileAttributeRecallOnOpen = 0x00040000;
    private const int FileAttributeRecallOnDataAccess = 0x00400000;

    private static bool IsCloudPlaceholder(FileAttributes attributes) =>
        ((int)attributes & (FileAttributeRecallOnOpen | FileAttributeRecallOnDataAccess)) != 0;

    public SourceEntry GetEntry(string path)
    {
        var attributes = File.GetAttributes(path);
        var isDirectory = attributes.HasFlag(FileAttributes.Directory);
        var isReparsePoint = attributes.HasFlag(FileAttributes.ReparsePoint);

        if (isDirectory)
        {
            var info = new DirectoryInfo(path);
            return new SourceEntry(
                FullPath: info.FullName,
                Name: info.Name,
                IsDirectory: true,
                Size: 0,
                LastWriteTimeUtc: info.LastWriteTimeUtc,
                Attributes: attributes,
                IsReparsePoint: isReparsePoint,
                IsCloudPlaceholder: IsCloudPlaceholder(attributes));
        }
        else
        {
            var info = new FileInfo(path);
            return new SourceEntry(
                FullPath: info.FullName,
                Name: info.Name,
                IsDirectory: false,
                Size: info.Length,
                LastWriteTimeUtc: info.LastWriteTimeUtc,
                Attributes: attributes,
                IsReparsePoint: isReparsePoint,
                IsCloudPlaceholder: IsCloudPlaceholder(attributes));
        }
    }

    public IEnumerable<SourceEntry> EnumerateChildren(string directoryPath)
    {
        var directory = new DirectoryInfo(directoryPath);
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            var isDirectory = entry.Attributes.HasFlag(FileAttributes.Directory);
            yield return new SourceEntry(
                FullPath: entry.FullName,
                Name: entry.Name,
                IsDirectory: isDirectory,
                Size: isDirectory ? 0 : ((FileInfo)entry).Length,
                LastWriteTimeUtc: entry.LastWriteTimeUtc,
                Attributes: entry.Attributes,
                IsReparsePoint: entry.Attributes.HasFlag(FileAttributes.ReparsePoint),
                IsCloudPlaceholder: IsCloudPlaceholder(entry.Attributes));
        }
    }

    public Stream OpenRead(string filePath) =>
        new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

    public string? TryGetSddl(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            FileSystemSecurity security = attributes.HasFlag(FileAttributes.Directory)
                ? new DirectoryInfo(path).GetAccessControl()
                : new FileInfo(path).GetAccessControl();

            return security.GetSecurityDescriptorSddlForm(
                AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // ACLs are best-effort metadata: a file we can't read the ACL of
            // (permissions, unsupported filesystem) still gets backed up.
            return null;
        }
    }
}
