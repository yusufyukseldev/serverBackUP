namespace ServerBackup.Engine.Scheduling;

public sealed record ScheduledJob(string RepoPath, string JobId, string PlanId);
