using FluentAssertions;
using ServerBackup.Service.Storage;
using Xunit;

namespace ServerBackup.Service.Tests.Storage;

public sealed class RepoNamingTests
{
    [Theory]
    [InlineData(@"D:\Yedek\Muhasebe", "Muhasebe")]
    [InlineData(@"D:\Yedek\Muhasebe\", "Muhasebe")]
    [InlineData(@"\\NAS01\yedek\fs01", "fs01")]
    public void DisplayName_is_the_last_path_segment(string repoPath, string expected)
    {
        RepoNaming.DisplayName(repoPath).Should().Be(expected);
    }

    [Fact]
    public void DisplayName_falls_back_to_the_trimmed_path_when_there_is_no_deeper_segment()
    {
        RepoNaming.DisplayName(@"D:\").Should().Be(@"D:");
    }
}
