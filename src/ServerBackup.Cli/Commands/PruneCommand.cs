using System.ComponentModel;
using System.Security.Cryptography;
using ServerBackup.Engine.Prune;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Retention;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ServerBackup.Cli.Commands;

public sealed class PruneCommand : AsyncCommand<PruneCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<REPO>")]
        [Description("Depo dizini.")]
        public required string Repo { get; init; }

        [CommandOption("--password")]
        [Description("Depo parolası. Verilmezse güvenli şekilde sorulur.")]
        public string? Password { get; init; }

        [CommandOption("--keep-last")]
        public int? KeepLast { get; init; }

        [CommandOption("--keep-daily")]
        public int? KeepDaily { get; init; }

        [CommandOption("--keep-weekly")]
        public int? KeepWeekly { get; init; }

        [CommandOption("--keep-monthly")]
        public int? KeepMonthly { get; init; }

        [CommandOption("--keep-yearly")]
        public int? KeepYearly { get; init; }

        [CommandOption("--plan")]
        [Description("Sadece bu plana ait snapshot'lara uygula. Verilmezse depodaki tüm snapshot'lar değerlendirilir.")]
        public string? PlanId { get; init; }

        [CommandOption("--snapshot")]
        [Description("Bu snapshot'ı sil (birden çok kez verilebilir). Verilirse saklama kuralları yerine sadece bu snapshot'lar silinir.")]
        public string[] SnapshotIds { get; init; } = [];

        [CommandOption("--apply")]
        [Description("Gerçekten sil/repack et. Verilmezse sadece ne yapılacağı gösterilir (dry-run, varsayılan).")]
        public bool Apply { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var password = settings.Password ?? AnsiConsole.Prompt(
            new TextPrompt<string>("Depo parolası:").Secret());

        var masterKey = await RepositoryKeyStore.UnlockAsync(settings.Repo, password, cancellationToken);
        try
        {
            if (settings.SnapshotIds.Length > 0)
            {
                return await RunManualAsync(settings, masterKey, cancellationToken);
            }

            var policy = new RetentionPolicy(
                KeepLast: settings.KeepLast,
                KeepDaily: settings.KeepDaily,
                KeepWeekly: settings.KeepWeekly,
                KeepMonthly: settings.KeepMonthly,
                KeepYearly: settings.KeepYearly);

            var engine = new PruneEngine(settings.Repo, masterKey);
            var result = await engine.RunAsync(policy, dryRun: !settings.Apply, planId: settings.PlanId, ct: cancellationToken);

            var mode = result.DryRun ? "[yellow](dry-run — hiçbir şey silinmedi)[/]" : "[green](uygulandı)[/]";
            AnsiConsole.MarkupLine($"Prune {mode}");
            AnsiConsole.MarkupLine($"  Silinecek snapshot: {result.SnapshotsToDelete.Count}");
            AnsiConsole.MarkupLine($"  Silinecek pack: {result.PacksToDelete.Count}");
            AnsiConsole.MarkupLine($"  Repack edilecek pack: {result.PacksToRepack.Count}");
            if (!result.DryRun)
            {
                AnsiConsole.MarkupLine($"  Boşaltılan alan: {result.BytesFreed:N0} bayt");
            }

            return 0;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    private static async Task<int> RunManualAsync(Settings settings, byte[] masterKey, CancellationToken ct)
    {
        var engine = new PruneEngine(settings.Repo, masterKey);
        var result = await engine.RunManualAsync(settings.SnapshotIds, dryRun: !settings.Apply, ct: ct);

        var mode = result.DryRun ? "[yellow](dry-run — hiçbir şey silinmedi)[/]" : "[green](uygulandı)[/]";
        AnsiConsole.MarkupLine($"Elle snapshot silme {mode}");
        AnsiConsole.MarkupLine($"  Silinecek snapshot: {result.SnapshotsToDelete.Count}");
        AnsiConsole.MarkupLine($"  Silinecek pack: {result.PacksToDelete.Count}");
        AnsiConsole.MarkupLine($"  Repack edilecek pack: {result.PacksToRepack.Count}");
        if (!result.DryRun)
        {
            AnsiConsole.MarkupLine($"  Boşaltılan alan: {result.BytesFreed:N0} bayt");
        }

        foreach (var refusal in result.Refused)
        {
            AnsiConsole.MarkupLine($"  [red]Silinemedi[/] {refusal.SnapshotId.EscapeMarkup()}: {Explain(refusal.Reason)}");
        }

        return result.Refused.Count > 0 ? 1 : 0;
    }

    private static string Explain(ManualPruneRefusalReason reason) => reason switch
    {
        ManualPruneRefusalReason.AppendOnly => "depo yalnızca-ekleme modunda",
        ManualPruneRefusalReason.ImmutabilityWindow => "snapshot değişmezlik penceresinde",
        ManualPruneRefusalReason.NotFound => "bu depoda böyle bir snapshot yok",
        _ => "bilinmeyen sebep",
    };
}
