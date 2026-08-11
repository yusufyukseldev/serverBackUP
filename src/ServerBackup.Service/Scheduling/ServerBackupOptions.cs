namespace ServerBackup.Service.Scheduling;

public sealed class ServerBackupOptions
{
    public const string SectionName = "ServerBackup";

    /// <summary>
    /// Starting set of repositories, used only to seed the registry the first
    /// time the service runs. After that the panel owns the list — see
    /// <see cref="Storage.RepositoryRegistry"/>.
    /// </summary>
    public List<string> Repositories { get; set; } = [];

    /// <summary>Where the service keeps state it writes itself. Empty means %ProgramData%\ServerBackup.</summary>
    public string? DataDirectory { get; set; }

    public int PollIntervalSeconds { get; set; } = 60;

    public int MaxConcurrentJobs { get; set; } = 2;

    public string ResolveDataDirectory() => string.IsNullOrWhiteSpace(DataDirectory)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ServerBackup")
        : DataDirectory;
}
