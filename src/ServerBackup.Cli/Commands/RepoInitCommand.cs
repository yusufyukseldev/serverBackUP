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
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var password = settings.Password ?? AnsiConsole.Prompt(
            new TextPrompt<string>("Depo parolası:").Secret());

        await RepositoryManager.InitializeAsync(settings.Path, password);

        AnsiConsole.MarkupLine($"[green]Depo oluşturuldu:[/] {settings.Path}");
        return 0;
    }
}
