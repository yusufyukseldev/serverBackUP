using System.Globalization;

namespace ServerBackup.Engine.Retention;

/// <summary>
/// Pure decision logic (no I/O) for which snapshots a <see cref="RetentionPolicy"/>
/// keeps — mirrors restic's `forget` semantics. A snapshot is kept if ANY
/// configured rule selects it (rules are unioned, never intersected). An
/// empty policy keeps everything — pruning requires explicit opt-in.
/// </summary>
public static class RetentionEvaluator
{
    public static HashSet<string> SelectSnapshotsToKeep(
        IReadOnlyList<SnapshotSummary> snapshots, RetentionPolicy policy, DateTimeOffset now)
    {
        var keep = new HashSet<string>();

        var hasAnyRule = policy.KeepLast is not null
            || policy.KeepHourly is not null
            || policy.KeepDaily is not null
            || policy.KeepWeekly is not null
            || policy.KeepMonthly is not null
            || policy.KeepYearly is not null
            || policy.KeepWithin is not null
            || policy.KeepTags is { Count: > 0 };

        if (!hasAnyRule)
        {
            foreach (var s in snapshots)
            {
                keep.Add(s.SnapshotId);
            }

            return keep;
        }

        var orderedDesc = snapshots.OrderByDescending(s => s.StartedAtUtc).ToList();

        if (policy.KeepLast is { } last)
        {
            foreach (var s in orderedDesc.Take(last))
            {
                keep.Add(s.SnapshotId);
            }
        }

        if (policy.KeepHourly is { } hourly)
        {
            KeepMostRecentPerBucket(orderedDesc, hourly, s => (s.StartedAtUtc.Year, s.StartedAtUtc.Month, s.StartedAtUtc.Day, s.StartedAtUtc.Hour), keep);
        }

        if (policy.KeepDaily is { } daily)
        {
            KeepMostRecentPerBucket(orderedDesc, daily, s => (s.StartedAtUtc.Year, s.StartedAtUtc.Month, s.StartedAtUtc.Day), keep);
        }

        if (policy.KeepWeekly is { } weekly)
        {
            KeepMostRecentPerBucket(orderedDesc, weekly, s => IsoWeek(s.StartedAtUtc), keep);
        }

        if (policy.KeepMonthly is { } monthly)
        {
            KeepMostRecentPerBucket(orderedDesc, monthly, s => (s.StartedAtUtc.Year, s.StartedAtUtc.Month), keep);
        }

        if (policy.KeepYearly is { } yearly)
        {
            KeepMostRecentPerBucket(orderedDesc, yearly, s => s.StartedAtUtc.Year, keep);
        }

        if (policy.KeepWithin is { } within)
        {
            var cutoff = now - within;
            foreach (var s in orderedDesc.Where(s => s.StartedAtUtc >= cutoff))
            {
                keep.Add(s.SnapshotId);
            }
        }

        if (policy.KeepTags is { Count: > 0 } tags)
        {
            foreach (var s in orderedDesc.Where(s => s.Tags.Any(t => tags.Contains(t, StringComparer.OrdinalIgnoreCase))))
            {
                keep.Add(s.SnapshotId);
            }
        }

        return keep;
    }

    /// <summary>Keeps the most recent snapshot in each of the N most recent distinct buckets.</summary>
    private static void KeepMostRecentPerBucket<TKey>(
        List<SnapshotSummary> orderedDesc, int bucketCount, Func<SnapshotSummary, TKey> bucketOf, HashSet<string> keep)
        where TKey : notnull
    {
        var seenBuckets = new HashSet<TKey>();
        foreach (var s in orderedDesc)
        {
            var bucket = bucketOf(s);
            if (seenBuckets.Contains(bucket))
            {
                continue; // an older snapshot in an already-counted bucket
            }

            if (seenBuckets.Count >= bucketCount)
            {
                break; // enough buckets found; everything remaining is older still
            }

            seenBuckets.Add(bucket);
            keep.Add(s.SnapshotId);
        }
    }

    private static (int Year, int Week) IsoWeek(DateTimeOffset dt)
    {
        var date = dt.UtcDateTime;
        return (ISOWeek.GetYear(date), ISOWeek.GetWeekOfYear(date));
    }
}
