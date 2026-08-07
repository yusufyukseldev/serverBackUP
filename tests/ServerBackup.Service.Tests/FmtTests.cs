using FluentAssertions;
using ServerBackup.Service.Formatting;
using Xunit;

namespace ServerBackup.Service.Tests;

public sealed class FmtTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1024, "1,0 KB")]
    [InlineData(5_153_960, "4,9 MB")]
    public void Bytes_formats_with_tr_TR_decimal_comma(long bytes, string expected)
    {
        Fmt.Bytes(bytes).Should().Be(expected);
    }

    [Fact]
    public void Number_uses_tr_TR_thousands_separator()
    {
        Fmt.Number(3204).Should().Be("3.204");
    }

    [Fact]
    public void DateTime_renders_local_time_not_utc()
    {
        var utc = new DateTimeOffset(2026, 8, 7, 3, 0, 0, TimeSpan.Zero);
        var local = utc.ToLocalTime();

        Fmt.DateTime(utc).Should().Be(local.ToString("dd.MM.yyyy HH:mm"));
    }

    [Fact]
    public void Relative_describes_minutes_ago()
    {
        Fmt.Relative(DateTimeOffset.UtcNow.AddMinutes(-12)).Should().Be("12 dk önce");
    }

    [Fact]
    public void Duration_shows_the_two_most_significant_units_when_larger_than_a_minute()
    {
        Fmt.Duration(new TimeSpan(1, 12, 0)).Should().Be("1 sa 12 dk");
    }

    [Fact]
    public void Duration_shows_a_single_unit_when_under_a_minute()
    {
        Fmt.Duration(TimeSpan.FromSeconds(47)).Should().Be("47 sn");
    }

    [Fact]
    public void TruncatePath_keeps_the_root_and_the_last_segment()
    {
        Fmt.TruncatePath(@"C:\Veri\Şirket\Arşiv\2024\Muhasebe", 20).Should().Be(@"C:\Veri\…\Muhasebe");
    }

    [Fact]
    public void TruncatePath_returns_short_paths_unchanged()
    {
        Fmt.TruncatePath(@"C:\Veri", 20).Should().Be(@"C:\Veri");
    }
}
