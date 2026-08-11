using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServerBackup.Data;
using ServerBackup.Data.Entities;
using ServerBackup.Engine.Repository;
using ServerBackup.Engine.Scheduling;
using Xunit;

namespace ServerBackup.Integration.Tests;

/// <summary>
/// SQLite cannot ORDER BY a <see cref="DateTimeOffset"/> column: EF throws
/// <see cref="NotSupportedException"/> only when the query is executed, which
/// in the Blazor panel tears down the whole circuit and leaves a dead page.
/// Every catalog query therefore materialises before sorting; these tests pin
/// that convention for the timestamp columns the UI sorts on.
/// </summary>
public sealed class CatalogQueryShapeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sb-query-" + Guid.NewGuid().ToString("n"));
    private readonly string _dbPath;

    public CatalogQueryShapeTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "catalog.db");
    }

    [Fact]
    public async Task Ordering_job_logs_by_timestamp_in_the_database_is_not_supported()
    {
        await SeedAsync();
        await using var db = CatalogDbContextFactory.Create(_dbPath);

        var act = async () => await db.JobLogs.AsNoTracking().OrderBy(l => l.TimestampUtc).ToListAsync();

        await act.Should().ThrowAsync<NotSupportedException>(
            "if this ever starts working the in-memory sorts below may be simplified — until then they are required");
    }

    [Fact]
    public async Task Job_logs_sorted_after_materialising_come_back_in_timestamp_order()
    {
        await SeedAsync();
        await using var db = CatalogDbContextFactory.Create(_dbPath);

        var logs = (await db.JobLogs.AsNoTracking().Where(l => l.JobId == "job1").ToListAsync())
            .OrderBy(l => l.TimestampUtc)
            .ToList();

        logs.Select(l => l.Message).Should().ContainInOrder("ilk", "orta", "son");
    }

    [Fact]
    public async Task Jobs_sorted_after_materialising_come_back_newest_first()
    {
        await SeedAsync();
        await using var db = CatalogDbContextFactory.Create(_dbPath);

        var jobs = (await db.Jobs.AsNoTracking().ToListAsync())
            .OrderByDescending(j => j.StartedAtUtc)
            .ToList();

        jobs[0].JobId.Should().Be("job2");
    }

    private async Task SeedAsync()
    {
        // Goes through the real initialiser so the schema matches production
        // migrations rather than an ad-hoc EnsureCreated shape.
        await RepositoryManager.InitializeAsync(_dir, "correct horse battery staple");

        await using var db = CatalogDbContextFactory.Create(_dbPath);
        var baseTime = new DateTimeOffset(2026, 8, 11, 9, 0, 0, TimeSpan.Zero);

        db.Jobs.Add(new JobEntity { JobId = "job1", Kind = "backup", Status = JobStatus.Succeeded, StartedAtUtc = baseTime });
        db.Jobs.Add(new JobEntity { JobId = "job2", Kind = "backup", Status = JobStatus.Succeeded, StartedAtUtc = baseTime.AddHours(1) });

        db.JobLogs.Add(new JobLogEntity { JobId = "job1", TimestampUtc = baseTime.AddMinutes(2), Level = "Information", Message = "orta" });
        db.JobLogs.Add(new JobLogEntity { JobId = "job1", TimestampUtc = baseTime.AddMinutes(5), Level = "Information", Message = "son" });
        db.JobLogs.Add(new JobLogEntity { JobId = "job1", TimestampUtc = baseTime, Level = "Information", Message = "ilk" });

        await db.SaveChangesAsync();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Temp cleanup is best-effort.
        }
    }
}
