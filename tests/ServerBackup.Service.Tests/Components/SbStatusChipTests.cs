using Bunit;
using FluentAssertions;
using ServerBackup.Service.Components.Ui;
using Xunit;

namespace ServerBackup.Service.Tests.Components;

public sealed class SbStatusChipTests : BunitContext
{
    [Theory]
    [InlineData("Succeeded", "sb-status--ok", "Başarılı")]
    [InlineData("Running", "sb-status--run", "Sürüyor")]
    [InlineData("Pending", "sb-status--pending", "Bekliyor")]
    [InlineData("Warning", "sb-status--warn", "Uyarı")]
    [InlineData("Failed", "sb-status--err", "Hata")]
    [InlineData("Cancelled", "sb-status--muted", "İptal edildi")]
    [InlineData("Locked", "sb-status--locked", "Korumalı")]
    public void Maps_all_seven_dictionary_statuses_to_the_right_class_and_default_text(string status, string expectedClass, string expectedText)
    {
        var cut = Render<SbStatusChip>(p => p.Add(c => c.Status, status));

        cut.Find("span").ClassList.Should().Contain(expectedClass);
        cut.Markup.Should().Contain(expectedText);
    }

    [Fact]
    public void An_explicit_Text_overrides_the_default_turkish_label()
    {
        var cut = Render<SbStatusChip>(p => p.Add(c => c.Status, "Failed").Add(c => c.Text, "3 dosya başarısız"));

        cut.Markup.Should().Contain("3 dosya başarısız");
        cut.Markup.Should().NotContain(">Hata<");
    }
}
