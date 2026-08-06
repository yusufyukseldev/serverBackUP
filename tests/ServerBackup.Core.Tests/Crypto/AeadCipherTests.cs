using System.Security.Cryptography;
using FluentAssertions;
using ServerBackup.Core.Crypto;
using Xunit;

namespace ServerBackup.Core.Tests.Crypto;

public sealed class AeadCipherTests
{
    [Fact]
    public void Seal_then_Open_roundtrips_plaintext()
    {
        var key = RandomNumberGeneratorFixture.Bytes(AeadCipher.KeySizeBytes);
        var nonce = RandomNumberGeneratorFixture.Bytes(AeadCipher.NonceSizeBytes);
        var plaintext = "hello, ServerBackup"u8.ToArray();

        var sealedData = AeadCipher.Seal(key, nonce, plaintext);
        var opened = AeadCipher.Open(key, nonce, sealedData);

        opened.Should().Equal(plaintext);
    }

    [Fact]
    public void Seal_then_Open_roundtrips_empty_plaintext()
    {
        var key = RandomNumberGeneratorFixture.Bytes(AeadCipher.KeySizeBytes);
        var nonce = RandomNumberGeneratorFixture.Bytes(AeadCipher.NonceSizeBytes);

        var sealedData = AeadCipher.Seal(key, nonce, ReadOnlySpan<byte>.Empty);
        var opened = AeadCipher.Open(key, nonce, sealedData);

        opened.Should().BeEmpty();
        sealedData.Should().HaveCount(AeadCipher.TagSizeBytes);
    }

    [Fact]
    public void Open_throws_when_ciphertext_is_tampered()
    {
        var key = RandomNumberGeneratorFixture.Bytes(AeadCipher.KeySizeBytes);
        var nonce = RandomNumberGeneratorFixture.Bytes(AeadCipher.NonceSizeBytes);
        var sealedData = AeadCipher.Seal(key, nonce, "sensitive data"u8.ToArray());
        sealedData[0] ^= 0xFF;

        var act = () => AeadCipher.Open(key, nonce, sealedData);

        act.Should().Throw<AuthenticationTagMismatchException>();
    }

    [Fact]
    public void Open_throws_when_tag_is_tampered()
    {
        var key = RandomNumberGeneratorFixture.Bytes(AeadCipher.KeySizeBytes);
        var nonce = RandomNumberGeneratorFixture.Bytes(AeadCipher.NonceSizeBytes);
        var sealedData = AeadCipher.Seal(key, nonce, "sensitive data"u8.ToArray());
        sealedData[^1] ^= 0xFF;

        var act = () => AeadCipher.Open(key, nonce, sealedData);

        act.Should().Throw<AuthenticationTagMismatchException>();
    }

    [Fact]
    public void Open_throws_with_wrong_key()
    {
        var key = RandomNumberGeneratorFixture.Bytes(AeadCipher.KeySizeBytes);
        var wrongKey = RandomNumberGeneratorFixture.Bytes(AeadCipher.KeySizeBytes);
        var nonce = RandomNumberGeneratorFixture.Bytes(AeadCipher.NonceSizeBytes);
        var sealedData = AeadCipher.Seal(key, nonce, "sensitive data"u8.ToArray());

        var act = () => AeadCipher.Open(wrongKey, nonce, sealedData);

        act.Should().Throw<AuthenticationTagMismatchException>();
    }

    [Fact]
    public void Open_throws_with_wrong_associated_data()
    {
        var key = RandomNumberGeneratorFixture.Bytes(AeadCipher.KeySizeBytes);
        var nonce = RandomNumberGeneratorFixture.Bytes(AeadCipher.NonceSizeBytes);
        var sealedData = AeadCipher.Seal(key, nonce, "sensitive data"u8.ToArray(), "aad-1"u8.ToArray());

        var act = () => AeadCipher.Open(key, nonce, sealedData, "aad-2"u8.ToArray());

        act.Should().Throw<AuthenticationTagMismatchException>();
    }
}
