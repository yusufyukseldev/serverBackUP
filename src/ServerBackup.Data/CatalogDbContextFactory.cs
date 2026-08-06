using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ServerBackup.Data;

public static class CatalogDbContextFactory
{
    public static CatalogDbContext Create(string dbPath)
    {
        // Pooling=False: without it, Microsoft.Data.Sqlite keeps the native
        // connection (and OS file handle) alive after Dispose for reuse, which
        // breaks callers that need to delete/replace the file immediately
        // afterward (e.g. rebuild-index discarding a suspect catalog.db).
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite($"Data Source={dbPath};Pooling=False")
            .Options;

        return new CatalogDbContext(options);
    }
}

/// <summary>Lets `dotnet ef migrations add` run without a hosted DI container.</summary>
public sealed class CatalogDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args) => CatalogDbContextFactory.Create("design-time.db");
}
