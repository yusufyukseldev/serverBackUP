namespace ServerBackup.Data.Entities;

public sealed class JobEntity
{
    public required string JobId { get; set; }
    public string? PlanId { get; set; }
    public required string Kind { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }

    public List<JobLogEntity> Logs { get; set; } = [];
}
