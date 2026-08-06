using ServerBackup.Core.Repository;
using ServerBackup.Data;
using ServerBackup.Data.Entities;

namespace ServerBackup.Engine.Backup;

/// <summary>
/// Owns the currently-open pack file for a backup run: rotates to a new pack
/// once the target size is reached, and records Pack/Blob catalog rows only
/// when a pack is actually closed (so a pack's file bytes and its catalog
/// rows always appear together, never one without the other).
///
/// Not thread-safe by design — see plan Faz 5: many workers may compress
/// blobs in parallel, but exactly one writer stage calls <see cref="WriteAsync"/>.
/// </summary>
public sealed class PackFileSet : IAsyncDisposable
{
    public const long TargetPackSizeBytes = 32L * 1024 * 1024;

    private readonly string _repoPath;
    private readonly byte[] _packSubKey;
    private readonly CatalogDbContext _db;

    private PackWriter? _writer;
    private FileStream? _stream;
    private string? _packId;

    public PackFileSet(string repoPath, byte[] packSubKey, CatalogDbContext db)
    {
        _repoPath = repoPath;
        _packSubKey = packSubKey;
        _db = db;
    }

    public async Task WriteAsync(byte[] blobId, BlobKind kind, byte[] compressedData, byte codec, int lenPlain, CancellationToken ct)
    {
        EnsureOpen();
        _writer!.AddPreCompressedBlob(blobId, kind, compressedData, codec, lenPlain);

        if (_writer.CurrentLengthBytes >= (ulong)TargetPackSizeBytes)
        {
            await CloseCurrentAsync(ct);
        }
    }

    /// <summary>Closes and indexes whatever pack is currently open, even if under the target size. Call on successful completion only.</summary>
    public async Task FlushAsync(CancellationToken ct)
    {
        if (_writer is not null)
        {
            await CloseCurrentAsync(ct);
        }
    }

    private void EnsureOpen()
    {
        if (_writer is not null)
        {
            return;
        }

        _packId = PackId.NewId();
        var path = Path.Combine(_repoPath, PackId.RelativePath(_packId));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _stream = File.Create(path);
        _writer = new PackWriter(_stream, _packSubKey);
    }

    private async Task CloseCurrentAsync(CancellationToken ct)
    {
        var summary = _writer!.Close();
        _writer.Dispose();
        await _stream!.DisposeAsync();

        _db.Packs.Add(new PackEntity
        {
            PackId = _packId!,
            Sha256 = summary.Sha256,
            SizeBytes = summary.TotalLengthBytes,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        foreach (var entry in summary.Entries)
        {
            var blobIdHex = Convert.ToHexStringLower(entry.BlobId);

            // Upsert: a repack (Faz 8) relocates existing blobs into a new
            // pack — the row already exists and must be repointed, not
            // duplicated (BlobId is the primary key).
            var existing = await _db.Blobs.FindAsync([blobIdHex], ct);
            if (existing is not null)
            {
                existing.PackId = _packId!;
                existing.Offset = (long)entry.Offset;
                existing.LenStored = (int)entry.LenStored;
                existing.LenPlain = (int)entry.LenPlain;
                existing.Compression = entry.Compression;
            }
            else
            {
                _db.Blobs.Add(new BlobEntity
                {
                    BlobId = blobIdHex,
                    PackId = _packId!,
                    Kind = (byte)entry.Kind,
                    Offset = (long)entry.Offset,
                    LenStored = (int)entry.LenStored,
                    LenPlain = (int)entry.LenPlain,
                    Compression = entry.Compression,
                });
            }
        }

        await _db.SaveChangesAsync(ct);

        _writer = null;
        _stream = null;
        _packId = null;
    }

    /// <summary>
    /// Discards any still-open pack without indexing it — reached on an
    /// aborted run (exception/cancellation). The partial file is deleted so
    /// it can never be mistaken for a complete pack by rebuild-index.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_writer is null)
        {
            return;
        }

        var packId = _packId;
        _writer.Dispose();
        if (_stream is not null)
        {
            await _stream.DisposeAsync();
        }

        if (packId is not null)
        {
            var path = Path.Combine(_repoPath, PackId.RelativePath(packId));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        _writer = null;
        _stream = null;
        _packId = null;
    }
}
