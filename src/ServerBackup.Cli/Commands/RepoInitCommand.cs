using System.ComponentModel;
using ServerBackup.Engine.Repository;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ServerBackup.Cli.Commands;

public sealed class RepoInitCommand : AsyncCommand<RepoInitCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<PATH>")]
        [Description("Yeni deponun oluşturulacağı dizin.")]
        public required string Path { get; init; }

        [CommandOption("--password")]
        [Description("Depo parolası. Verilmezse güvenli şekilde sorulur.")]
        public string? Password { get; init; }

        [CommandOption("--immutability-days")]
        [Description("Bu kadar gün içindeki snapshot'lar prune tarafından ASLA silinemez (panelden bile) — fidye yazılımı koruması.")]
        public int? ImmutabilityDays { get; init; }

        [CommandOption("--append-only")]
        [Description("Hiçbir snapshot hiçbir zaman silinemez (immutability-days'ten daha güçlü).")]
        public bool AppendOnly { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var password = settings.Password ?? AnsiConsole.Prompt(
            new TextPrompt<string>("Depo parolası:").Secret());

        await RepositoryManager.InitializeAsync(
            settings.Path, password, settings.ImmutabilityDays, settings.AppendOnly, cancellationToken);

        AnsiConsole.MarkupLine($"[green]Depo oluşturuldu:[/] {settings.Path}");
        if (settings.AppendOnly)
        {
            AnsiConsole.MarkupLine("[yellow]Append-only mod: hiçbir snapshot asla silinemeyecek.[/]");
        }
        else if (settings.ImmutabilityDays is { } days)
        {
            AnsiConsole.MarkupLine($"[yellow]Immutability penceresi: son {days} günün snapshot'ları silinemez.[/]");
        }

        return 0;
    }
}
