using System.ComponentModel;
using ServerBackup.Engine.Repository;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ServerBackup.Cli.Commands;

public sealed class RepoDisableUnattendedCommand : Command<RepoDisableUnattendedCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<PATH>")]
        [Description("Depo dizini.")]
        public required string Path { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        UnattendedKeyStore.Disable(settings.Path);
        AnsiConsole.MarkupLine("[green]Parolasız (servis) erişim devre dışı bırakıldı.[/]");
        return 0;
    }
}
