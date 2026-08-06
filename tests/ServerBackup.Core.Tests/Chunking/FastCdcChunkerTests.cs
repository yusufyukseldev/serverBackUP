using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using ServerBackup.Core.Chunking;
using ServerBackup.Core.Crypto;
using ServerBackup.Core.Tests.Crypto;
using Xunit;

namespace ServerBackup.Core.Tests.Chunking;

public sealed class FastCdcChunkerTests
{
    private static readonly ulong[] GearTable = GearTableFactory.Derive(RandomNumberGeneratorFixture.Bytes(32));

    private static byte[][] ChunkInMemory(FastCdcChunker chunker, byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        return chunker.Chunk(stream).ToArray();
    }

    [Property]
    public bool Concatenated_chunks_reproduce_the_original_data(byte[] data)
    {
        var chunker = new FastCdcChunker(GearTable);
        var chunks = ChunkInMemory(chunker, data);

        var rebuilt = chunks.SelectMany(c => c).ToArray();
        return rebuilt.SequenceEqual(data);
    }

    [Property]
    public bool Chunking_the_same_data_twice_yields_the_same_boundaries(byte[] data)
    {
        var chunker = new FastCdcChunker(GearTable);

        var lengths1 = ChunkInMemory(chunker, data).Select(c => c.Length).ToArray();
        var lengths2 = ChunkInMemory(chunker, data).Select(c => c.Length).ToArray();

        return lengths1.SequenceEqual(lengths2);
    }

    [Fact]
    public void No_chunk_exceeds_MaxSize()
    {
        var chunker = new FastCdcChunker(GearTable);
        var data = RandomNumberGeneratorFixture.Bytes(FastCdcParameters.MaxSize * 5);

        var chunks = ChunkInMemory(chunker, data);

        chunks.Should().OnlyContain(c => c.Length <= FastCdcParameters.MaxSize);
    }

    [Fact]
    public void Only_the_last_chunk_may_be_shorter_than_MinSize()
    {
        var chunker = new FastCdcChunker(GearTable);
        var data = RandomNumberGeneratorFixture.Bytes(FastCdcParameters.MaxSize * 5);

        var chunks = ChunkInMemory(chunker, data);

        chunks.Take(chunks.Length - 1).Should().OnlyContain(c => c.Length >= FastCdcParameters.MinSize);
    }

    [Fact]
    public void Small_input_below_MinSize_becomes_a_single_chunk()
    {
        var chunker = new FastCdcChunker(GearTable);
        var data = RandomNumberGeneratorFixture.Bytes(1000);

        var chunks = ChunkInMemory(chunker, data);

        chunks.Should().HaveCount(1);
        chunks[0].Should().Equal(data);
    }

    [Fact]
    public void Empty_input_produces_no_chunks()
    {
        var chunker = new FastCdcChunker(GearTable);

        var chunks = ChunkInMemory(chunker, []);

        chunks.Should().BeEmpty();
    }

    [Fact]
    public void Different_gear_tables_produce_different_boundaries_for_the_same_data()
    {
        var data = RandomNumberGeneratorFixture.Bytes(FastCdcParameters.MaxSize * 5);
        var tableA = GearTableFactory.Derive(RandomNumberGeneratorFixture.Bytes(32));
        var tableB = GearTableFactory.Derive(RandomNumberGeneratorFixture.Bytes(32));

        var lengthsA = ChunkInMemory(new FastCdcChunker(tableA), data).Select(c => c.Length).ToArray();
        var lengthsB = ChunkInMemory(new FastCdcChunker(tableB), data).Select(c => c.Length).ToArray();

        lengthsA.Should().NotEqual(lengthsB);
    }

    [Fact]
    public void Chunk_boundaries_resync_shortly_after_an_edit()
    {
        // Property that makes CDC useful for dedup: an edit near the start of a
        // large file should only perturb chunks close to the edit. Once the
        // scanner realigns on unchanged content, it must reproduce byte-identical
        // chunks for the untouched tail — this is what makes incremental backups
        // cheap.
        var chunker = new FastCdcChunker(GearTable);
        var original = RandomNumberGeneratorFixture.Bytes(FastCdcParameters.MaxSize * 10);

        var editPosition = FastCdcParameters.MaxSize; // well past the first chunk
        var inserted = RandomNumberGeneratorFixture.Bytes(777);
        var modified = original[..editPosition]
            .Concat(inserted)
            .Concat(original[editPosition..])
            .ToArray();

        var originalChunks = ChunkInMemory(chunker, original);
        var modifiedChunks = ChunkInMemory(chunker, modified);

        var originalHashes = new HashSet<string>(originalChunks.Select(HashChunk));
        var modifiedHashes = new HashSet<string>(modifiedChunks.Select(HashChunk));

        // At minimum, every chunk that starts beyond (editPosition + MaxSize) in
        // the original file is guaranteed to reappear byte-for-byte in the
        // modified stream, because FastCDC's fingerprint restarts from zero at
        // each chunk boundary and depends only on local content.
        var unaffectedTailStart = editPosition + FastCdcParameters.MaxSize;
        var offset = 0;
        var unaffectedChunkCount = 0;
        foreach (var chunk in originalChunks)
        {
            if (offset >= unaffectedTailStart)
            {
                unaffectedChunkCount++;
            }

            offset += chunk.Length;
        }

        unaffectedChunkCount.Should().BeGreaterThan(0, "the test data must be large enough to contain unaffected chunks");

        var reusedCount = originalHashes.Count(h => modifiedHashes.Contains(h));
        reusedCount.Should().BeGreaterThanOrEqualTo(unaffectedChunkCount);

        static string HashChunk(byte[] chunk) => Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(chunk));
    }
}
