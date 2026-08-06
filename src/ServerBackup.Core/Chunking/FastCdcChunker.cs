using System.Buffers;
using ServerBackup.Core.Crypto;

namespace ServerBackup.Core.Chunking;

/// <summary>
/// Splits data into content-defined chunks using FastCDC normalized chunking
/// (Xia et al. 2016) over a Gear rolling hash. The Gear table is keyed (see
/// <see cref="GearTableFactory"/>) so chunk boundaries cannot be predicted or
/// fingerprinted without repository access.
/// </summary>
public sealed class FastCdcChunker
{
    private readonly ulong[] _gearTable;

    public FastCdcChunker(ulong[] gearTable)
    {
        ArgumentNullException.ThrowIfNull(gearTable);
        if (gearTable.Length != GearTableFactory.EntryCount)
        {
            throw new ArgumentException(
                $"Gear table must have {GearTableFactory.EntryCount} entries, got {gearTable.Length}.",
                nameof(gearTable));
        }

        _gearTable = gearTable;
    }

    /// <summary>
    /// Computes the length of the next chunk at the start of <paramref name="data"/>.
    /// The caller must supply either the entire remainder of the file, or at
    /// least <see cref="FastCdcParameters.MaxSize"/> bytes if more data
    /// follows afterward — this method never looks past what it is given.
    /// </summary>
    public int NextChunkLength(ReadOnlySpan<byte> data)
    {
        if (data.Length <= FastCdcParameters.MinSize)
        {
            return data.Length;
        }

        var normalSize = Math.Min(FastCdcParameters.NormalSize, data.Length);
        var maxSize = Math.Min(FastCdcParameters.MaxSize, data.Length);

        var fp = 0UL;
        var i = FastCdcParameters.MinSize;

        for (; i < normalSize; i++)
        {
            fp = (fp << 1) + _gearTable[data[i]];
            if ((fp & FastCdcParameters.MaskS) == 0)
            {
                return i + 1;
            }
        }

        for (; i < maxSize; i++)
        {
            fp = (fp << 1) + _gearTable[data[i]];
            if ((fp & FastCdcParameters.MaskL) == 0)
            {
                return i + 1;
            }
        }

        return maxSize;
    }

    /// <summary>
    /// Splits a stream into content-defined chunks. Reads through a pooled
    /// sliding window (kept at least <see cref="FastCdcParameters.MaxSize"/>
    /// bytes ahead until EOF) so the whole file is never buffered at once.
    /// Each yielded array is a right-sized, independently owned copy of that
    /// chunk's bytes.
    /// </summary>
    public IEnumerable<byte[]> Chunk(Stream stream)
    {
        const int windowCapacity = FastCdcParameters.MaxSize * 2;
        var window = ArrayPool<byte>.Shared.Rent(windowCapacity);
        try
        {
            var windowLength = 0;
            var eof = false;

            while (true)
            {
                while (!eof && windowLength < FastCdcParameters.MaxSize)
                {
                    var read = stream.Read(window, windowLength, window.Length - windowLength);
                    if (read == 0)
                    {
                        eof = true;
                        break;
                    }

                    windowLength += read;
                }

                if (windowLength == 0)
                {
                    yield break;
                }

                var chunkLength = NextChunkLength(window.AsSpan(0, windowLength));
                var chunk = new byte[chunkLength];
                window.AsSpan(0, chunkLength).CopyTo(chunk);
                yield return chunk;

                var remaining = windowLength - chunkLength;
                window.AsSpan(chunkLength, remaining).CopyTo(window.AsSpan(0, remaining));
                windowLength = remaining;

                if (eof && windowLength == 0)
                {
                    yield break;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(window);
        }
    }
}
