using Cronos;

namespace ServerBackup.Engine.Scheduling;

/// <summary>
/// Pure cron due-date logic (no I/O), so the scheduler's decision-making is
/// unit-testable without a running service. "Due" means: the next scheduled
/// occurrence after the last successful run is at or before now. A plan that
/// has never succeeded is always due immediately. A plan that missed several
/// occurrences while the service was down (misfire) runs once to catch up —
/// it does not queue one run per missed occurrence.
/// </summary>
public static class CronScheduleCalculator
{
    public static bool IsDue(string cronExpression, DateTimeOffset? lastSuccessfulRunUtc, DateTimeOffset nowUtc)
    {
        var next = GetNextOccurrence(cronExpression, lastSuccessfulRunUtc ?? DateTimeOffset.MinValue);
        return next is not null && next <= nowUtc;
    }

    public static DateTimeOffset? GetNextOccurrence(string cronExpression, DateTimeOffset afterUtc)
    {
        var expression = CronExpression.Parse(cronExpression, CronFormat.Standard);
        var next = expression.GetNextOccurrence(afterUtc.UtcDateTime, TimeZoneInfo.Utc);
        return next is null ? null : new DateTimeOffset(DateTime.SpecifyKind(next.Value, DateTimeKind.Utc));
    }
}
