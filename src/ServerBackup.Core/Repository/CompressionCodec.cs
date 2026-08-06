using ZstdSharp;

namespace ServerBackup.Core.Repository;

/// <summary>
/// Compresses pack blob payloads with zstd, falling back to storing the data
/// raw when compression doesn't help (already-compressed media, encrypted
/// files, etc.) — see docs/format-spec.md. The 97% threshold means "keep raw
/// unless zstd actually saves at least ~3%", avoiding wasted CPU on data that
/// won't shrink meaningfully.
/// </summary>
public static class CompressionCodec
{
    public const byte Raw = 0;
    public const byte Zstd = 1;

    private const double MinSavingsRatio = 0.97;

    public static (byte[] Data, byte Codec) Compress(ReadOnlySpan<byte> plaintext, int level = 3)
    {
        if (plaintext.IsEmpty)
        {
            return ([], Raw);
        }

        using var compressor = new Compressor(level);
        var compressed = compressor.Wrap(plaintext).ToArray();

        if (compressed.Length < plaintext.Length * MinSavingsRatio)
        {
            return (compressed, Zstd);
        }

        return (plaintext.ToArray(), Raw);
    }

    public static byte[] Decompress(byte codec, ReadOnlySpan<byte> data, int decompressedLength)
    {
        switch (codec)
        {
            case Raw:
                return data.ToArray();
            case Zstd:
                if (decompressedLength == 0)
                {
                    return [];
                }

                using (var decompressor = new Decompressor())
                {
                    var output = new byte[decompressedLength];
                    var written = decompressor.Unwrap(data, output);
                    if (written != decompressedLength)
                    {
                        throw new InvalidDataException(
                            $"Decompressed size mismatch: expected {decompressedLength}, got {written}.");
                    }

                    return output;
                }
            default:
                throw new NotSupportedException($"Unknown compression codec {codec}.");
        }
    }
}
