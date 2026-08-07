using System.Globalization;

namespace ServerBackup.Service.Formatting;

public static class Fmt
{
    private static readonly CultureInfo TrTr = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB"];

    public static string Bytes(long bytes)
    {
        double value = bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < ByteUnits.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        var digits = unitIndex == 0 ? "N0" : "N1";
        return $"{value.ToString(digits, TrTr)} {ByteUnits[unitIndex]}";
    }

    public static string Number(long value) => value.ToString("N0", TrTr);

    /// <summary>Local wall-clock time, never UTC — an operator reading the panel cares when a job ran on this machine, not in UTC.</summary>
    public static string DateTime(DateTimeOffset value) => value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", TrTr);

    public static string Relative(DateTimeOffset value)
    {
        var delta = DateTimeOffset.UtcNow - value.ToUniversalTime();
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        return delta switch
        {
            { TotalSeconds: < 60 } => "az önce",
            { TotalMinutes: < 60 } => $"{(int)delta.TotalMinutes} dk önce",
            { TotalHours: < 24 } => $"{(int)delta.TotalHours} sa önce",
            { TotalDays: < 30 } => $"{(int)delta.TotalDays} gün önce",
            { TotalDays: < 365 } => $"{(int)(delta.TotalDays / 30)} ay önce",
            _ => $"{(int)(delta.TotalDays / 365)} yıl önce",
        };
    }

    /// <summary>En anlamlı iki birim (design-system.md §9) — a single unit once you're down to seconds, two once anything larger is involved.</summary>
    public static string Duration(TimeSpan span)
    {
        (int Value, string Unit)[] parts =
        [
            (span.Days, "gün"),
            (span.Hours, "sa"),
            (span.Minutes, "dk"),
            (span.Seconds, "sn"),
        ];

        var firstIndex = Array.FindIndex(parts, p => p.Value > 0);
        if (firstIndex < 0)
        {
            return "0 sn";
        }

        if (firstIndex == parts.Length - 1)
        {
            return $"{parts[firstIndex].Value} {parts[firstIndex].Unit}";
        }

        return $"{parts[firstIndex].Value} {parts[firstIndex].Unit} {parts[firstIndex + 1].Value} {parts[firstIndex + 1].Unit}";
    }

    public static string TruncatePath(string path, int maxLength)
    {
        if (path.Length <= maxLength)
        {
            return path;
        }

        var segments = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3)
        {
            return HardTruncate(path, maxLength);
        }

        var isUnc = path.StartsWith(@"\\", StringComparison.Ordinal);
        var prefix = isUnc ? $@"\\{segments[0]}" : segments[0];
        var truncated = $@"{prefix}\{segments[1]}\…\{segments[^1]}";

        return truncated.Length <= maxLength ? truncated : HardTruncate(truncated, maxLength);
    }

    private static string HardTruncate(string value, int maxLength) =>
        maxLength <= 1 ? "…" : string.Concat(value.AsSpan(0, maxLength - 1), "…");
}
