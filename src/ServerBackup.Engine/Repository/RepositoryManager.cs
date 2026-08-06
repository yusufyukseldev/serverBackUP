using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
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

    /// <param name="immutabilityWindowDays">
    /// If set, snapshots newer than this many days can never be deleted —
    /// not by prune, not from the panel, no override. See plan Faz 11 and
    /// docs/threat-model.md: this is the ransomware-resistance guarantee.
    /// </param>
    /// <param name="appendOnly">
    /// If true, prune never deletes ANYTHING regardless of retention policy
    /// — strictly stronger than <paramref name="immutabilityWindowDays"/>,
    /// which still lets old snapshots age out.
    /// </param>
    public static async Task InitializeAsync(
        string repoPath, string password, int? immutabilityWindowDays = null, bool appendOnly = false, CancellationToken ct = default)
    {
        Directory.CreateDirectory(repoPath);
        Directory.CreateDirectory(Path.Combine(repoPath, "data"));
        Directory.CreateDirectory(Path.Combine(repoPath, "keys"));
        Directory.CreateDirectory(Path.Combine(repoPath, "locks"));

        HardenRepositoryAcl(repoPath);

        var configPath = Path.Combine(repoPath, "config.json");
        if (File.Exists(configPath))
        {
            throw new InvalidOperationException($"A repository already exists at '{repoPath}' (config.json present).");
        }

        var config = RepositoryConfig.CreateNew(immutabilityWindowDays, appendOnly);
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
        var skippedPacks = new List<string>();

        if (Directory.Exists(dataDir))
        {
            foreach (var packFile in Directory.EnumerateFiles(dataDir, "*.pack", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();

                var packId = Path.GetFileNameWithoutExtension(packFile);
                var bytes = await File.ReadAllBytesAsync(packFile, ct);

                // A pack from a killed/crashed process (never reached Close()) is
                // never indexed: it fails to parse here and is skipped, not
                // treated as a fatal error. A pack is only ever "complete" if it
                // was closed, and only closed packs are safe to catalog.
                PackReader reader;
                try
                {
                    using var probeStream = new MemoryStream(bytes, writable: false);
                    reader = new PackReader(probeStream, packSubKey);
                }
                catch (Exception ex) when (ex is InvalidDataException or CryptographicException or EndOfStreamException)
                {
                    skippedPacks.Add(packFile);
                    continue;
                }

                var sha256 = SHA256.HashData(bytes);

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
        return new RebuildResult(packCount, blobCount, skippedPacks);
    }

    public static async Task<RepositoryConfig> ReadConfigAsync(string repoPath, CancellationToken ct = default)
    {
        var configPath = Path.Combine(repoPath, "config.json");
        var json = await File.ReadAllTextAsync(configPath, ct);
        return JsonSerializer.Deserialize<RepositoryConfig>(json)
            ?? throw new InvalidDataException($"Could not parse '{configPath}'.");
    }

    /// <summary>
    /// Restricts the repository directory to the identity that created it,
    /// plus Administrators and SYSTEM — removes inherited access from
    /// broader groups (Users, Authenticated Users, Everyone) so a
    /// compromised low-privilege account/process can't read or tamper with
    /// backup data. Best-effort: failures are surfaced, not swallowed, since
    /// a repo that silently isn't hardened is a false sense of security.
    /// </summary>
    private static void HardenRepositoryAcl(string repoPath)
    {
        var info = new DirectoryInfo(repoPath);
        var security = info.GetAccessControl();

        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        var currentUser = WindowsIdentity.GetCurrent().User;
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

        foreach (var sid in new[] { currentUser, administrators, system })
        {
            if (sid is null)
            {
                continue;
            }

            security.AddAccessRule(new FileSystemAccessRule(
                sid,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        info.SetAccessControl(security);
    }

    private static string CatalogPath(string repoPath) => Path.Combine(repoPath, "catalog.db");
}

public sealed record RebuildResult(int PackCount, int BlobCount, IReadOnlyList<string> SkippedPacks);
