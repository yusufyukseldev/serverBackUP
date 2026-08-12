using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServerBackup.Data;
using ServerBackup.Data.Entities;
using ServerBackup.Engine.Backup;
using ServerBackup.Engine.Notifications;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Scanning;
using ServerBackup.Engine.Scheduling;
using ServerBackup.Service.Scheduling;
using ServerBackup.Service.Storage;
using Xunit;

namespace ServerBackup.Integration.Tests;

public sealed class BackupSchedulerServiceTests : IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly string _repoPath = Path.Combine(Path.GetTempPath(), "sb-svc-repo-" + Guid.NewGuid().ToString("n"));
    private readonly string _sourcePath = Path.Combine(Path.GetTempPath(), "sb-svc-src-" + Guid.NewGuid().ToString("n"));
    private readonly string _statePath = Path.Combine(Path.GetTempPath(), "sb-svc-state-" + Guid.NewGuid().ToString("n"));

    public BackupSchedulerServiceTests() => Directory.CreateDirectory(_sourcePath);

    [Fact]
    public async Task Starting_the_service_picks_up_a_due_plan_and_stopping_it_leaves_no_job_stuck_in_progress()
    {
        File.WriteAllBytes(Path.Combine(_sourcePath, "a.bin"), new byte[500_000]);

        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);
        UnattendedKeyStore.Enable(_repoPath, masterKey);

        var planId = Guid.NewGuid().ToString("n");
        await using (var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db")))
        {
            db.Plans.Add(new PlanEntity
            {
                PlanId = planId,
                Name = "test-plan",
                SourcePathsJson = JsonSerializer.Serialize(new[] { _sourcePath }),
                CronSchedule = "0 3 * * *", // irrelevant for a plan that has never run — always immediately due
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var settings = new ServerBackupOptions
        {
            Repositories = [_repoPath],
            DataDirectory = _statePath,
            PollIntervalSeconds = 1,
            MaxConcurrentJobs = 1,
        };
        var queue = new JobQueue();
        var service = new BackupSchedulerService(
            Options.Create(settings),
            new RepositoryRegistry(settings),
            queue,
            new NoOpNotifier(),
            NullLogger<BackupSchedulerService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            // Wait for the job to at least start (Pending/Running observed at least once).
            var observedInProgress = await WaitUntilAsync(async () =>
            {
                await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));
                return db.Jobs.Any(j => j.PlanId == planId);
            }, TimeSpan.FromSeconds(15));

            observedInProgress.Should().BeTrue("the service should have queued a job for the always-due plan within the poll interval");
        }
        finally
        {
            // Graceful shutdown must complete promptly — BackgroundService waits
            // for ExecuteAsync, which must respect the stopping token throughout.
            var stopTask = service.StopAsync(CancellationToken.None);
            var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(20)));
            completed.Should().Be(stopTask, "StopAsync must not hang");
            await stopTask;
        }

        // The critical invariant: no job is left permanently stuck in
        // Pending/Running after the service has fully stopped.
        await using var finalDb = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));
        var job = finalDb.Jobs.Single(j => j.PlanId == planId);
        job.Status.Should().BeOneOf(JobStatus.Succeeded, JobStatus.Failed, JobStatus.Cancelled);
    }

    [Fact]
    public async Task A_due_verify_schedule_runs_and_reports_success_on_a_healthy_repository()
    {
        File.WriteAllBytes(Path.Combine(_sourcePath, "a.bin"), new byte[500_000]);

        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);
        UnattendedKeyStore.Enable(_repoPath, masterKey);

        // Back up directly (not through the scheduler) so there is something
        // for the verify job to actually check.
        var backupEngine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);
        await backupEngine.RunAsync([_sourcePath]);

        var planId = Guid.NewGuid().ToString("n");
        await using (var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db")))
        {
            db.Plans.Add(new PlanEntity
            {
                PlanId = planId,
                Name = "verify-only-plan",
                SourcePathsJson = JsonSerializer.Serialize(new[] { _sourcePath }),
                CronSchedule = null, // no backup scheduling — only verify
                VerifyCronSchedule = "0 4 * * *", // never having run, this is immediately due regardless of the expression
                VerifyLevel = "Packs",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var notifier = new RecordingNotifier();
        var (service, _) = StartScheduler(notifier);
        try
        {
            var job = await WaitForTerminalJobAsync(planId, "verify", TimeSpan.FromSeconds(15));
            job.Status.Should().Be(JobStatus.Succeeded);
        }
        finally
        {
            await StopAsync(service);
        }

        notifier.Calls.Should().BeEmpty("a clean repository must not raise an alert");
    }

    [Fact]
    public async Task A_due_verify_schedule_fails_and_alerts_when_the_repository_is_corrupted()
    {
        File.WriteAllBytes(Path.Combine(_sourcePath, "a.bin"), new byte[500_000]);

        await RepositoryManager.InitializeAsync(_repoPath, Password);
        var masterKey = await RepositoryKeyStore.UnlockAsync(_repoPath, Password);
        UnattendedKeyStore.Enable(_repoPath, masterKey);

        var backupEngine = new BackupEngine(new LocalSourceProvider(), _repoPath, masterKey);
        await backupEngine.RunAsync([_sourcePath]);

        // Corrupt every pack on disk so a Packs-level verify is guaranteed to find something,
        // regardless of which pack(s) the scan happened to produce.
        var packFiles = Directory.GetFiles(_repoPath, "*.pack", SearchOption.AllDirectories);
        packFiles.Should().NotBeEmpty("the backup above must have written at least one pack");
        foreach (var packFile in packFiles)
        {
            var bytes = File.ReadAllBytes(packFile);
            bytes[^1] ^= 0xFF;
            File.WriteAllBytes(packFile, bytes);
        }

        var planId = Guid.NewGuid().ToString("n");
        await using (var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db")))
        {
            db.Plans.Add(new PlanEntity
            {
                PlanId = planId,
                Name = "verify-only-plan",
                SourcePathsJson = JsonSerializer.Serialize(new[] { _sourcePath }),
                CronSchedule = null,
                VerifyCronSchedule = "0 4 * * *",
                VerifyLevel = "Packs",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var notifier = new RecordingNotifier();
        var (service, _) = StartScheduler(notifier);
        try
        {
            var job = await WaitForTerminalJobAsync(planId, "verify", TimeSpan.FromSeconds(15));
            job.Status.Should().Be(JobStatus.Failed);
        }
        finally
        {
            await StopAsync(service);
        }

        notifier.Calls.Should().ContainSingle(c => c.Severity == NotificationSeverity.Error,
            "a corrupted repository must not fail silently — the operator only sees Alerts, not the job log");
    }

    private (BackupSchedulerService Service, RepositoryRegistry Registry) StartScheduler(INotifier notifier)
    {
        var settings = new ServerBackupOptions
        {
            Repositories = [_repoPath],
            DataDirectory = _statePath,
            PollIntervalSeconds = 1,
            MaxConcurrentJobs = 1,
        };
        var registry = new RepositoryRegistry(settings);
        var service = new BackupSchedulerService(
            Options.Create(settings), registry, new JobQueue(), notifier, NullLogger<BackupSchedulerService>.Instance);

        service.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return (service, registry);
    }

    private static async Task StopAsync(BackupSchedulerService service)
    {
        var stopTask = service.StopAsync(CancellationToken.None);
        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(20)));
        completed.Should().Be(stopTask, "StopAsync must not hang");
        await stopTask;
    }

    private async Task<JobEntity> WaitForTerminalJobAsync(string planId, string kind, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var db = CatalogDbContextFactory.Create(Path.Combine(_repoPath, "catalog.db"));
            var job = db.Jobs.FirstOrDefault(j => j.PlanId == planId && j.Kind == kind
                && (j.Status == JobStatus.Succeeded || j.Status == JobStatus.Failed || j.Status == JobStatus.Cancelled));
            if (job is not null)
            {
                return job;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"No terminal '{kind}' job for plan '{planId}' within {timeout}.");
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(200);
        }

        return false;
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _repoPath, _sourcePath, _statePath })
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private sealed class NoOpNotifier : INotifier
    {
        public void Notify(string title, string message, NotificationSeverity severity)
        {
        }
    }

    private sealed class RecordingNotifier : INotifier
    {
        public List<(string Title, string Message, NotificationSeverity Severity)> Calls { get; } = [];

        public void Notify(string title, string message, NotificationSeverity severity) =>
            Calls.Add((title, message, severity));
    }
}
