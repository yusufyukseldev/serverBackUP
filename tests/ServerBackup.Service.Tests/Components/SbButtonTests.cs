using Bunit;
using FluentAssertions;
using ServerBackup.Service.Components.Ui;
using Xunit;

namespace ServerBackup.Service.Tests.Components;

public sealed class SbButtonTests : BunitContext
{
    [Fact]
    public void Disabled_without_a_reason_throws()
    {
        var act = () => Render<SbButton>(p => p.Add(c => c.Disabled, true));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Disabled_with_a_reason_renders_it_as_the_title()
    {
        var cut = Render<SbButton>(p => p
            .Add(c => c.Disabled, true)
            .Add(c => c.DisabledReason, "Depo kilitli"));

        cut.Find("button").GetAttribute("title").Should().Be("Depo kilitli");
    }
}
