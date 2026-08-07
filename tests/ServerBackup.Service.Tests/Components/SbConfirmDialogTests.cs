using Bunit;
using FluentAssertions;
using ServerBackup.Service.Components.Ui;
using Xunit;

namespace ServerBackup.Service.Tests.Components;

public sealed class SbConfirmDialogTests : BunitContext
{
    [Fact]
    public void Confirm_button_is_disabled_until_the_confirm_word_is_typed_correctly()
    {
        var cut = Render<SbConfirmDialog>(p => p
            .Add(c => c.Title, "47 snapshot kalıcı olarak silinecek")
            .Add(c => c.Consequence, "Bu işlem geri alınamaz.")
            .Add(c => c.ConfirmWord, "Muhasebe Arşivi")
            .Add(c => c.ConfirmLabel, "47 snapshot'ı sil"));

        var confirmButton = cut.FindAll("button").Single(b => b.TextContent.Contains("47 snapshot'ı sil"));
        confirmButton.HasAttribute("disabled").Should().BeTrue();

        var input = cut.Find("input");
        input.Input("yanlış kelime");
        confirmButton = cut.FindAll("button").Single(b => b.TextContent.Contains("47 snapshot'ı sil"));
        confirmButton.HasAttribute("disabled").Should().BeTrue();

        input.Input("Muhasebe Arşivi");
        confirmButton = cut.FindAll("button").Single(b => b.TextContent.Contains("47 snapshot'ı sil"));
        confirmButton.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Confirm_button_is_enabled_immediately_when_no_confirm_word_is_required()
    {
        var cut = Render<SbConfirmDialog>(p => p
            .Add(c => c.Title, "Snapshot silinecek")
            .Add(c => c.Consequence, "Bu işlem geri alınamaz.")
            .Add(c => c.ConfirmLabel, "Sil"));

        var confirmButton = cut.FindAll("button").Single(b => b.TextContent.Contains("Sil"));
        confirmButton.HasAttribute("disabled").Should().BeFalse();
    }
}
