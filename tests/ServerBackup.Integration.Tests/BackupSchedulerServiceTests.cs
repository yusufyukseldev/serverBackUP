using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServerBackup.Data;
using ServerBackup.Data.Entities;
using ServerBackup.Engine.Backup;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Scanning;
using ServerBackup.Engine.Scheduling;
using ServerBackup.Service.Scheduling;
using Xunit;

namespace ServerBackup.Integration.Tests;

public sealed class BackupSchedulerServiceTests : IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly string _repoPath = Path.Combine(Path.GetTempPath(), "sb-svc-repo-" + Guid.NewGuid().ToString("n"));
    private readonly string _sourcePath = Path.Combine(Path.GetTempPath(), "sb-svc-src-" + Guid.NewGuid().ToString("n"));

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

        var options = Options.Create(new ServerBackupOptions
        {
            Repositories = [_repoPath],
            PollIntervalSeconds = 1,
            MaxConcurrentJobs = 1,
        });
        var queue = new JobQueue();
        var service = new BackupSchedulerService(options, queue, NullLogger<BackupSchedulerService>.Instance);

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
        foreach (var dir in new[] { _repoPath, _sourcePath })
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
