namespace ServerBackup.Service.Scheduling;

public sealed class ServerBackupOptions
{
    public const string SectionName = "ServerBackup";

    public List<string> Repositories { get; set; } = [];

    public int PollIntervalSeconds { get; set; } = 60;

    public int MaxConcurrentJobs { get; set; } = 2;
}
