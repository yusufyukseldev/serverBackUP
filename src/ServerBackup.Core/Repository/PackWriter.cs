using System.Buffers.Binary;
using System.Security.Cryptography;
using ServerBackup.Core.Crypto;

namespace ServerBackup.Core.Repository;

/// <summary>
/// Writes a single pack file: random packSalt, then each blob's compressed
/// ciphertext, then an encrypted header describing every blob, then a 4-byte
/// length footer — see docs/format-spec.md "Pack Dosyası İkili Düzeni".
/// A pack is write-once: once <see cref="Close"/> is called it is never
/// appended to again, which is what makes the deterministic per-blob nonce
/// counter safe.
/// </summary>
public sealed class PackWriter : IDisposable
{
    private readonly Stream _output;
    private readonly byte[] _packKey;
    private readonly IncrementalHash _hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly List<BlobEntry> _entries = [];

    private ulong _offset;
    private ulong _nextBlobIndex;
    private bool _closed;

    public PackWriter(Stream output, ReadOnlySpan<byte> packSubKey)
    {
        _output = output;

        var packSalt = RandomNumberGenerator.GetBytes(16);
        _packKey = SubKeys.DerivePackKey(packSubKey, packSalt);

        WriteRaw(packSalt);
        _offset = (ulong)packSalt.Length;
    }

    /// <summary>Number of blobs written so far.</summary>
    public int BlobCount => _entries.Count;

    /// <summary>Total bytes written so far (including salt, but not the not-yet-written header/footer).</summary>
    public ulong CurrentLengthBytes => _offset;

    /// <summary>Encrypts, optionally compresses, and appends one blob to the pack.</summary>
    public void AddBlob(byte[] blobId, BlobKind kind, ReadOnlySpan<byte> plaintext)
    {
        if (_closed)
        {
            throw new InvalidOperationException("Cannot add a blob after the pack has been closed.");
        }

        if (blobId.Length != Crypto.BlobId.SizeBytes)
        {
            throw new ArgumentException($"Blob id must be {Crypto.BlobId.SizeBytes} bytes.", nameof(blobId));
        }

        var (compressed, codec) = CompressionCodec.Compress(plaintext);
        var nonce = PackNonce.ForBlobIndex(_nextBlobIndex);
        var sealedData = AeadCipher.Seal(_packKey, nonce, compressed);

        WriteRaw(sealedData);

        _entries.Add(new BlobEntry(
            BlobId: blobId,
            Kind: kind,
            Offset: _offset,
            LenStored: (uint)sealedData.Length,
            LenPlain: (uint)plaintext.Length,
            Compression: codec));

        _offset += (ulong)sealedData.Length;
        _nextBlobIndex++;
    }

    /// <summary>Writes the encrypted header and footer, and returns what the catalog needs to record.</summary>
    public PackSummary Close()
    {
        if (_closed)
        {
            throw new InvalidOperationException("Pack has already been closed.");
        }

        _closed = true;

        var headerPlain = SerializeHeader(_entries);
        var headerNonce = PackNonce.ForBlobIndex(PackNonce.HeaderBlobIndex);
        var headerSealed = AeadCipher.Seal(_packKey, headerNonce, headerPlain);
        WriteRaw(headerSealed);
        _offset += (ulong)headerSealed.Length;

        var footer = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(footer, (uint)headerSealed.Length);
        WriteRaw(footer);
        _offset += (ulong)footer.Length;

        _output.Flush();

        var sha256 = _hasher.GetHashAndReset();
        return new PackSummary(_entries.AsReadOnly(), (long)_offset, sha256);
    }

    internal static byte[] SerializeHeader(IReadOnlyList<BlobEntry> entries)
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms))
        {
            writer.Write((uint)entries.Count);
            foreach (var entry in entries)
            {
                writer.Write((byte)entry.Kind);
                writer.Write(entry.BlobId);
                writer.Write(entry.Offset);
                writer.Write(entry.LenStored);
                writer.Write(entry.LenPlain);
                writer.Write(entry.Compression);
            }
        }

        return ms.ToArray();
    }

    private void WriteRaw(ReadOnlySpan<byte> data)
    {
        _output.Write(data);
        _hasher.AppendData(data);
    }

    /// <summary>
    /// Releases the internal hash state. Does NOT finalize an unclosed pack —
    /// an aborted write (exception, cancellation) must not silently produce a
    /// pack that looks complete. Callers must call <see cref="Close"/> explicitly.
    /// </summary>
    public void Dispose() => _hasher.Dispose();
}
