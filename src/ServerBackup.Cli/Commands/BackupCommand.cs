using System.ComponentModel;
using System.Security.Cryptography;
using ServerBackup.Engine.Backup;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Scanning;
using ServerBackup.Engine.Vss;
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

        [CommandOption("--no-vss")]
        [Description("Volume Shadow Copy kullanma; dosyaları doğrudan oku (açık/kilitli dosyalar atlanabilir, yönetici gerektirmez).")]
        public bool NoVss { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var password = settings.Password ?? AnsiConsole.Prompt(
            new TextPrompt<string>("Depo parolası:").Secret());

        var masterKey = await RepositoryKeyStore.UnlockAsync(settings.Repo, password, cancellationToken);
        VssSnapshotSession? vssSession = null;
        try
        {
            ISourceProvider source = new LocalSourceProvider();

            if (!settings.NoVss)
            {
                try
                {
                    vssSession = VssSnapshotSession.Create(settings.SourcePaths);
                    source = new VssSourceProvider(source, vssSession);
                    AnsiConsole.MarkupLine("[grey]VSS gölge kopyası oluşturuldu.[/]");
                }
                catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
                {
                    AnsiConsole.MarkupLine($"[red]VSS kullanılamadı:[/] {ex.Message.EscapeMarkup()}");
                    AnsiConsole.MarkupLine("[yellow]VSS olmadan devam etmek için --no-vss kullanın.[/]");
                    return 1;
                }
            }

            BackupProgress? lastProgress = null;
            var progress = new Progress<BackupProgress>(p =>
            {
                lastProgress = p;
                AnsiConsole.MarkupLine(
                    $"[grey]{p.FilesScanned} dosya ({p.FilesUnchanged} değişmedi, {p.FilesChanged} değişti) — {p.NewBlobsWritten} yeni blob, {p.NewBytesWritten:N0} bayt[/]");
            });

            var filter = new ScanFilter(settings.SourcePaths[0]);
            var engine = new BackupEngine(source, settings.Repo, masterKey, filter: filter, progress: progress);
            var snapshotId = await engine.RunAsync(settings.SourcePaths, settings.Parent, ct: cancellationToken);

            AnsiConsole.MarkupLine($"[green]Yedekleme tamamlandı.[/] Snapshot: [bold]{snapshotId}[/]");
            if (lastProgress is { EntriesSkipped: > 0 } p)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]{p.EntriesSkipped} öğe okunamadı ve atlandı[/] (izin reddedildi veya dosya kilitli). Ayrıntılar depo denetim kaydında.");
            }

            return 0;
        }
        finally
        {
            vssSession?.Dispose();
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }
}
