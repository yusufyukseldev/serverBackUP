using BenchmarkDotNet.Attributes;
using ServerBackup.Core.Chunking;
using ServerBackup.Core.Crypto;

namespace ServerBackup.Benchmarks;

/// <summary>
/// Measures FastCdcChunker throughput. Target: &gt; 500 MB/s single-threaded
/// (see plan Faz 2). Run with: dotnet run --project tests/ServerBackup.Benchmarks -c Release
/// </summary>
[MemoryDiagnoser]
public class FastCdcChunkerBenchmarks
{
    private const int DataSizeBytes = 256 * 1024 * 1024;

    private byte[] _data = [];
    private FastCdcChunker _chunker = null!;

    [GlobalSetup]
    public void Setup()
    {
        var masterKey = new byte[32];
        Random.Shared.NextBytes(masterKey);
        _chunker = new FastCdcChunker(GearTableFactory.Derive(masterKey));

        _data = new byte[DataSizeBytes];
        Random.Shared.NextBytes(_data);
    }

    [Benchmark]
    public long ChunkRandomData()
    {
        using var stream = new MemoryStream(_data, writable: false);
        long total = 0;
        foreach (var chunk in _chunker.Chunk(stream))
        {
            total += chunk.Length;
        }

        return total;
    }
}
