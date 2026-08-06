using FluentAssertions;
using ServerBackup.Core.Crypto;
using Xunit;

namespace ServerBackup.Core.Tests.Crypto;

public sealed class BlobIdTests
{
    [Fact]
    public void Compute_is_deterministic_for_same_key_and_data()
    {
        var key = RandomNumberGeneratorFixture.Bytes(32);
        var data = "chunk contents"u8.ToArray();

        var id1 = BlobId.Compute(key, data);
        var id2 = BlobId.Compute(key, data);

        id1.Should().Equal(id2);
        id1.Should().HaveCount(BlobId.SizeBytes);
    }

    [Fact]
    public void Compute_differs_for_different_keys_given_same_data()
    {
        var data = "chunk contents"u8.ToArray();
        var key1 = RandomNumberGeneratorFixture.Bytes(32);
        var key2 = RandomNumberGeneratorFixture.Bytes(32);

        var id1 = BlobId.Compute(key1, data);
        var id2 = BlobId.Compute(key2, data);

        id1.Should().NotEqual(id2);
    }

    [Fact]
    public void Compute_differs_for_different_data_given_same_key()
    {
        var key = RandomNumberGeneratorFixture.Bytes(32);

        var id1 = BlobId.Compute(key, "data A"u8.ToArray());
        var id2 = BlobId.Compute(key, "data B"u8.ToArray());

        id1.Should().NotEqual(id2);
    }

    [Fact]
    public void ToHex_produces_lowercase_64_character_string()
    {
        var key = RandomNumberGeneratorFixture.Bytes(32);
        var id = BlobId.Compute(key, "x"u8.ToArray());

        var hex = BlobId.ToHex(id);

        hex.Should().HaveLength(64);
        hex.Should().MatchRegex("^[0-9a-f]{64}$");
    }
}
