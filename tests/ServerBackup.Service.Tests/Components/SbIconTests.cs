using Bunit;
using FluentAssertions;
using ServerBackup.Service.Components.Ui;
using Xunit;

namespace ServerBackup.Service.Tests.Components;

public sealed class SbIconTests : BunitContext
{
    [Fact]
    public void Renders_a_use_element_pointing_at_the_named_sprite_symbol()
    {
        var cut = Render<SbIcon>(p => p.Add(c => c.Name, "gauge"));

        cut.Find("use").GetAttribute("href").Should().Be("icons.svg#i-gauge");
    }

    [Fact]
    public void Small_adds_the_ic_sm_modifier_class()
    {
        var cut = Render<SbIcon>(p => p.Add(c => c.Name, "db").Add(c => c.Small, true));

        cut.Find("svg").ClassList.Should().Contain("ic-sm");
    }
}
