using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using ServerBackup.Service.Components.Layout;
using Xunit;

namespace ServerBackup.Service.Tests.Components;

public sealed class PageHeaderTests : BunitContext
{
    [Fact]
    public void Renders_the_title_as_a_heading()
    {
        var cut = Render<PageHeader>(p => p.Add(c => c.Title, "Planlar"));

        cut.Find("h1").TextContent.Should().Be("Planlar");
    }

    [Fact]
    public void Renders_actions_when_provided()
    {
        var cut = Render<PageHeader>(p => p
            .Add(c => c.Title, "Planlar")
            .Add(c => c.Actions, (RenderFragment)(builder => builder.AddContent(0, "Yeni plan"))));

        cut.Markup.Should().Contain("Yeni plan");
    }

    [Fact]
    public void Omits_the_actions_row_when_none_are_given()
    {
        var cut = Render<PageHeader>(p => p.Add(c => c.Title, "Planlar"));

        cut.Markup.Should().NotContain("sb-row");
    }
}
