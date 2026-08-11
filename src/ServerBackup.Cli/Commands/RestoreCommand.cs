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

        [CommandArgument(2, "[TARGET]")]
        [Description("Geri yükleme hedef dizini. --in-place ile birlikte verilmez.")]
        public string? Target { get; init; }

        [CommandOption("--in-place")]
        [Description("Snapshot'ı alındığı orijinal konumların üzerine geri yükler.")]
        public bool InPlace { get; init; }

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

    protected override ValidationResult Validate(CommandContext context, Settings settings)
    {
        if (settings.InPlace && settings.Target is not null)
        {
            return ValidationResult.Error("--in-place ile hedef dizin birlikte verilemez.");
        }

        if (!settings.InPlace && string.IsNullOrWhiteSpace(settings.Target))
        {
            return ValidationResult.Error("Hedef dizin girin ya da --in-place kullanın.");
        }

        return ValidationResult.Success();
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

            if (settings.InPlace)
            {
                var written = await engine.RestoreInPlaceAsync(settings.SnapshotId, settings.Paths, policy, cancellationToken);
                AnsiConsole.MarkupLine($"[green]Orijinal konuma geri dönüldü:[/] {string.Join(", ", written)}");
                return 0;
            }

            await engine.RestoreAsync(settings.SnapshotId, settings.Target!, settings.Paths, policy, cancellationToken);
            AnsiConsole.MarkupLine($"[green]Geri yükleme tamamlandı:[/] {settings.Target}");
            return 0;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }
}
