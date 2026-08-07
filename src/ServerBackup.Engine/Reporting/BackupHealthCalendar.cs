namespace ServerBackup.Engine.Reporting;

public enum DayHealth
{
    /// <summary>No job ran that day — not a failure, just silence.</summary>
    None,
    Ok,
    Err,
}

/// <summary>
/// Pure bucketing logic (no I/O) behind the dashboard's 90-day health strip
/// (design-system.md §12.1). A day with any failed job is Err even if other
/// jobs that day succeeded — a single failure is the thing an operator needs
/// to see, and averaging it away would defeat the strip's purpose.
/// </summary>
public static class BackupHealthCalendar
{
    public static IReadOnlyList<DayHealth> LastNDays(
        IReadOnlyList<(DateTimeOffset StartedAtUtc, string Status)> jobs, DateTimeOffset todayUtc, int days)
    {
        var byDay = new Dictionary<DateOnly, DayHealth>();
        foreach (var job in jobs)
        {
            var day = DateOnly.FromDateTime(job.StartedAtUtc.UtcDateTime);
            var health = job.Status == "Failed" ? DayHealth.Err : DayHealth.Ok;

            byDay[day] = byDay.TryGetValue(day, out var existing) && existing == DayHealth.Err
                ? DayHealth.Err
                : health;
        }

        var todayDate = DateOnly.FromDateTime(todayUtc.UtcDateTime);
        var result = new DayHealth[days];
        for (var i = 0; i < days; i++)
        {
            var day = todayDate.AddDays(-(days - 1 - i));
            result[i] = byDay.GetValueOrDefault(day, DayHealth.None);
        }

        return result;
    }
}
