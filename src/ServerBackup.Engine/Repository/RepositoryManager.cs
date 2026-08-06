using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ServerBackup.Core.Crypto;
using ServerBackup.Core.Repository;
using ServerBackup.Data;
using ServerBackup.Data.Entities;

namespace ServerBackup.Engine.Repository;

/// <summary>
/// Repository lifecycle operations that span both Core (pack format, crypto)
/// and Data (SQLite catalog): create a new repository, and rebuild the
/// catalog purely from what's on disk when it's lost or suspect.
/// </summary>
public static class RepositoryManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task InitializeAsync(string repoPath, string password, CancellationToken ct = default)
    {
        Directory.CreateDirectory(repoPath);
        Directory.CreateDirectory(Path.Combine(repoPath, "data"));
        Directory.CreateDirectory(Path.Combine(repoPath, "keys"));
        Directory.CreateDirectory(Path.Combine(repoPath, "locks"));

        var configPath = Path.Combine(repoPath, "config.json");
        if (File.Exists(configPath))
        {
            throw new InvalidOperationException($"A repository already exists at '{repoPath}' (config.json present).");
        }

        var config = RepositoryConfig.CreateNew();
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config, JsonOptions), ct);

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var (masterKey, keyFile) = MasterKeyFile.CreateNew(passwordBytes);
        try
        {
            var keyFilePath = Path.Combine(repoPath, "keys", $"{keyFile.KeyId}.json");
            await File.WriteAllTextAsync(keyFilePath, JsonSerializer.Serialize(keyFile, JsonOptions), ct);

            await using var db = CatalogDbContextFactory.Create(CatalogPath(repoPath));
            await db.Database.MigrateAsync(ct);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    /// <summary>
    /// Discards the catalog and rebuilds Packs/Blobs entirely from the pack
    /// files on disk. Snapshot metadata cannot be recovered this way (it
    /// lives only in the catalog and the tree blobs it points to) — this
    /// restores the ability to read every blob, not the snapshot list.
    /// </summary>
    public static async Task<RebuildResult> RebuildIndexAsync(string repoPath, byte[] masterKey, CancellationToken ct = default)
    {
        var dbPath = CatalogPath(repoPath);
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var file = dbPath + suffix;
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        await using var db = CatalogDbContextFactory.Create(dbPath);
        await db.Database.MigrateAsync(ct);

        var packSubKey = SubKeys.Derive(masterKey, SubKeys.PackKeyInfo);
        var dataDir = Path.Combine(repoPath, "data");

        var packCount = 0;
        var blobCount = 0;

        if (Directory.Exists(dataDir))
        {
            foreach (var packFile in Directory.EnumerateFiles(dataDir, "*.pack", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();

                var packId = Path.GetFileNameWithoutExtension(packFile);
                var bytes = await File.ReadAllBytesAsync(packFile, ct);
                var sha256 = SHA256.HashData(bytes);

                using var stream = new MemoryStream(bytes, writable: false);
                var reader = new PackReader(stream, packSubKey);

                db.Packs.Add(new PackEntity
                {
                    PackId = packId,
                    Sha256 = sha256,
                    SizeBytes = bytes.LongLength,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                });

                foreach (var entry in reader.Entries)
                {
                    db.Blobs.Add(new BlobEntity
                    {
                        BlobId = Convert.ToHexStringLower(entry.BlobId),
                        PackId = packId,
                        Kind = (byte)entry.Kind,
                        Offset = (long)entry.Offset,
                        LenStored = (int)entry.LenStored,
                        LenPlain = (int)entry.LenPlain,
                        Compression = entry.Compression,
                    });
                    blobCount++;
                }

                packCount++;
            }
        }

        await db.SaveChangesAsync(ct);
        return new RebuildResult(packCount, blobCount);
    }

    private static string CatalogPath(string repoPath) => Path.Combine(repoPath, "catalog.db");
}

public sealed record RebuildResult(int PackCount, int BlobCount);
