using Bunit;
using FluentAssertions;
using ServerBackup.Service.Components.Ui;
using Xunit;

namespace ServerBackup.Service.Tests.Components;

/// <summary>
/// The picker serves two jobs with opposite rules: a plan collects a set of
/// source paths, a restore names exactly one target directory.
/// </summary>
public sealed class SbPathPickerTests : BunitContext, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sb-picker-" + Guid.NewGuid().ToString("n"));

    public SbPathPickerTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Muhasebe"));
        Directory.CreateDirectory(Path.Combine(_root, "Raporlar"));
    }

    [Fact]
    public void Single_mode_replaces_the_selection_instead_of_appending_to_it()
    {
        IReadOnlyList<string>? chosen = null;
        var cut = RenderPicker(single: true, paths: [_root], onChange: v => chosen = v);

        ClickLabelled(cut, "Raporlar klasörünü hedef olarak seç");

        chosen.Should().Equal([Path.Combine(_root, "Raporlar")]);
    }

    [Fact]
    public void Multi_mode_appends_so_a_plan_can_cover_several_roots()
    {
        IReadOnlyList<string>? chosen = null;
        var cut = RenderPicker(single: false, paths: [_root], onChange: v => chosen = v);

        ClickLabelled(cut, $"{Path.Combine(_root, "Raporlar")} yolunu kaynak yollara ekle");

        chosen.Should().Equal([_root, Path.Combine(_root, "Raporlar")]);
    }

    [Fact]
    public void It_opens_where_the_current_choice_is_rather_than_at_the_volume_root()
    {
        var cut = RenderPicker(single: true, paths: [_root], onChange: _ => { });

        // Its children are listed, which only happens if _root is the open folder.
        cut.Markup.Should().Contain("Muhasebe").And.Contain("Raporlar");
    }

    [Fact]
    public void A_folder_that_no_longer_exists_does_not_stop_the_picker_from_opening()
    {
        var cut = RenderPicker(single: true, paths: [Path.Combine(_root, "silinmis")], onChange: _ => { });

        cut.Find(".sb-picker-list").Should().NotBeNull();
    }

    /// <summary>A Windows path in a CSS attribute selector reads as escape sequences, so match in C# instead.</summary>
    private static void ClickLabelled(IRenderedComponent<SbPathPicker> cut, string ariaLabel) =>
        cut.FindAll("button").Single(b => b.GetAttribute("aria-label") == ariaLabel).Click();

    private IRenderedComponent<SbPathPicker> RenderPicker(
        bool single, IReadOnlyList<string> paths, Action<IReadOnlyList<string>> onChange) =>
        Render<SbPathPicker>(p => p
            .Add(c => c.Single, single)
            .Add(c => c.Paths, paths)
            .Add(c => c.PathsChanged, onChange));

    public new void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        base.Dispose();
    }
}
