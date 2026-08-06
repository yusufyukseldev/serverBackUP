using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ServerBackup.Data;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ServerBackup.Cli.Commands;

public sealed class SnapshotsCommand : AsyncCommand<SnapshotsCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<REPO>")]
        [Description("Depo dizini.")]
        public required string Repo { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        await using var db = CatalogDbContextFactory.Create(Path.Combine(settings.Repo, "catalog.db"));

        // SQLite can't translate ORDER BY on DateTimeOffset — sort client-side.
        var snapshots = (await db.Snapshots.AsNoTracking().ToListAsync(cancellationToken))
            .OrderByDescending(s => s.StartedAtUtc)
            .ToList();

        var table = new Table();
        table.AddColumn("Snapshot");
        table.AddColumn("Başlangıç (UTC)");
        table.AddColumn("Bitiş (UTC)");
        table.AddColumn("Üst Snapshot");

        foreach (var snapshot in snapshots)
        {
            table.AddRow(
                snapshot.SnapshotId,
                snapshot.StartedAtUtc.ToString("u"),
                snapshot.FinishedAtUtc?.ToString("u") ?? "-",
                snapshot.ParentSnapshotId ?? "-");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}
