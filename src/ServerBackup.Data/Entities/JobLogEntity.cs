namespace ServerBackup.Data.Entities;

public sealed class JobLogEntity
{
    public int Id { get; set; }
    public required string JobId { get; set; }
    public required DateTimeOffset TimestampUtc { get; set; }
    public required string Level { get; set; }
    public required string Message { get; set; }

    public JobEntity Job { get; set; } = null!;
}
