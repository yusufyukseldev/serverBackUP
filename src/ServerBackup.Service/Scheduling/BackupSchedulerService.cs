using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServerBackup.Data;
using ServerBackup.Data.Entities;
using ServerBackup.Engine.Backup;
using ServerBackup.Engine.Notifications;
using ServerBackup.Engine.Prune;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Retention;
using ServerBackup.Engine.Scanning;
using ServerBackup.Engine.Scheduling;
using ServerBackup.Engine.Verify;
using ServerBackup.Service.Storage;

namespace ServerBackup.Service.Scheduling;

/// <summary>
/// Polls every configured repository's Plans on a timer, enqueues due ones,
/// and runs them through a bounded worker pool — see plan Faz 9. A backup
/// mid-run is cancelled promptly on service stop (BackupEngine already
/// leaves the repository consistent on cancellation, per Faz 5); the queue
/// itself is drained so already-enqueued jobs still get a final status
/// recorded instead of vanishing silently.
/// </summary>
public sealed class BackupSchedulerService(
    IOptions<ServerBackupOptions> options,
    RepositoryRegistry repositories,
    JobQueue queue,
    INotifier notifier,
    ILogger<BackupSchedulerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerCount = Math.Max(1, options.Value.MaxConcurrentJobs);
        var workers = Enumerable.Range(0, workerCount)
            .Select(_ => WorkerLoopAsync(stoppingToken))
            .ToArray();

        var pollInterval = TimeSpan.FromSeconds(Math.Max(5, options.Value.PollIntervalSeconds));
        using var timer = new PeriodicTimer(pollInterval);

        try
        {
            do
            {
                await PollAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }

        queue.Writer.TryComplete();
        await Task.WhenAll(workers);
    }

    private async Task PollAsync(CancellationToken ct)
    {
        // Read per poll, not once at startup: a repository attached from the
        // panel has to start being scheduled without restarting the service.
        foreach (var repoPath in repositories.Paths)
        {
            try
            {
                await PollRepositoryAsync(repoPath, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to poll repository {RepoPath} for due plans.", repoPath);
            }
        }
    }

    private async Task PollRepositoryAsync(string repoPath, CancellationToken ct)
    {
        var dbPath = Path.Combine(repoPath, "catalog.db");
        if (!File.Exists(dbPath))
        {
            logger.LogWarning("Repository {RepoPath} has no catalog.db — skipping.", repoPath);
            return;
        }

        await using var db = CatalogDbContextFactory.Create(dbPath);
        var plans = await db.Plans.AsNoTracking().ToListAsync(ct);

        foreach (var plan in plans)
        {
            await EnqueueIfDueAsync(db, repoPath, plan, "backup", plan.CronSchedule, ct);
            await EnqueueIfDueAsync(db, repoPath, plan, "verify", plan.VerifyCronSchedule, ct);
        }
    }

    private async Task EnqueueIfDueAsync(CatalogDbContext db, string repoPath, PlanEntity plan, string kind, string? cron, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(cron))
        {
            return;
        }

        // SQLite can't translate ORDER BY on DateTimeOffset — sort client-side.
        var lastSuccess = (await db.Jobs.AsNoTracking()
                .Where(j => j.PlanId == plan.PlanId && j.Kind == kind && j.Status == JobStatus.Succeeded)
                .ToListAsync(ct))
            .OrderByDescending(j => j.FinishedAtUtc)
            .FirstOrDefault()
            ?.FinishedAtUtc;

        if (!CronScheduleCalculator.IsDue(cron, lastSuccess, DateTimeOffset.UtcNow))
        {
            return;
        }

        var alreadyQueued = await db.Jobs.AnyAsync(
            j => j.PlanId == plan.PlanId && j.Kind == kind && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running), ct);
        if (alreadyQueued)
        {
            return;
        }

        var jobId = Guid.NewGuid().ToString("n");
        db.Jobs.Add(new JobEntity
        {
            JobId = jobId,
            PlanId = plan.PlanId,
            Kind = kind,
            Status = JobStatus.Pending,
        });
        await db.SaveChangesAsync(ct);

        await queue.Writer.WriteAsync(new ScheduledJob(repoPath, jobId, plan.PlanId, kind), ct);
        logger.LogInformation("Queued {Kind} job {JobId} for plan {PlanName} in {RepoPath}.", kind, jobId, plan.Name, repoPath);
    }

    private async Task WorkerLoopAsync(CancellationToken stoppingToken)
    {
        // Deliberately not passing stoppingToken to ReadAllAsync: on shutdown
        // we still want to drain and record a final status for jobs already
        // in the queue, rather than dropping them silently. Each individual
        // run below does respect stoppingToken.
        await foreach (var job in queue.Reader.ReadAllAsync(CancellationToken.None))
        {
            await RunJobAsync(job, stoppingToken);
        }
    }

    private async Task RunJobAsync(ScheduledJob scheduled, CancellationToken stoppingToken)
    {
        await using var db = CatalogDbContextFactory.Create(Path.Combine(scheduled.RepoPath, "catalog.db"));
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.JobId == scheduled.JobId);
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.PlanId == scheduled.PlanId);

        if (job is null || plan is null)
        {
            logger.LogWarning("Job {JobId} or plan {PlanId} disappeared before it could run.", scheduled.JobId, scheduled.PlanId);
            return;
        }

        job.Status = JobStatus.Running;
        job.StartedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);

        try
        {
            if (!UnattendedKeyStore.IsEnabled(scheduled.RepoPath))
            {
                throw new InvalidOperationException(
                    $"Repository '{scheduled.RepoPath}' is not enabled for unattended access — run 'serverbackup repo enable-unattended'.");
            }

            var masterKey = UnattendedKeyStore.Unlock(scheduled.RepoPath);
            try
            {
                if (scheduled.Kind == "verify")
                {
                    await RunVerifyJobAsync(db, job, plan, scheduled, masterKey, stoppingToken);
                }
                else
                {
                    await RunBackupJobAsync(db, job, plan, scheduled, masterKey, stoppingToken);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(masterKey);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            job.Status = JobStatus.Cancelled;
            job.FinishedAtUtc = DateTimeOffset.UtcNow;
            job.ErrorMessage = "Cancelled (service stopping).";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {JobId} for plan {PlanId} failed.", job.JobId, plan.PlanId);
            job.Status = JobStatus.Failed;
            job.FinishedAtUtc = DateTimeOffset.UtcNow;
            job.ErrorMessage = ex.Message;
            db.JobLogs.Add(new JobLogEntity
            {
                JobId = job.JobId,
                TimestampUtc = DateTimeOffset.UtcNow,
                Level = "Error",
                Message = ex.ToString(),
            });

            var title = ex is AnomalyDetectedException
                ? $"ServerBackup: OLASI FİDYE YAZILIMI / KİTLESEL DEĞİŞİM — '{plan.Name}' durduruldu"
                : scheduled.Kind == "verify"
                    ? $"ServerBackup: '{plan.Name}' doğrulaması çalıştırılamadı"
                    : $"ServerBackup: '{plan.Name}' yedeklemesi başarısız";
            notifier.Notify(title, ex.Message, NotificationSeverity.Error);
        }

        // Persist the final status even if stoppingToken is already cancelled.
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task RunBackupJobAsync(
        CatalogDbContext db, JobEntity job, PlanEntity plan, ScheduledJob scheduled, byte[] masterKey, CancellationToken stoppingToken)
    {
        var sourcePaths = JsonSerializer.Deserialize<string[]>(plan.SourcePathsJson)
            ?? throw new InvalidDataException($"Plan '{plan.PlanId}' has no valid source paths.");

        // Chain to this plan's most recent snapshot so the run is
        // incremental and anomaly detection (which needs a parent to
        // compare against) actually has something to compare against.
        var parentSnapshotId = (await db.Snapshots.AsNoTracking()
                .Where(s => s.PlanId == plan.PlanId)
                .ToListAsync(CancellationToken.None))
            .OrderByDescending(s => s.StartedAtUtc)
            .FirstOrDefault()
            ?.SnapshotId;

        // Unattended runs have no human watching, so anomaly
        // detection defaults to aborting rather than just warning —
        // see plan Faz 11.
        BackupProgress? lastProgress = null;
        var engine = new BackupEngine(
            new LocalSourceProvider(), scheduled.RepoPath, masterKey,
            filter: new ScanFilter(sourcePaths[0]),
            progress: new Progress<BackupProgress>(p => lastProgress = p),
            anomalyPolicy: new AnomalyPolicy(AbortOnDetection: true));
        var snapshotId = await engine.RunAsync(sourcePaths, parentSnapshotId, plan.PlanId, stoppingToken);

        if (!string.IsNullOrEmpty(plan.RetentionPolicyJson))
        {
            var policy = JsonSerializer.Deserialize<RetentionPolicy>(plan.RetentionPolicyJson);
            if (policy is not null)
            {
                var pruneEngine = new PruneEngine(scheduled.RepoPath, masterKey);
                await pruneEngine.RunAsync(policy, dryRun: false, planId: plan.PlanId, ct: stoppingToken);
            }
        }

        var skipped = lastProgress?.EntriesSkipped ?? 0;
        job.Status = JobStatus.Succeeded;
        job.FinishedAtUtc = DateTimeOffset.UtcNow;
        db.JobLogs.Add(new JobLogEntity
        {
            JobId = job.JobId,
            TimestampUtc = DateTimeOffset.UtcNow,
            Level = skipped > 0 ? "Warning" : "Information",
            Message = skipped > 0
                ? $"Snapshot {snapshotId} completed; {skipped} unreadable entries skipped."
                : $"Snapshot {snapshotId} completed successfully.",
        });
    }

    /// <summary>
    /// An unverified backup is a wish, not a backup — see CLAUDE.md item 4 of
    /// the panel backlog. Issues never fail silently: they land in the job
    /// log AND go through the same notifier a failed backup uses, because an
    /// operator who only checks alerts must see repository corruption too.
    /// </summary>
    private async Task RunVerifyJobAsync(
        CatalogDbContext db, JobEntity job, PlanEntity plan, ScheduledJob scheduled, byte[] masterKey, CancellationToken stoppingToken)
    {
        var level = Enum.TryParse<VerifyLevel>(plan.VerifyLevel, out var parsed) ? parsed : VerifyLevel.Packs;
        var engine = new VerifyEngine(scheduled.RepoPath, masterKey);
        var issues = await engine.RunAsync(level, stoppingToken);

        job.FinishedAtUtc = DateTimeOffset.UtcNow;

        if (issues.Count == 0)
        {
            job.Status = JobStatus.Succeeded;
            db.JobLogs.Add(new JobLogEntity
            {
                JobId = job.JobId,
                TimestampUtc = DateTimeOffset.UtcNow,
                Level = "Information",
                Message = $"Doğrulama ({level}) tamamlandı, sorun bulunamadı.",
            });
            return;
        }

        job.Status = JobStatus.Failed;
        job.ErrorMessage = $"{issues.Count} sorun bulundu.";
        foreach (var issue in issues)
        {
            db.JobLogs.Add(new JobLogEntity
            {
                JobId = job.JobId,
                TimestampUtc = DateTimeOffset.UtcNow,
                Level = "Error",
                Message = $"[{issue.Category}] {issue.Description}",
            });
        }

        notifier.Notify(
            $"ServerBackup: '{plan.Name}' deposunda doğrulama sorunu",
            $"{issues.Count} sorun bulundu. İlk sorun: {issues[0].Description}",
            NotificationSeverity.Error);
    }
}
