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

    /// <summary>
    /// The inverse of <see cref="Build"/>, for reopening a saved plan in the
    /// day/hour/interval form instead of dropping the operator into raw cron.
    /// Only the shapes <see cref="Build"/> emits are recognised; anything
    /// hand-written returns null and belongs in the cron text box.
    /// </summary>
    public static SimpleSchedule? ParseSimple(string? cron)
    {
        var fields = (cron ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5 || fields[0] != "0" || fields[2] != "*" || fields[3] != "*")
        {
            return null;
        }

        if (!TryParseHours(fields[1], out var startHour, out var endHour, out var intervalHours))
        {
            return null;
        }

        return TryParseDays(fields[4], out var days)
            ? new SimpleSchedule(days, startHour, endHour, intervalHours)
            : null;
    }

    private static bool TryParseHours(string field, out int startHour, out int endHour, out int intervalHours)
    {
        (startHour, endHour, intervalHours) = (0, 0, 1);

        var step = field.Split('/');
        if (step.Length > 2)
        {
            return false;
        }

        if (step.Length == 2 && (!int.TryParse(step[1], out intervalHours) || intervalHours < 1))
        {
            return false;
        }

        var range = step[0].Split('-');
        if (range.Length > 2 || !TryParseHour(range[0], out startHour))
        {
            return false;
        }

        if (range.Length == 1)
        {
            // A fixed hour is what Build emits for "once a day"; reporting it as
            // an interval of 1 would round-trip into an hourly plan.
            (endHour, intervalHours) = (startHour, 24);
            return step.Length == 1;
        }

        return TryParseHour(range[1], out endHour) && endHour >= startHour;
    }

    private static bool TryParseHour(string text, out int hour) =>
        int.TryParse(text, out hour) && hour is >= 0 and <= 23;

    private static bool TryParseDays(string field, out IReadOnlySet<DayOfWeek> days)
    {
        if (field == "*")
        {
            days = new HashSet<DayOfWeek>(Enum.GetValues<DayOfWeek>());
            return true;
        }

        var parsed = new HashSet<DayOfWeek>();
        foreach (var part in field.Split(','))
        {
            if (!int.TryParse(part, out var number) || number is < 0 or > 6)
            {
                days = parsed;
                return false;
            }

            parsed.Add((DayOfWeek)number);
        }

        days = parsed;
        return parsed.Count > 0;
    }
}

public sealed record SimpleSchedule(IReadOnlySet<DayOfWeek> Days, int StartHour, int EndHour, int IntervalHours);
