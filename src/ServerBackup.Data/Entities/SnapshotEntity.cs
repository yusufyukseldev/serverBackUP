namespace ServerBackup.Data.Entities;

public sealed class SnapshotEntity
{
    public required string SnapshotId { get; set; }
    public string? PlanId { get; set; }
    public string? ParentSnapshotId { get; set; }
    public required DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public required string RootTreeBlobId { get; set; }

    public List<SnapshotPathEntity> Paths { get; set; } = [];
}
