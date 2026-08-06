using Cronos;
using FluentAssertions;
using ServerBackup.Engine.Scheduling;
using Xunit;

namespace ServerBackup.Engine.Tests.Scheduling;

public sealed class CronScheduleCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero); // Thursday

    [Fact]
    public void A_plan_that_never_ran_is_immediately_due()
    {
        var due = CronScheduleCalculator.IsDue("0 0 * * *", lastSuccessfulRunUtc: null, Now);

        due.Should().BeTrue();
    }

    [Fact]
    public void Not_due_when_the_next_occurrence_is_still_in_the_future()
    {
        // Daily at midnight, last run was this morning at 00:00 — next is tomorrow.
        var lastRun = new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero);

        var due = CronScheduleCalculator.IsDue("0 0 * * *", lastRun, Now);

        due.Should().BeFalse();
    }

    [Fact]
    public void Due_once_the_next_occurrence_has_passed()
    {
        var lastRun = new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero);

        var due = CronScheduleCalculator.IsDue("0 0 * * *", lastRun, Now);

        due.Should().BeTrue("the 2026-08-06 00:00 occurrence has already passed relative to Now (12:00)");
    }

    [Fact]
    public void Misfire_after_a_long_outage_becomes_due_exactly_once_not_once_per_missed_run()
    {
        // Hourly schedule, last successful run was 10 days ago (service was down).
        var lastRun = Now.AddDays(-10);

        var due = CronScheduleCalculator.IsDue("0 * * * *", lastRun, Now);

        due.Should().BeTrue();
        // The point of the misfire test: IsDue is a single boolean, not a
        // count — the scheduler that calls this can only ever enqueue one
        // job per poll, so a 10-day gap can never turn into 240 queued jobs.
    }

    [Fact]
    public void GetNextOccurrence_returns_the_correct_next_daily_time()
    {
        var next = CronScheduleCalculator.GetNextOccurrence("30 3 * * *", Now);

        next.Should().Be(new DateTimeOffset(2026, 8, 7, 3, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void GetNextOccurrence_respects_day_of_week_fields()
    {
        // Every Sunday at 02:00. 2026-08-06 is a Thursday.
        var next = CronScheduleCalculator.GetNextOccurrence("0 2 * * SUN", Now);

        next!.Value.DayOfWeek.Should().Be(DayOfWeek.Sunday);
        next.Value.Should().BeAfter(Now);
    }

    [Theory]
    [InlineData("not a cron expression")]
    [InlineData("* * * *")] // too few fields
    public void An_invalid_cron_expression_throws_a_clear_error(string invalid)
    {
        var act = () => CronScheduleCalculator.GetNextOccurrence(invalid, Now);

        act.Should().Throw<CronFormatException>();
    }
}
