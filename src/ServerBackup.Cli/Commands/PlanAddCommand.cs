using System.ComponentModel;
using System.Text.Json;
using ServerBackup.Data;
using ServerBackup.Data.Entities;
using ServerBackup.Engine.Retention;
using ServerBackup.Engine.Scheduling;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ServerBackup.Cli.Commands;

public sealed class PlanAddCommand : AsyncCommand<PlanAddCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<REPO>")]
        [Description("Depo dizini.")]
        public required string Repo { get; init; }

        [CommandOption("--name")]
        public required string Name { get; init; }

        [CommandOption("--source")]
        [Description("Yedeklenecek yol (tekrarlanabilir).")]
        public required string[] Sources { get; init; }

        [CommandOption("--cron")]
        [Description("Cron ifadesi (dakika saat gün ay haftagünü), örn: '0 3 * * *' (her gün 03:00).")]
        public required string Cron { get; init; }

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
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        // Fail fast on a bad cron expression rather than letting the service discover it later.
        Cronos.CronExpression.Parse(settings.Cron, Cronos.CronFormat.Standard);

        var hasRetention = settings.KeepLast is not null || settings.KeepDaily is not null || settings.KeepWeekly is not null
            || settings.KeepMonthly is not null || settings.KeepYearly is not null;
        var retentionJson = hasRetention
            ? JsonSerializer.Serialize(new RetentionPolicy(
                settings.KeepLast, null, settings.KeepDaily, settings.KeepWeekly, settings.KeepMonthly, settings.KeepYearly))
            : null;

        await using var db = CatalogDbContextFactory.Create(System.IO.Path.Combine(settings.Repo, "catalog.db"));
        var planId = Guid.NewGuid().ToString("n");
        db.Plans.Add(new PlanEntity
        {
            PlanId = planId,
            Name = settings.Name,
            SourcePathsJson = JsonSerializer.Serialize(settings.Sources),
            CronSchedule = settings.Cron,
            RetentionPolicyJson = retentionJson,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);

        AnsiConsole.MarkupLine($"[green]Plan oluşturuldu:[/] {planId}");
        return 0;
    }
}
