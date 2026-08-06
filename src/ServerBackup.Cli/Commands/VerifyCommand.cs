using System.ComponentModel;
using System.Security.Cryptography;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Verify;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ServerBackup.Cli.Commands;

public sealed class VerifyCommand : AsyncCommand<VerifyCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<REPO>")]
        [Description("Depo dizini.")]
        public required string Repo { get; init; }

        [CommandOption("--password")]
        [Description("Depo parolası. Verilmezse güvenli şekilde sorulur.")]
        public string? Password { get; init; }

        [CommandOption("--full")]
        [Description("Her blobu çözüp içerik kimliğini yeniden hesapla (en yavaş, en derin kontrol).")]
        public bool Full { get; init; }

        [CommandOption("--packs")]
        [Description("Pack dosyalarının SHA-256'sını doğrula.")]
        public bool Packs { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var password = settings.Password ?? AnsiConsole.Prompt(
            new TextPrompt<string>("Depo parolası:").Secret());

        var masterKey = await RepositoryKeyStore.UnlockAsync(settings.Repo, password, cancellationToken);
        try
        {
            var level = settings.Full ? VerifyLevel.Full : settings.Packs ? VerifyLevel.Packs : VerifyLevel.Index;

            var engine = new VerifyEngine(settings.Repo, masterKey);
            var issues = await engine.RunAsync(level, cancellationToken);

            if (issues.Count == 0)
            {
                AnsiConsole.MarkupLine($"[green]Sorun bulunamadı[/] ({level} seviyesinde).");
                return 0;
            }

            AnsiConsole.MarkupLine($"[red]{issues.Count} sorun bulundu[/] ({level} seviyesinde):");
            foreach (var issue in issues)
            {
                AnsiConsole.MarkupLine($"  [yellow][[{issue.Category}]][/] {issue.Description}");
            }

            return 1;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }
}
