using System.Security.Cryptography;
using FluentAssertions;
using ServerBackup.Core.Crypto;
using ServerBackup.Core.Repository;
using ServerBackup.Core.Tests.Crypto;
using Xunit;

namespace ServerBackup.Core.Tests.Repository;

public sealed class PackWriterReaderTests
{
    private static byte[] MasterKey => RandomNumberGeneratorFixture.Bytes(32);

    private static byte[] PackSubKey(byte[] masterKey) => SubKeys.Derive(masterKey, SubKeys.PackKeyInfo);

    private static byte[] NewBlobId() => RandomNumberGeneratorFixture.Bytes(BlobId.SizeBytes);

    [Fact]
    public void Roundtrip_recovers_every_blob_in_write_order()
    {
        var masterKey = MasterKey;
        var packSubKey = PackSubKey(masterKey);

        var blob1 = "hello, ServerBackup"u8.ToArray();
        var blob2 = RandomNumberGeneratorFixture.Bytes(500_000); // incompressible
        var blob3 = new byte[100_000]; // highly compressible (all zeros)
        var id1 = NewBlobId();
        var id2 = NewBlobId();
        var id3 = NewBlobId();

        using var stream = new MemoryStream();
        var writer = new PackWriter(stream, packSubKey);
        writer.AddBlob(id1, BlobKind.Data, blob1);
        writer.AddBlob(id2, BlobKind.Data, blob2);
        writer.AddBlob(id3, BlobKind.Tree, blob3);
        var summary = writer.Close();
        writer.Dispose();

        summary.Entries.Should().HaveCount(3);
        summary.TotalLengthBytes.Should().Be(stream.Length);

        stream.Position = 0;
        var reader = new PackReader(stream, packSubKey);

        reader.Entries.Should().HaveCount(3);
        reader.ReadBlob(0).Should().Equal(blob1);
        reader.ReadBlob(1).Should().Equal(blob2);
        reader.ReadBlob(2).Should().Equal(blob3);
        reader.Entries[2].Kind.Should().Be(BlobKind.Tree);
    }

