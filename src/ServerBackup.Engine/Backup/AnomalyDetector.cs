namespace ServerBackup.Engine.Backup;

/// <summary>
/// Configurable ransomware/mass-corruption detection for a backup run —
/// see plan Faz 11. Two independent signals: a sudden bulk change/deletion
/// ratio (generic — catches most encryption sweeps), and specific
/// known-ransomware file extensions appearing where they didn't before
/// (catches it even on a small subset of files).
/// </summary>
public sealed record AnomalyPolicy(
    double ChangedOrDeletedRatioThreshold = 0.5,
    int MinimumParentFileCount = 20,
    IReadOnlyList<string>? SuspiciousExtensions = null,
    bool AbortOnDetection = false)
{
    public static readonly IReadOnlyList<string> DefaultSuspiciousExtensions =
    [
        ".locked", ".encrypted", ".crypt", ".crypted", ".locky", ".cerber",
        ".zepto", ".enc", ".vault", ".ransom", ".wcry", ".wncry", ".ryk", ".conti",
    ];

    public IReadOnlyList<string> EffectiveSuspiciousExtensions => SuspiciousExtensions ?? DefaultSuspiciousExtensions;
}

public sealed record AnomalyReport(bool Detected, IReadOnlyList<string> Reasons);

public static class AnomalyDetector
{
    public static AnomalyReport Evaluate(
        AnomalyPolicy policy,
        int parentFileCount,
        int changedCount,
        int deletedCount,
        IReadOnlyList<string> newOrChangedFileNames)
    {
        var reasons = new List<string>();

        if (parentFileCount >= policy.MinimumParentFileCount)
        {
            var ratio = (double)(changedCount + deletedCount) / parentFileCount;
            if (ratio > policy.ChangedOrDeletedRatioThreshold)
            {
                reasons.Add(
                    $"Dosyaların %{ratio * 100:0}'i değişti veya silindi " +
                    $"(eşik: %{policy.ChangedOrDeletedRatioThreshold * 100:0}, {changedCount} değişti + {deletedCount} silindi / {parentFileCount} toplam).");
            }
        }

        var suspicious = newOrChangedFileNames
            .Where(name => policy.EffectiveSuspiciousExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (suspicious.Count > 0)
        {
            var sample = string.Join(", ", suspicious.Take(5));
            var suffix = suspicious.Count > 5 ? ", ..." : "";
            reasons.Add($"Şüpheli uzantılı {suspicious.Count} yeni/değişmiş dosya: {sample}{suffix}");
        }

        return new AnomalyReport(reasons.Count > 0, reasons);
    }
}

/// <summary>Thrown when <see cref="AnomalyPolicy.AbortOnDetection"/> is set and an anomaly was found — the snapshot is never committed.</summary>
public sealed class AnomalyDetectedException(string message) : Exception(message);
