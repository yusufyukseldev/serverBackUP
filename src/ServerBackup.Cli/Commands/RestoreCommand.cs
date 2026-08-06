using System.ComponentModel;
using System.Security.Cryptography;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Restore;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ServerBackup.Cli.Commands;

public sealed class RestoreCommand : AsyncCommand<RestoreCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<REPO>")]
        [Description("Depo dizini.")]
        public required string Repo { get; init; }

        [CommandArgument(1, "<SNAPSHOT_ID>")]
        [Description("Geri yüklenecek snapshot kimliği.")]
        public required string SnapshotId { get; init; }

        [CommandArgument(2, "<TARGET>")]
        [Description("Geri yükleme hedef dizini.")]
        public required string Target { get; init; }

        [CommandOption("--password")]
        [Description("Depo parolası. Verilmezse güvenli şekilde sorulur.")]
        public string? Password { get; init; }

        [CommandOption("--path")]
        [Description("Sadece belirtilen yol(lar)ı geri yükle (tekrarlanabilir).")]
        public string[]? Paths { get; init; }

        [CommandOption("--overwrite")]
        [Description("Var olan dosyalarla karşılaşınca ne yapılacağı: overwrite (varsayılan), skip, fail.")]
        [DefaultValue("overwrite")]
        public string Overwrite { get; init; } = "overwrite";
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var password = settings.Password ?? AnsiConsole.Prompt(
            new TextPrompt<string>("Depo parolası:").Secret());

        var masterKey = await RepositoryKeyStore.UnlockAsync(settings.Repo, password, cancellationToken);
        try
        {
            var policy = settings.Overwrite.ToLowerInvariant() switch
            {
                "overwrite" => OverwritePolicy.Overwrite,
                "skip" => OverwritePolicy.Skip,
                "fail" => OverwritePolicy.Fail,
                _ => throw new ArgumentException($"Bilinmeyen --overwrite değeri: '{settings.Overwrite}'."),
            };

            var engine = new RestoreEngine(settings.Repo, masterKey);
            await engine.RestoreAsync(settings.SnapshotId, settings.Target, settings.Paths, policy, cancellationToken);

            AnsiConsole.MarkupLine($"[green]Geri yükleme tamamlandı:[/] {settings.Target}");
            return 0;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }
}
