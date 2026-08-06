using Microsoft.EntityFrameworkCore;
using ServerBackup.Core.Repository;
using ServerBackup.Data;

namespace ServerBackup.Engine.Repository;

/// <summary>
/// Reads arbitrary blobs (data or tree) by content-addressed id, resolving
/// which pack holds them via the catalog and caching open pack readers for
/// reuse (a tree walk typically reads many blobs from the same few packs).
/// Used by both incremental backup (reading the parent snapshot's tree) and
/// restore (plan Faz 6).
/// </summary>
public sealed class BlobStore : IDisposable
{
    private readonly string _repoPath;
    private readonly byte[] _packSubKey;
    private readonly CatalogDbContext _db;
    private readonly Dictionary<string, (FileStream Stream, PackReader Reader)> _openPacks = [];

    public BlobStore(string repoPath, byte[] packSubKey, CatalogDbContext db)
    {
        _repoPath = repoPath;
        _packSubKey = packSubKey;
        _db = db;
    }

    public async Task<byte[]> ReadBlobAsync(string blobIdHex, CancellationToken ct = default)
    {
        var blobRow = await _db.Blobs.AsNoTracking().FirstOrDefaultAsync(b => b.BlobId == blobIdHex, ct)
            ?? throw new InvalidOperationException($"Blob '{blobIdHex}' not found in catalog.");

        var reader = GetOrOpenPack(blobRow.PackId);
        var index = FindEntryIndex(reader, blobIdHex);
        return reader.ReadBlob(index);
    }

    private PackReader GetOrOpenPack(string packId)
    {
        if (_openPacks.TryGetValue(packId, out var cached))
        {
            return cached.Reader;
        }

        var path = Path.Combine(_repoPath, PackId.RelativePath(packId));
        var stream = File.OpenRead(path);
        var reader = new PackReader(stream, _packSubKey);
        _openPacks[packId] = (stream, reader);
        return reader;
    }

    private static int FindEntryIndex(PackReader reader, string blobIdHex)
    {
        for (var i = 0; i < reader.Entries.Count; i++)
        {
            if (string.Equals(Convert.ToHexStringLower(reader.Entries[i].BlobId), blobIdHex, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new InvalidOperationException($"Blob '{blobIdHex}' was not found inside its recorded pack.");
    }

    public void Dispose()
    {
        foreach (var (stream, _) in _openPacks.Values)
        {
            stream.Dispose();
        }

        _openPacks.Clear();
    }
}
