namespace ServerBackup.Data.Entities;

public sealed class SnapshotPathEntity
{
    public int Id { get; set; }
    public required string SnapshotId { get; set; }
    public required string SourcePath { get; set; }

    public SnapshotEntity Snapshot { get; set; } = null!;
}
