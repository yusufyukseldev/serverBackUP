namespace ServerBackup.Service.Storage;

/// <summary>ServerBackupOptions only stores repo paths, no display name — the folder name is the closest thing to one.</summary>
public static class RepoNaming
{
    public static string DisplayName(string repoPath)
    {
        var trimmed = repoPath.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }
}