    [Fact]
    public void Empty_pack_roundtrips_with_zero_entries()
    {
        var packSubKey = PackSubKey(MasterKey);

        using var stream = new MemoryStream();
        var writer = new PackWriter(stream, packSubKey);
        writer.Close();
        writer.Dispose();

        stream.Position = 0;
        var reader = new PackReader(stream, packSubKey);

        reader.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Summary_sha256_matches_an_independent_hash_of_the_file()
    {
        var packSubKey = PackSubKey(MasterKey);

        using var stream = new MemoryStream();
        var writer = new PackWriter(stream, packSubKey);
        writer.AddBlob(NewBlobId(), BlobKind.Data, "data"u8.ToArray());
        var summary = writer.Close();
        writer.Dispose();

        var independentHash = SHA256.HashData(stream.ToArray());

        summary.Sha256.Should().Equal(independentHash);
    }

    [Fact]
    public void Compressible_data_is_stored_with_the_zstd_codec()
    {
        var packSubKey = PackSubKey(MasterKey);
        var compressible = new byte[200_000]; // all zeros

        using var stream = new MemoryStream();
        var writer = new PackWriter(stream, packSubKey);
        writer.AddBlob(NewBlobId(), BlobKind.Data, compressible);
        var summary = writer.Close();
        writer.Dispose();

        summary.Entries[0].Compression.Should().Be(CompressionCodec.Zstd);
        summary.Entries[0].LenStored.Should().BeLessThan((uint)compressible.Length);
    }

    [Fact]
    public void Incompressible_data_is_stored_raw()
    {
        var packSubKey = PackSubKey(MasterKey);
        var incompressible = RandomNumberGeneratorFixture.Bytes(200_000);

        using var stream = new MemoryStream();
        var writer = new PackWriter(stream, packSubKey);
        writer.AddBlob(NewBlobId(), BlobKind.Data, incompressible);
        var summary = writer.Close();
        writer.Dispose();

        summary.Entries[0].Compression.Should().Be(CompressionCodec.Raw);
    }

    [Fact]
    public void Reading_with_the_wrong_master_key_fails_to_open_the_header()
    {
        var packSubKey = PackSubKey(MasterKey);
        var wrongPackSubKey = PackSubKey(MasterKey);

        using var stream = new MemoryStream();
        var writer = new PackWriter(stream, packSubKey);
        writer.AddBlob(NewBlobId(), BlobKind.Data, "data"u8.ToArray());
        writer.Close();
        writer.Dispose();

        stream.Position = 0;
        var act = () => new PackReader(stream, wrongPackSubKey);

        act.Should().Throw<AuthenticationTagMismatchException>();
    }

    [Fact]
    public void Tampering_with_a_blob_byte_is_detected_on_read()
    {
        var packSubKey = PackSubKey(MasterKey);

        using var stream = new MemoryStream();
        var writer = new PackWriter(stream, packSubKey);
        writer.AddBlob(NewBlobId(), BlobKind.Data, RandomNumberGeneratorFixture.Bytes(10_000));
        writer.Close();
        writer.Dispose();

        var bytes = stream.ToArray();
        bytes[20] ^= 0xFF; // inside the first blob's ciphertext region (after the 16-byte salt)
        using var tampered = new MemoryStream(bytes);
        var reader = new PackReader(tampered, packSubKey);

        var act = () => reader.ReadBlob(0);

        act.Should().Throw<AuthenticationTagMismatchException>();
    }

    [Fact]
    public void Tampering_with_the_header_is_detected_on_open()
    {
        var packSubKey = PackSubKey(MasterKey);

        using var stream = new MemoryStream();
        var writer = new PackWriter(stream, packSubKey);
        writer.AddBlob(NewBlobId(), BlobKind.Data, RandomNumberGeneratorFixture.Bytes(10_000));
        writer.Close();
        writer.Dispose();

        var bytes = stream.ToArray();
        bytes[^10] ^= 0xFF; // inside the encrypted header, before the length footer
        using var tampered = new MemoryStream(bytes);

        var act = () => new PackReader(tampered, packSubKey);

        act.Should().Throw<AuthenticationTagMismatchException>();
    }

    [Fact]
    public void AddBlob_after_Close_throws()
    {
        var packSubKey = PackSubKey(MasterKey);

        using var stream = new MemoryStream();
        var writer = new PackWriter(stream, packSubKey);
        writer.Close();

        var act = () => writer.AddBlob(NewBlobId(), BlobKind.Data, "x"u8.ToArray());

        act.Should().Throw<InvalidOperationException>();
        writer.Dispose();
    }

    [Fact]
    public void Rebuilding_entries_from_the_raw_pack_file_alone_reproduces_the_catalog()
    {
        // Simulates "repo rebuild-index": no separate catalog is consulted —
        // the pack file is self-describing given only the repository key.
        var masterKey = MasterKey;
        var packSubKey = PackSubKey(masterKey);
        var ids = new[] { NewBlobId(), NewBlobId(), NewBlobId() };
        var payloads = new[] { "a"u8.ToArray(), "bb"u8.ToArray(), "ccc"u8.ToArray() };

        using var stream = new MemoryStream();
        var writer = new PackWriter(stream, packSubKey);
        for (var i = 0; i < ids.Length; i++)
        {
            writer.AddBlob(ids[i], BlobKind.Data, payloads[i]);
        }

        var originalSummary = writer.Close();
        writer.Dispose();

        stream.Position = 0;
        var rebuilt = new PackReader(stream, packSubKey);

        rebuilt.Entries.Select(e => e.BlobId).Should().BeEquivalentTo(originalSummary.Entries.Select(e => e.BlobId));
        for (var i = 0; i < ids.Length; i++)
        {
            rebuilt.ReadBlob(i).Should().Equal(payloads[i]);
        }
    }
}
