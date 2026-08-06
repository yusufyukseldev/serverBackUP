using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ServerBackup.Core.Repository;
using ServerBackup.Data;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ServerBackup.Cli.Commands;

public sealed class RepoInfoCommand : AsyncCommand<RepoInfoCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<PATH>")]
        [Description("Depo dizini.")]
        public required string Path { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var configPath = System.IO.Path.Combine(settings.Path, "config.json");
        if (!File.Exists(configPath))
        {
            AnsiConsole.MarkupLine($"[red]'{settings.Path}' bir ServerBackup deposu değil (config.json yok).[/]");
            return 1;
        }

        var config = JsonSerializer.Deserialize<RepositoryConfig>(await File.ReadAllTextAsync(configPath))
            ?? throw new InvalidDataException("config.json ayrıştırılamadı.");

        AnsiConsole.MarkupLine($"Depo kimliği: [bold]{config.RepositoryId}[/]");
        AnsiConsole.MarkupLine($"Format sürümü: {config.FormatVersion}");
        AnsiConsole.MarkupLine($"Oluşturulma: {config.CreatedAtUtc:u}");
        if (config.AppendOnly)
        {
            AnsiConsole.MarkupLine("[yellow]Append-only:[/] açık (hiçbir snapshot silinemez)");
        }
        else if (config.ImmutabilityWindowDays is { } days)
        {
            AnsiConsole.MarkupLine($"[yellow]Immutability penceresi:[/] {days} gün");
        }

        var dbPath = System.IO.Path.Combine(settings.Path, "catalog.db");
        if (File.Exists(dbPath))
        {
            await using var db = CatalogDbContextFactory.Create(dbPath);
            var packCount = await db.Packs.CountAsync();
            var blobCount = await db.Blobs.CountAsync();
            var snapshotCount = await db.Snapshots.CountAsync();
            var totalBytes = await db.Packs.SumAsync(p => (long?)p.SizeBytes) ?? 0;

            AnsiConsole.MarkupLine($"Pack sayısı: {packCount}");
            AnsiConsole.MarkupLine($"Blob sayısı: {blobCount}");
            AnsiConsole.MarkupLine($"Snapshot sayısı: {snapshotCount}");
            AnsiConsole.MarkupLine($"Toplam depo boyutu: {totalBytes:N0} bayt");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]catalog.db bulunamadı — 'repo rebuild-index' çalıştırılabilir.[/]");
        }

        return 0;
    }
}
