using System.Buffers.Binary;
using ServerBackup.Core.Crypto;

namespace ServerBackup.Core.Repository;

/// <summary>
/// Reads a pack file written by <see cref="PackWriter"/>: parses the
/// (encrypted) header on construction, then decrypts individual blobs on
/// demand by seeking. The header is read from the end of the file, since its
/// length is stored in the last 4 bytes — see docs/format-spec.md.
/// </summary>
public sealed class PackReader
{
    private readonly Stream _input;
    private readonly byte[] _packKey;

    public IReadOnlyList<BlobEntry> Entries { get; }

    public PackReader(Stream input, ReadOnlySpan<byte> packSubKey)
    {
        if (!input.CanSeek)
        {
            throw new ArgumentException("Pack stream must be seekable.", nameof(input));
        }

        _input = input;

        var salt = new byte[16];
        input.Seek(0, SeekOrigin.Begin);
        input.ReadExactly(salt);

        Span<byte> footer = stackalloc byte[4];
        input.Seek(-4, SeekOrigin.End);
        input.ReadExactly(footer);
        var headerLength = BinaryPrimitives.ReadUInt32LittleEndian(footer);

        var headerStart = input.Length - 4 - headerLength;
        if (headerStart < salt.Length)
        {
            throw new InvalidDataException("Pack file is truncated or its header length footer is corrupt.");
        }

        input.Seek(headerStart, SeekOrigin.Begin);
        var headerSealed = new byte[headerLength];
        input.ReadExactly(headerSealed);

        _packKey = SubKeys.DerivePackKey(packSubKey, salt);
        var headerNonce = PackNonce.ForBlobIndex(PackNonce.HeaderBlobIndex);
        var headerPlain = AeadCipher.Open(_packKey, headerNonce, headerSealed);

        Entries = ParseHeader(headerPlain);
    }

    /// <summary>Decrypts and decompresses the blob at the given position (0-based, in write order).</summary>
    public byte[] ReadBlob(int blobIndex)
    {
        if (blobIndex < 0 || blobIndex >= Entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(blobIndex));
        }

        var entry = Entries[blobIndex];
        _input.Seek((long)entry.Offset, SeekOrigin.Begin);
        var sealedData = new byte[entry.LenStored];
        _input.ReadExactly(sealedData);

        var nonce = PackNonce.ForBlobIndex((ulong)blobIndex);
        var compressed = AeadCipher.Open(_packKey, nonce, sealedData);

        return CompressionCodec.Decompress(entry.Compression, compressed, (int)entry.LenPlain);
    }

    internal static IReadOnlyList<BlobEntry> ParseHeader(byte[] headerPlain)
    {
        using var ms = new MemoryStream(headerPlain);
        using var reader = new BinaryReader(ms);

        var count = reader.ReadUInt32();
        var entries = new List<BlobEntry>((int)count);
        for (var i = 0; i < count; i++)
        {
            var kind = (BlobKind)reader.ReadByte();
            var blobId = reader.ReadBytes(BlobId.SizeBytes);
            var offset = reader.ReadUInt64();
            var lenStored = reader.ReadUInt32();
            var lenPlain = reader.ReadUInt32();
            var compression = reader.ReadByte();
            entries.Add(new BlobEntry(blobId, kind, offset, lenStored, lenPlain, compression));
        }

        return entries.AsReadOnly();
    }
}
