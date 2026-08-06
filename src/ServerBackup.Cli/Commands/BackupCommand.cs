using System.ComponentModel;
using System.Security.Cryptography;
using ServerBackup.Engine.Backup;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Scanning;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ServerBackup.Cli.Commands;

public sealed class BackupCommand : AsyncCommand<BackupCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<REPO>")]
        [Description("Depo dizini.")]
        public required string Repo { get; init; }

        [CommandArgument(1, "<SOURCE_PATHS>")]
        [Description("Yedeklenecek bir veya daha fazla dosya/klasör yolu.")]
        public required string[] SourcePaths { get; init; }

        [CommandOption("--password")]
        [Description("Depo parolası. Verilmezse güvenli şekilde sorulur.")]
        public string? Password { get; init; }

        [CommandOption("--parent")]
        [Description("Incremental için önceki snapshot kimliği. Verilmezse tam tarama yapılır.")]
        public string? Parent { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var password = settings.Password ?? AnsiConsole.Prompt(
            new TextPrompt<string>("Depo parolası:").Secret());

        var masterKey = await RepositoryKeyStore.UnlockAsync(settings.Repo, password, cancellationToken);
        try
        {
            var progress = new Progress<BackupProgress>(p =>
                AnsiConsole.MarkupLine(
                    $"[grey]{p.FilesScanned} dosya ({p.FilesUnchanged} değişmedi, {p.FilesChanged} değişti) — {p.NewBlobsWritten} yeni blob, {p.NewBytesWritten:N0} bayt[/]"));

            var engine = new BackupEngine(new LocalSourceProvider(), settings.Repo, masterKey, progress: progress);
            var snapshotId = await engine.RunAsync(settings.SourcePaths, settings.Parent, cancellationToken);

            AnsiConsole.MarkupLine($"[green]Yedekleme tamamlandı.[/] Snapshot: [bold]{snapshotId}[/]");
            return 0;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }
}
