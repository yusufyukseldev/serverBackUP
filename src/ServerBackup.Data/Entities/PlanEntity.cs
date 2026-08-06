namespace ServerBackup.Data.Entities;

public sealed class PlanEntity
{
    public required string PlanId { get; set; }
    public required string Name { get; set; }
    public required string SourcePathsJson { get; set; }
    public string? CronSchedule { get; set; }
    public string? RetentionPolicyJson { get; set; }
    public required DateTimeOffset CreatedAtUtc { get; set; }
}
