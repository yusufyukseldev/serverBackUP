using FluentAssertions;
using ServerBackup.Engine.Reporting;
using Xunit;

namespace ServerBackup.Engine.Tests.Reporting;

public sealed class BackupHealthCalendarTests
{
    private static readonly DateTimeOffset Today = new(2026, 8, 7, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_day_with_no_jobs_is_None()
    {
        var result = BackupHealthCalendar.LastNDays([], Today, days: 3);

        result.Should().AllBeEquivalentTo(DayHealth.None);
    }

    [Fact]
    public void A_day_with_only_succeeded_jobs_is_Ok()
    {
        var jobs = new[] { (Today, "Succeeded") };

        var result = BackupHealthCalendar.LastNDays(jobs, Today, days: 1);

        result.Should().Equal(DayHealth.Ok);
    }

    [Fact]
    public void A_day_with_any_failed_job_is_Err_even_if_others_succeeded()
    {
        var jobs = new[] { (Today, "Succeeded"), (Today, "Failed") };

        var result = BackupHealthCalendar.LastNDays(jobs, Today, days: 1);

        result.Should().Equal(DayHealth.Err);
    }

    [Fact]
    public void Days_are_returned_oldest_to_newest()
    {
        var yesterday = Today.AddDays(-1);
        var jobs = new[] { (yesterday, "Failed"), (Today, "Succeeded") };

        var result = BackupHealthCalendar.LastNDays(jobs, Today, days: 2);

        result.Should().Equal(DayHealth.Err, DayHealth.Ok);
    }
}
