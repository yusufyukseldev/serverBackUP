using FluentAssertions;
using ServerBackup.Engine.Vss;
using Xunit;

namespace ServerBackup.Engine.Tests.Vss;

public sealed class VolumeMapperTests
{
    private static VolumeMapper NewMapper() => new(new Dictionary<string, string>
    {
        [@"C:\"] = @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1",
        [@"D:\"] = @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy2",
    });

    [Fact]
    public void MapPath_rewrites_a_path_under_a_known_volume()
    {
        var mapper = NewMapper();

        var mapped = mapper.MapPath(@"C:\Data\reports\a.txt");

        mapped.Should().Be(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1\Data\reports\a.txt");
    }

    [Fact]
    public void MapPath_leaves_a_path_under_an_unknown_volume_unchanged()
    {
        var mapper = NewMapper();

        var mapped = mapper.MapPath(@"E:\other\file.txt");

        mapped.Should().Be(@"E:\other\file.txt");
    }

    [Fact]
    public void MapPath_handles_the_volume_root_itself()
    {
        var mapper = NewMapper();

        var mapped = mapper.MapPath(@"C:\");

        mapped.Should().Be(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1\");
    }

    [Fact]
    public void UnmapPath_is_the_inverse_of_MapPath()
    {
        var mapper = NewMapper();
        var original = @"D:\Projects\deep\nested\file.bin";

        var roundtripped = mapper.UnmapPath(mapper.MapPath(original));

        roundtripped.Should().Be(original);
    }

    [Fact]
    public void UnmapPath_leaves_a_non_shadow_path_unchanged()
    {
        var mapper = NewMapper();

        mapper.UnmapPath(@"C:\Data\file.txt").Should().Be(@"C:\Data\file.txt");
    }
}
