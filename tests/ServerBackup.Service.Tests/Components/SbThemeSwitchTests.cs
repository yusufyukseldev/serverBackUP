using Bunit;
using FluentAssertions;
using ServerBackup.Service.Components.Ui;
using Xunit;

namespace ServerBackup.Service.Tests.Components;

/// <summary>
/// The preference lives in the browser (localStorage + a data-theme attribute
/// on &lt;html&gt;), so what this component owes is exactly two things: name the
/// theme a click would give you, and hand the toggle to the JS that applies it.
/// </summary>
public sealed class SbThemeSwitchTests : BunitContext
{
    [Fact]
    public void In_dark_theme_it_offers_the_light_one()
    {
        JSInterop.Setup<string>("sbTheme.get").SetResult("dark");

        var cut = Render<SbThemeSwitch>();

        cut.Find("button").GetAttribute("aria-label").Should().Be("Açık temaya geç");
        cut.Markup.Should().Contain("#i-sun");
    }

    [Fact]
    public void In_light_theme_it_offers_the_dark_one()
    {
        JSInterop.Setup<string>("sbTheme.get").SetResult("light");

        var cut = Render<SbThemeSwitch>();

        cut.Find("button").GetAttribute("aria-label").Should().Be("Koyu temaya geç");
        cut.Markup.Should().Contain("#i-moon");
    }

    [Fact]
    public void Clicking_toggles_through_js_and_flips_what_is_offered()
    {
        JSInterop.Setup<string>("sbTheme.get").SetResult("dark");
        var toggle = JSInterop.Setup<string>("sbTheme.toggle").SetResult("light");

        var cut = Render<SbThemeSwitch>();
        cut.Find("button").Click();

        toggle.Invocations.Should().ContainSingle();
        cut.Find("button").GetAttribute("aria-label").Should().Be("Koyu temaya geç");
    }
}
