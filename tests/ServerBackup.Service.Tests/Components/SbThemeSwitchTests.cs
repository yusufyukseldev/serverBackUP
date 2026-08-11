using Bunit;
using FluentAssertions;
using ServerBackup.Service.Components.Ui;
using Xunit;

namespace ServerBackup.Service.Tests.Components;

/// <summary>
/// The preference lives in the browser (localStorage + a data-theme attribute
/// on &lt;html&gt;), so what this component owes is exactly two things: report
/// the stored choice, and hand a new one to the same JS that applies it.
/// </summary>
public sealed class SbThemeSwitchTests : BunitContext
{
    [Fact]
    public void Marks_the_stored_preference_as_the_pressed_option()
    {
        JSInterop.Setup<string>("sbTheme.get").SetResult("dark");

        var cut = Render<SbThemeSwitch>();

        var pressed = cut.FindAll("button[aria-pressed=true]");
        pressed.Should().ContainSingle().Which.GetAttribute("title").Should().Be("Koyu tema");
    }

    [Fact]
    public void Clicking_an_option_stores_it_and_moves_the_pressed_state()
    {
        JSInterop.Setup<string>("sbTheme.get").SetResult("system");
        var set = JSInterop.Setup<string>("sbTheme.set", "light").SetResult("light");

        var cut = Render<SbThemeSwitch>();
        cut.FindAll("button").Single(b => b.GetAttribute("title") == "Açık tema").Click();

        set.Invocations.Should().ContainSingle();
        cut.FindAll("button[aria-pressed=true]").Should().ContainSingle()
            .Which.GetAttribute("title").Should().Be("Açık tema");
    }

    [Fact]
    public void Offers_system_light_and_dark_each_with_a_screen_reader_name()
    {
        JSInterop.Setup<string>("sbTheme.get").SetResult("system");

        var cut = Render<SbThemeSwitch>();

        cut.FindAll(".sb-theme-btn .sb-sr-only").Select(e => e.TextContent)
            .Should().Equal("Sistem ayarını kullan", "Açık tema", "Koyu tema");
    }
}
