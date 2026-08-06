using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ServerBackup.Data;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ServerBackup.Cli.Commands;

public sealed class JobsCommand : AsyncCommand<JobsCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<REPO>")]
        [Description("Depo dizini.")]
        public required string Repo { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        await using var db = CatalogDbContextFactory.Create(System.IO.Path.Combine(settings.Repo, "catalog.db"));

        // SQLite can't translate ORDER BY on DateTimeOffset — sort client-side.
        var jobs = (await db.Jobs.AsNoTracking().ToListAsync(cancellationToken))
            .OrderByDescending(j => j.StartedAtUtc)
            .ToList();

        var table = new Table();
        table.AddColumn("İş");
        table.AddColumn("Plan");
        table.AddColumn("Durum");
        table.AddColumn("Başlangıç");
        table.AddColumn("Bitiş");
        table.AddColumn("Hata");

        foreach (var job in jobs)
        {
            var statusColor = job.Status switch
            {
                "Succeeded" => "green",
                "Failed" => "red",
                "Running" => "yellow",
                _ => "grey",
            };

            table.AddRow(
                job.JobId.EscapeMarkup(),
                (job.PlanId ?? "-").EscapeMarkup(),
                $"[{statusColor}]{job.Status.EscapeMarkup()}[/]",
                job.StartedAtUtc?.ToString("u") ?? "-",
                job.FinishedAtUtc?.ToString("u") ?? "-",
                (job.ErrorMessage ?? "-").EscapeMarkup());
        }

        AnsiConsole.Write(table);
        return 0;
    }
}
