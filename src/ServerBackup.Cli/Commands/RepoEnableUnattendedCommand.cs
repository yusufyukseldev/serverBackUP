using System.ComponentModel;
using System.Security.Cryptography;
using ServerBackup.Engine.Repository;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ServerBackup.Cli.Commands;

public sealed class RepoEnableUnattendedCommand : AsyncCommand<RepoEnableUnattendedCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<PATH>")]
        [Description("Depo dizini.")]
        public required string Path { get; init; }

        [CommandOption("--password")]
        [Description("Depo parolası. Verilmezse güvenli şekilde sorulur.")]
        public string? Password { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var password = settings.Password ?? AnsiConsole.Prompt(
            new TextPrompt<string>("Depo parolası:").Secret());

        var masterKey = await RepositoryKeyStore.UnlockAsync(settings.Path, password, cancellationToken);
        try
        {
            UnattendedKeyStore.Enable(settings.Path, masterKey);
            AnsiConsole.MarkupLine("[green]Parolasız (servis) erişim etkinleştirildi.[/]");
            AnsiConsole.MarkupLine(
                "[yellow]Uyarı:[/] Bu makinede SYSTEM/yönetici yetkisi ele geçiren biri artık bu depoyu açabilir " +
                "(DPAPI LocalMachine kapsamı). Bu bilinçli bir takas — kapatmak için 'repo disable-unattended'.");
            return 0;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }
}
