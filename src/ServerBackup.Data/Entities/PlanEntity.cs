namespace ServerBackup.Data.Entities;

public sealed class PlanEntity
{
    public required string PlanId { get; set; }
    public required string Name { get; set; }
    public required string SourcePathsJson { get; set; }
    public string? CronSchedule { get; set; }
    public string? RetentionPolicyJson { get; set; }

    /// <summary>Null means "no periodic verify" — a plan with a backup schedule is not required to have one.</summary>
    public string? VerifyCronSchedule { get; set; }

    /// <summary>Stores a ServerBackup.Engine.Verify.VerifyLevel name; unset when VerifyCronSchedule is unset.</summary>
    public string? VerifyLevel { get; set; }

    public required DateTimeOffset CreatedAtUtc { get; set; }
}
