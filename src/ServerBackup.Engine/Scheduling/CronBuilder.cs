namespace ServerBackup.Engine.Scheduling;

/// <summary>
/// Turns a "which days, which hour window, how often within it" description into a
/// standard 5-field cron expression — lets the UI/CLI offer day-of-week checkboxes and an
/// hour range instead of asking operators to hand-write cron syntax. Businesses commonly
/// want "weekdays only, 08:00-20:00, every 2 hours" (skip weekends with no data entry,
/// skip nights) — that shape doesn't fit a single cron field cleanly without this.
/// </summary>
public static class CronBuilder
{
    public static string Build(IReadOnlySet<DayOfWeek> days, int startHour, int endHour, int intervalHours)
    {
        if (days.Count == 0)
        {
            throw new ArgumentException("At least one day must be selected.", nameof(days));
        }

        if (startHour is < 0 or > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(startHour), startHour, "Hour must be between 0 and 23.");
        }

        if (endHour is < 0 or > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(endHour), endHour, "Hour must be between 0 and 23.");
        }

        if (endHour < startHour)
        {
            throw new ArgumentException("End hour must not be before start hour.", nameof(endHour));
        }

        if (intervalHours < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalHours), intervalHours, "Interval must be at least 1 hour.");
        }

        var hoursField = startHour == endHour
            ? startHour.ToString()
            : intervalHours == 1
                ? $"{startHour}-{endHour}"
                : $"{startHour}-{endHour}/{intervalHours}";

        var daysField = days.Count == 7
            ? "*"
            : string.Join(',', days.Select(d => (int)d).OrderBy(n => n));

        return $"0 {hoursField} * * {daysField}";
    }
}
