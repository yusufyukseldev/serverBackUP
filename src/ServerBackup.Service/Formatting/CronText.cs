namespace ServerBackup.Service.Formatting;

/// <summary>
/// Renders a cron expression as the sentence an operator can verify at a
/// glance — design-system.md §12.3 forbids showing raw cron in the schedule
/// column. Only the shapes <see cref="ServerBackup.Engine.Scheduling.CronBuilder"/>
/// produces are described; anything hand-written falls back to the raw
/// expression rather than risking a confident wrong description.
/// </summary>
public static class CronText
{
    private static readonly string[] DayNames = ["Paz", "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt"];

    public static string Describe(string? cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            return "—";
        }

        var fields = cronExpression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5 || fields[0] != "0" || fields[2] != "*" || fields[3] != "*")
        {
            return cronExpression;
        }

        var hours = DescribeHours(fields[1]);
        var days = DescribeDays(fields[4]);

        return hours is null || days is null ? cronExpression : $"{days} {hours}";
    }

    private static string? DescribeHours(string field)
    {
        if (int.TryParse(field, out var single))
        {
            return single is >= 0 and <= 23 ? $"saat {single:00}:00" : null;
        }

        var stepSplit = field.Split('/');
        var rangeSplit = stepSplit[0].Split('-');
        if (stepSplit.Length > 2 || rangeSplit.Length != 2
            || !int.TryParse(rangeSplit[0], out var start) || !int.TryParse(rangeSplit[1], out var end))
        {
            return null;
        }

        var window = $"{start:00}:00–{end:00}:00 arası";
        if (stepSplit.Length == 1)
        {
            return $"{window} saat başı";
        }

        return int.TryParse(stepSplit[1], out var step) ? $"{window} {step} saatte bir" : null;
    }

    private static string? DescribeDays(string field)
    {
        if (field == "*")
        {
            return "Her gün";
        }

        var days = new List<int>();
        foreach (var part in field.Split(','))
        {
            if (!int.TryParse(part, out var day) || day is < 0 or > 6)
            {
                return null;
            }

            days.Add(day);
        }

        days.Sort();

        if (days.SequenceEqual([1, 2, 3, 4, 5]))
        {
            return "Hafta içi";
        }

        if (days.SequenceEqual([0, 6]))
        {
            return "Hafta sonu";
        }

        return days.Count == 7 ? "Her gün" : string.Join(", ", days.Select(d => DayNames[d]));
    }
}
