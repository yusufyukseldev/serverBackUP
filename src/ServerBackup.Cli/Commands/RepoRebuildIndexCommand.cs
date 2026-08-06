using System.ComponentModel;
using System.Security.Cryptography;
using ServerBackup.Engine.Repository;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ServerBackup.Cli.Commands;

public sealed class RepoRebuildIndexCommand : AsyncCommand<RepoRebuildIndexCommand.Settings>
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

        var masterKey = await RepositoryKeyStore.UnlockAsync(settings.Path, password);
        try
        {
            var result = await RepositoryManager.RebuildIndexAsync(settings.Path, masterKey);
            AnsiConsole.MarkupLine(
                $"[green]Katalog yeniden oluşturuldu:[/] {result.PackCount} pack, {result.BlobCount} blob.");
            if (result.SkippedPacks.Count > 0)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]{result.SkippedPacks.Count} pack dosyası atlandı (tamamlanmamış/bozuk):[/]");
                foreach (var path in result.SkippedPacks)
                {
                    AnsiConsole.MarkupLine($"  [grey]{path}[/]");
                }
            }

            return 0;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }
}
