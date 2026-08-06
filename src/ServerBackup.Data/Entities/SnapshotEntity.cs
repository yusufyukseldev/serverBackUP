namespace ServerBackup.Data.Entities;

public sealed class SnapshotEntity
{
    public required string SnapshotId { get; set; }
    public string? PlanId { get; set; }
    public string? ParentSnapshotId { get; set; }
    public required DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public required string RootTreeBlobId { get; set; }

    /// <summary>Comma-separated tags, used by the retention policy's KeepTags rule. Null/empty means no tags.</summary>
    public string? Tags { get; set; }

    public List<SnapshotPathEntity> Paths { get; set; } = [];
}
