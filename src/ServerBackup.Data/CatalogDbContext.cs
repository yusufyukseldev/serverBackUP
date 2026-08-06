using Microsoft.EntityFrameworkCore;
using ServerBackup.Data.Entities;

namespace ServerBackup.Data;

/// <summary>
/// The repository catalog (catalog.db — see docs/format-spec.md "Katalog").
/// Losing this file is not a disaster: pack files are self-describing and
/// `repo rebuild-index` can reconstruct Packs/Blobs from them directly.
/// </summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<PackEntity> Packs => Set<PackEntity>();
    public DbSet<BlobEntity> Blobs => Set<BlobEntity>();
    public DbSet<SnapshotEntity> Snapshots => Set<SnapshotEntity>();
    public DbSet<SnapshotPathEntity> SnapshotPaths => Set<SnapshotPathEntity>();
    public DbSet<PlanEntity> Plans => Set<PlanEntity>();
    public DbSet<JobEntity> Jobs => Set<JobEntity>();
    public DbSet<JobLogEntity> JobLogs => Set<JobLogEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PackEntity>(e =>
        {
            e.HasKey(p => p.PackId);
        });

        modelBuilder.Entity<BlobEntity>(e =>
        {
            e.HasKey(b => b.BlobId);
            e.HasIndex(b => b.PackId);
            e.HasOne(b => b.Pack)
                .WithMany(p => p.Blobs)
                .HasForeignKey(b => b.PackId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SnapshotEntity>(e =>
        {
            e.HasKey(s => s.SnapshotId);
            e.HasIndex(s => s.PlanId);
        });

        modelBuilder.Entity<SnapshotPathEntity>(e =>
        {
            e.HasIndex(sp => sp.SnapshotId);
            e.HasOne(sp => sp.Snapshot)
                .WithMany(s => s.Paths)
                .HasForeignKey(sp => sp.SnapshotId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlanEntity>(e =>
        {
            e.HasKey(p => p.PlanId);
        });

        modelBuilder.Entity<JobEntity>(e =>
        {
            e.HasKey(j => j.JobId);
            e.HasIndex(j => j.PlanId);
        });

        modelBuilder.Entity<JobLogEntity>(e =>
        {
            e.HasIndex(l => l.JobId);
            e.HasOne(l => l.Job)
                .WithMany(j => j.Logs)
                .HasForeignKey(l => l.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLogEntity>(e =>
        {
            e.HasIndex(a => a.TimestampUtc);
        });
    }
}
