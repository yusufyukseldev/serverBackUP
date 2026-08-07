using FluentAssertions;
using ServerBackup.Service.Storage;
using Xunit;

namespace ServerBackup.Service.Tests.Storage;

public sealed class DiskUsageTests
{
    [Theory]
    [InlineData(84, false)]
    [InlineData(85, true)]
    [InlineData(94, true)]
    [InlineData(95, false)]
    public void IsWarning_covers_85_to_under_95(int percent, bool expected)
    {
        DiskUsage.IsWarning(percent).Should().Be(expected);
    }

    [Theory]
    [InlineData(94, false)]
    [InlineData(95, true)]
    [InlineData(100, true)]
    public void IsDanger_is_95_and_above(int percent, bool expected)
    {
        DiskUsage.IsDanger(percent).Should().Be(expected);
    }

    [Fact]
    public void GetUsedPercent_returns_null_for_an_unresolvable_root()
    {
        DiskUsage.GetUsedPercent(@"\\unreachable-host\share\repo").Should().BeNull();
    }
}
