using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ServerBackup.Data;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ServerBackup.Cli.Commands;

public sealed class PlanListCommand : AsyncCommand<PlanListCommand.Settings>
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
        var plans = await db.Plans.AsNoTracking().ToListAsync(cancellationToken);

        var table = new Table();
        table.AddColumn("Plan");
        table.AddColumn("İsim");
        table.AddColumn("Cron");
        table.AddColumn("Kaynaklar");

        foreach (var plan in plans)
        {
            table.AddRow(
                plan.PlanId.EscapeMarkup(),
                plan.Name.EscapeMarkup(),
                (plan.CronSchedule ?? "-").EscapeMarkup(),
                plan.SourcePathsJson.EscapeMarkup());
        }

        AnsiConsole.Write(table);
        return 0;
    }
}
