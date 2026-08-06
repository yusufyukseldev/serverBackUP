using System.ComponentModel;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ServerBackup.Core.Crypto;
using ServerBackup.Data;
using ServerBackup.Engine.Backup;
using ServerBackup.Engine.Repository;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ServerBackup.Cli.Commands;

public sealed class DiffCommand : AsyncCommand<DiffCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<REPO>")]
        [Description("Depo dizini.")]
        public required string Repo { get; init; }

        [CommandArgument(1, "<SNAPSHOT_A>")]
        [Description("Karşılaştırılacak ilk snapshot.")]
        public required string SnapshotA { get; init; }

        [CommandArgument(2, "<SNAPSHOT_B>")]
        [Description("Karşılaştırılacak ikinci snapshot.")]
        public required string SnapshotB { get; init; }

        [CommandOption("--password")]
        [Description("Depo parolası. Verilmezse güvenli şekilde sorulur.")]
        public string? Password { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var password = settings.Password ?? AnsiConsole.Prompt(
            new TextPrompt<string>("Depo parolası:").Secret());

        var masterKey = await RepositoryKeyStore.UnlockAsync(settings.Repo, password, cancellationToken);
        try
        {
            await using var db = CatalogDbContextFactory.Create(System.IO.Path.Combine(settings.Repo, "catalog.db"));
            var snapshotA = await RequireSnapshotAsync(db, settings.SnapshotA, cancellationToken);
            var snapshotB = await RequireSnapshotAsync(db, settings.SnapshotB, cancellationToken);

            var packSubKey = SubKeys.Derive(masterKey, SubKeys.PackKeyInfo);
            using var blobStore = new BlobStore(settings.Repo, packSubKey, db);

            var mapA = await ParentSnapshotCache.LoadAsync(blobStore, snapshotA.RootTreeBlobId, cancellationToken);
            var mapB = await ParentSnapshotCache.LoadAsync(blobStore, snapshotB.RootTreeBlobId, cancellationToken);

            var allPaths = mapA.FilesByRelativePath.Keys.Union(mapB.FilesByRelativePath.Keys, StringComparer.Ordinal);

            foreach (var path in allPaths.OrderBy(p => p, StringComparer.Ordinal))
            {
                var inA = mapA.FilesByRelativePath.TryGetValue(path, out var nodeA);
                var inB = mapB.FilesByRelativePath.TryGetValue(path, out var nodeB);

                var escapedPath = path.EscapeMarkup();
                if (!inA)
                {
                    AnsiConsole.MarkupLine($"[green]+[/] {escapedPath}");
                }
                else if (!inB)
                {
                    AnsiConsole.MarkupLine($"[red]-[/] {escapedPath}");
                }
                else if (nodeA!.Size != nodeB!.Size || nodeA.ModifiedAtFileTimeUtc != nodeB.ModifiedAtFileTimeUtc)
                {
                    AnsiConsole.MarkupLine($"[yellow]~[/] {escapedPath}");
                }
            }

            return 0;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    private static async Task<Data.Entities.SnapshotEntity> RequireSnapshotAsync(CatalogDbContext db, string snapshotId, CancellationToken ct)
    {
        return await db.Snapshots.AsNoTracking().FirstOrDefaultAsync(s => s.SnapshotId == snapshotId, ct)
            ?? throw new InvalidOperationException($"Snapshot bulunamadı: {snapshotId}");
    }
}
