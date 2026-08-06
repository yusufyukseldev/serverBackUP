namespace ServerBackup.Data.Entities;

public sealed class AuditLogEntity
{
    public int Id { get; set; }
    public required DateTimeOffset TimestampUtc { get; set; }
    public required string Actor { get; set; }
    public required string Action { get; set; }
    public string? Details { get; set; }
}
