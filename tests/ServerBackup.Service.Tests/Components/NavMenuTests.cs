using Bunit;
using FluentAssertions;
using ServerBackup.Service.Components.Layout;
using Xunit;

namespace ServerBackup.Service.Tests.Components;

public sealed class NavMenuTests : BunitContext
{
    [Fact]
    public void Renders_a_link_for_every_top_level_page()
    {
        var cut = Render<NavMenu>();

        cut.Markup.Should().Contain("Dashboard");
        cut.Markup.Should().Contain("Depolar");
        cut.Markup.Should().Contain("Planlar");
        cut.Markup.Should().Contain("İş Geçmişi");
        cut.Markup.Should().Contain("Snapshot'lar");
        cut.Markup.Should().Contain("Geri Yükle");
    }

    [Fact]
    public void Every_link_points_to_a_distinct_route()
    {
        var cut = Render<NavMenu>();

        var hrefs = cut.FindAll("a").Select(a => a.GetAttribute("href")).ToList();

        hrefs.Should().OnlyHaveUniqueItems();
        hrefs.Should().HaveCount(6);
    }
}
