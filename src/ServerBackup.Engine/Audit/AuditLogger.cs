using ServerBackup.Data;
using ServerBackup.Data.Entities;

namespace ServerBackup.Engine.Audit;

/// <summary>
/// Records who did what to a repository (deletions, restores) — see plan
/// Faz 11. The actor is the OS identity of the running process (the
/// interactive user for CLI/panel use, the service account for scheduled
/// jobs), since the repository has no user accounts of its own.
/// </summary>
public static class AuditLogger
{
    public static async Task RecordAsync(CatalogDbContext db, string action, string? details, CancellationToken ct = default)
    {
        db.AuditLogs.Add(new AuditLogEntity
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Actor = CurrentActor(),
            Action = action,
            Details = details,
        });

        await db.SaveChangesAsync(ct);
    }

    private static string CurrentActor()
    {
        try
        {
            return $"{Environment.UserDomainName}\\{Environment.UserName}";
        }
        catch
        {
            return Environment.UserName;
        }
    }
}
