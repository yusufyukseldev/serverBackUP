using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServerBackup.Data;
using ServerBackup.Data.Entities;
using ServerBackup.Engine.Backup;
using ServerBackup.Engine.Prune;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Retention;
using ServerBackup.Engine.Scanning;
using ServerBackup.Engine.Scheduling;

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
    JobQueue queue,
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
        foreach (var repoPath in options.Value.Repositories)
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
            if (string.IsNullOrEmpty(plan.CronSchedule))
            {
                continue;
            }

            // SQLite can't translate ORDER BY on DateTimeOffset — sort client-side.
            var lastSuccess = (await db.Jobs.AsNoTracking()
                    .Where(j => j.PlanId == plan.PlanId && j.Status == JobStatus.Succeeded)
                    .ToListAsync(ct))
                .OrderByDescending(j => j.FinishedAtUtc)
                .FirstOrDefault()
                ?.FinishedAtUtc;

            if (!CronScheduleCalculator.IsDue(plan.CronSchedule, lastSuccess, DateTimeOffset.UtcNow))
            {
                continue;
            }

            var alreadyQueued = await db.Jobs.AnyAsync(
                j => j.PlanId == plan.PlanId && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running), ct);
            if (alreadyQueued)
            {
                continue;
            }

            var jobId = Guid.NewGuid().ToString("n");
            db.Jobs.Add(new JobEntity
            {
                JobId = jobId,
                PlanId = plan.PlanId,
                Kind = "backup",
                Status = JobStatus.Pending,
            });
            await db.SaveChangesAsync(ct);

            await queue.Writer.WriteAsync(new ScheduledJob(repoPath, jobId, plan.PlanId), ct);
            logger.LogInformation("Queued job {JobId} for plan {PlanName} in {RepoPath}.", jobId, plan.Name, repoPath);
        }
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
                var sourcePaths = JsonSerializer.Deserialize<string[]>(plan.SourcePathsJson)
                    ?? throw new InvalidDataException($"Plan '{plan.PlanId}' has no valid source paths.");

                var engine = new BackupEngine(new LocalSourceProvider(), scheduled.RepoPath, masterKey);
                var snapshotId = await engine.RunAsync(sourcePaths, ct: stoppingToken);

                if (!string.IsNullOrEmpty(plan.RetentionPolicyJson))
                {
                    var policy = JsonSerializer.Deserialize<RetentionPolicy>(plan.RetentionPolicyJson);
                    if (policy is not null)
                    {
                        var pruneEngine = new PruneEngine(scheduled.RepoPath, masterKey);
                        await pruneEngine.RunAsync(policy, dryRun: false, stoppingToken);
                    }
                }

                job.Status = JobStatus.Succeeded;
                job.FinishedAtUtc = DateTimeOffset.UtcNow;
                db.JobLogs.Add(new JobLogEntity
                {
                    JobId = job.JobId,
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Level = "Information",
                    Message = $"Snapshot {snapshotId} completed successfully.",
                });
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
        }

        // Persist the final status even if stoppingToken is already cancelled.
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
