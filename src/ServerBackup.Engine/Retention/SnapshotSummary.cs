namespace ServerBackup.Engine.Retention;

public sealed record SnapshotSummary(string SnapshotId, DateTimeOffset StartedAtUtc, IReadOnlyList<string> Tags);
