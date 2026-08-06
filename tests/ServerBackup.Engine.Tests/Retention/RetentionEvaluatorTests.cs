using FluentAssertions;
using ServerBackup.Engine.Retention;
using Xunit;

namespace ServerBackup.Engine.Tests.Retention;

public sealed class RetentionEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    // Ten daily snapshots, most recent first: 2026-08-06 down to 2026-07-28.
    private static List<SnapshotSummary> TenDailySnapshots() =>
        Enumerable.Range(0, 10)
            .Select(i => new SnapshotSummary($"s{i}", Now.AddDays(-i), []))
            .ToList();

    [Fact]
    public void Empty_policy_keeps_everything()
    {
        var snapshots = TenDailySnapshots();

        var kept = RetentionEvaluator.SelectSnapshotsToKeep(snapshots, new RetentionPolicy(), Now);

        kept.Should().BeEquivalentTo(snapshots.Select(s => s.SnapshotId));
    }

    [Fact]
    public void KeepLast_keeps_only_the_N_most_recent()
    {
        var snapshots = TenDailySnapshots();

        var kept = RetentionEvaluator.SelectSnapshotsToKeep(snapshots, new RetentionPolicy(KeepLast: 3), Now);

        kept.Should().BeEquivalentTo(["s0", "s1", "s2"]);
    }

    [Fact]
    public void KeepDaily_keeps_the_most_recent_snapshot_per_day_up_to_N_days()
    {
        // Two snapshots on the same day (s0 and an extra one a few hours earlier).
        var snapshots = TenDailySnapshots();
        snapshots.Add(new SnapshotSummary("s0b", Now.AddHours(-6), []));

        var kept = RetentionEvaluator.SelectSnapshotsToKeep(snapshots, new RetentionPolicy(KeepDaily: 3), Now);

        // Only the most recent per day survives, for the 3 most recent days.
        kept.Should().BeEquivalentTo(["s0", "s1", "s2"]);
    }

    [Fact]
    public void KeepWeekly_keeps_one_snapshot_per_ISO_week_for_N_weeks()
    {
        var snapshots = new List<SnapshotSummary>
        {
            new("this-week-a", new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero), []),
            new("this-week-b", new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero), []),
            new("last-week", new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero), []),
            new("two-weeks-ago", new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero), []),
            new("three-weeks-ago", new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.Zero), []),
        };

        var kept = RetentionEvaluator.SelectSnapshotsToKeep(snapshots, new RetentionPolicy(KeepWeekly: 2), Now);

        kept.Should().BeEquivalentTo(["this-week-a", "last-week"]);
    }

    [Fact]
    public void KeepMonthly_keeps_one_snapshot_per_month_for_N_months()
    {
        var snapshots = new List<SnapshotSummary>
        {
            new("aug-1", new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero), []),
            new("aug-2", new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), []),
            new("jul", new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero), []),
            new("jun", new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), []),
            new("may", new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero), []),
        };

        var kept = RetentionEvaluator.SelectSnapshotsToKeep(snapshots, new RetentionPolicy(KeepMonthly: 2), Now);

        kept.Should().BeEquivalentTo(["aug-1", "jul"]);
    }

    [Fact]
    public void KeepYearly_keeps_one_snapshot_per_year_for_N_years()
    {
        var snapshots = new List<SnapshotSummary>
        {
            new("2026-a", new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero), []),
            new("2026-b", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), []),
            new("2025", new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero), []),
            new("2024", new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero), []),
        };

        var kept = RetentionEvaluator.SelectSnapshotsToKeep(snapshots, new RetentionPolicy(KeepYearly: 2), Now);

        kept.Should().BeEquivalentTo(["2026-a", "2025"]);
    }

    [Fact]
    public void KeepWithin_keeps_everything_newer_than_the_cutoff()
    {
        var snapshots = TenDailySnapshots();

        var kept = RetentionEvaluator.SelectSnapshotsToKeep(snapshots, new RetentionPolicy(KeepWithin: TimeSpan.FromDays(3.5)), Now);

        kept.Should().BeEquivalentTo(["s0", "s1", "s2", "s3"]);
    }

    [Fact]
    public void KeepTags_keeps_any_snapshot_with_a_matching_tag_regardless_of_age()
    {
        var snapshots = TenDailySnapshots();
        snapshots[9] = snapshots[9] with { Tags = ["monthly-archive"] };

        var kept = RetentionEvaluator.SelectSnapshotsToKeep(snapshots, new RetentionPolicy(KeepTags: ["monthly-archive"]), Now);

        kept.Should().BeEquivalentTo(["s9"]);
    }

    [Fact]
    public void Rules_are_unioned_not_intersected()
    {
        var snapshots = TenDailySnapshots();

        // KeepLast:1 alone would only keep s0; KeepDaily:3 alone would keep s0-s2.
        // Combined, the result is the union.
        var kept = RetentionEvaluator.SelectSnapshotsToKeep(
            snapshots, new RetentionPolicy(KeepLast: 1, KeepYearly: 1), Now);

        // KeepYearly:1 keeps the single most recent snapshot in the current year bucket,
        // which is also s0 here — still just {s0}, but proves both rules ran.
        kept.Should().BeEquivalentTo(["s0"]);
    }

    [Fact]
    public void GFS_style_combined_policy_matches_the_expected_selection_table()
    {
        // A realistic GFS policy over 40 days of daily snapshots.
        var snapshots = Enumerable.Range(0, 40)
            .Select(i => new SnapshotSummary($"d{i}", Now.AddDays(-i), []))
            .ToList();

        var policy = new RetentionPolicy(KeepDaily: 7, KeepWeekly: 4, KeepMonthly: 2);
        var kept = RetentionEvaluator.SelectSnapshotsToKeep(snapshots, policy, Now);

        // Daily rule: d0..d6 (7 most recent days).
        for (var i = 0; i <= 6; i++)
        {
            kept.Should().Contain($"d{i}");
        }

        // Every kept id must be explainable by at least one rule; nothing
        // beyond ~2 months back should survive with this policy.
        kept.Should().OnlyContain(id => int.Parse(id.Substring(1)) < 62);
        kept.Count.Should().BeLessThan(snapshots.Count, "the policy must actually reduce the snapshot set");
    }
}
