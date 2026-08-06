using System.ComponentModel;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ServerBackup.Core.Crypto;
using ServerBackup.Core.Trees;
using ServerBackup.Data;
using ServerBackup.Engine.Repository;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ServerBackup.Cli.Commands;

public sealed class LsCommand : AsyncCommand<LsCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<REPO>")]
        [Description("Depo dizini.")]
        public required string Repo { get; init; }

        [CommandArgument(1, "<SNAPSHOT_ID>")]
        [Description("Listelenecek snapshot kimliği.")]
        public required string SnapshotId { get; init; }

        [CommandOption("--password")]
        [Description("Depo parolası. Verilmezse güvenli şekilde sorulur.")]
        public string? Password { get; init; }

        [CommandOption("--path")]
        [Description("Kök yerine belirtilen alt yoldan listele.")]
        public string? Path { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var password = settings.Password ?? AnsiConsole.Prompt(
            new TextPrompt<string>("Depo parolası:").Secret());

        var masterKey = await RepositoryKeyStore.UnlockAsync(settings.Repo, password, cancellationToken);
        try
        {
            await using var db = CatalogDbContextFactory.Create(System.IO.Path.Combine(settings.Repo, "catalog.db"));
            var snapshot = await db.Snapshots.AsNoTracking()
                .FirstOrDefaultAsync(s => s.SnapshotId == settings.SnapshotId, cancellationToken);
            if (snapshot is null)
            {
                AnsiConsole.MarkupLine($"[red]Snapshot bulunamadı: {settings.SnapshotId.EscapeMarkup()}[/]");
                return 1;
            }

            var packSubKey = SubKeys.Derive(masterKey, SubKeys.PackKeyInfo);
            using var blobStore = new BlobStore(settings.Repo, packSubKey, db);

            var treeBlobIdHex = snapshot.RootTreeBlobId;
            if (settings.Path is not null)
            {
                treeBlobIdHex = await ResolveSubTreeAsync(blobStore, treeBlobIdHex, settings.Path, cancellationToken);
                if (treeBlobIdHex is null)
                {
                    AnsiConsole.MarkupLine($"[red]Yol bulunamadı: {settings.Path.EscapeMarkup()}[/]");
                    return 1;
                }
            }

            var bytes = await blobStore.ReadBlobAsync(treeBlobIdHex, cancellationToken);
            var tree = ServerBackup.Core.Trees.Tree.Deserialize(bytes);

            foreach (var node in tree.Nodes.OrderBy(n => n.Name, StringComparer.Ordinal))
            {
                var kind = node.Kind == TreeNodeKind.Directory ? "d" : "-";
                AnsiConsole.MarkupLine($"{kind} {node.Size,12:N0}  {node.Name.EscapeMarkup()}");
            }

            return 0;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    private static async Task<string?> ResolveSubTreeAsync(BlobStore blobStore, string rootTreeBlobIdHex, string relativePath, CancellationToken ct)
    {
        var segments = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var currentTreeBlobIdHex = rootTreeBlobIdHex;

        foreach (var segment in segments)
        {
            var bytes = await blobStore.ReadBlobAsync(currentTreeBlobIdHex, ct);
            var tree = ServerBackup.Core.Trees.Tree.Deserialize(bytes);
            var match = tree.Nodes.FirstOrDefault(n => n.Name == segment && n.Kind == TreeNodeKind.Directory);
            if (match?.SubTreeBlobIdHex is null)
            {
                return null;
            }

            currentTreeBlobIdHex = match.SubTreeBlobIdHex;
        }

        return currentTreeBlobIdHex;
    }
}
