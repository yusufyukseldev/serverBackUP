using FluentAssertions;
using ServerBackup.Engine.Scheduling;
using ServerBackup.Service.Formatting;
using Xunit;

namespace ServerBackup.Service.Tests;

public sealed class CronTextTests
{
    [Theory]
    [InlineData("0 3 * * *", "Her gün saat 03:00")]
    [InlineData("0 8-20/2 * * 1,2,3,4,5", "Hafta içi 08:00–20:00 arası 2 saatte bir")]
    [InlineData("0 9-17 * * 1,2,3,4,5", "Hafta içi 09:00–17:00 arası saat başı")]
    [InlineData("0 2 * * 0,6", "Hafta sonu saat 02:00")]
    [InlineData("0 4 * * 1,3", "Pzt, Çar saat 04:00")]
    [InlineData("0 6 * * 0,1,2,3,4,5,6", "Her gün saat 06:00")]
    public void Builder_shaped_expressions_are_described_in_plain_Turkish(string cron, string expected) =>
        CronText.Describe(cron).Should().Be(expected);

    [Theory]
    [InlineData("*/15 * * * *")]
    [InlineData("0 0 1 * *")]
    [InlineData("nonsense")]
    public void Hand_written_expressions_fall_back_to_the_raw_text(string cron) =>
        // Better to show cron the operator wrote than to describe it wrongly.
        CronText.Describe(cron).Should().Be(cron);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_missing_schedule_renders_as_a_dash(string? cron) =>
        CronText.Describe(cron).Should().Be("—");

    [Fact]
    public void Every_expression_CronBuilder_produces_is_describable()
    {
        var cron = CronBuilder.Build(
            new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday },
            startHour: 8, endHour: 18, intervalHours: 3);

        CronText.Describe(cron).Should().Be("Pzt, Çar, Cum 08:00–18:00 arası 3 saatte bir");
    }
}
