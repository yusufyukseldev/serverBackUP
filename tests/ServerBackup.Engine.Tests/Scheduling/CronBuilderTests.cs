using Cronos;
using FluentAssertions;
using ServerBackup.Engine.Scheduling;
using Xunit;

namespace ServerBackup.Engine.Tests.Scheduling;

public sealed class CronBuilderTests
{
    private static readonly HashSet<DayOfWeek> Weekdays =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    private static readonly HashSet<DayOfWeek> AllDays = Enum.GetValues<DayOfWeek>().ToHashSet();

    [Fact]
    public void Weekdays_business_hours_every_two_hours_produces_the_expected_expression()
    {
        var cron = CronBuilder.Build(Weekdays, startHour: 8, endHour: 20, intervalHours: 2);

        cron.Should().Be("0 8-20/2 * * 1,2,3,4,5");
    }

    [Fact]
    public void All_seven_days_selected_collapses_to_a_wildcard_day_field()
    {
        var cron = CronBuilder.Build(AllDays, startHour: 3, endHour: 3, intervalHours: 1);

        cron.Should().Be("0 3 * * *");
    }

    [Fact]
    public void Same_start_and_end_hour_means_run_once_a_day_ignoring_interval()
    {
        var cron = CronBuilder.Build(Weekdays, startHour: 22, endHour: 22, intervalHours: 4);

        cron.Should().Be("0 22 * * 1,2,3,4,5");
    }

    [Fact]
    public void Interval_of_one_produces_a_plain_hour_range_without_a_step()
    {
        var cron = CronBuilder.Build(Weekdays, startHour: 9, endHour: 17, intervalHours: 1);

        cron.Should().Be("0 9-17 * * 1,2,3,4,5");
    }

    [Theory]
    [MemberData(nameof(ValidCombinations))]
    public void Every_produced_expression_is_parseable_by_cronos(HashSet<DayOfWeek> days, int start, int end, int interval)
    {
        var cron = CronBuilder.Build(days, start, end, interval);

        var act = () => CronExpression.Parse(cron, CronFormat.Standard);

        act.Should().NotThrow();
    }

    public static TheoryData<HashSet<DayOfWeek>, int, int, int> ValidCombinations() => new()
    {
        { Weekdays, 8, 20, 2 },
        { AllDays, 0, 23, 6 },
        { new HashSet<DayOfWeek> { DayOfWeek.Saturday, DayOfWeek.Sunday }, 10, 10, 1 },
    };

    [Fact]
    public void No_days_selected_throws()
    {
        var act = () => CronBuilder.Build(new HashSet<DayOfWeek>(), 8, 20, 2);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(-1, 20)]
    [InlineData(24, 20)]
    [InlineData(8, 24)]
    public void Out_of_range_hours_throw(int start, int end)
    {
        var act = () => CronBuilder.Build(Weekdays, start, end, 1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void End_hour_before_start_hour_throws()
    {
        var act = () => CronBuilder.Build(Weekdays, startHour: 20, endHour: 8, intervalHours: 1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Zero_interval_throws()
    {
        var act = () => CronBuilder.Build(Weekdays, 8, 20, intervalHours: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
