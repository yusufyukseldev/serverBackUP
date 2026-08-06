using FluentAssertions;
using ServerBackup.Core.Crypto;
using Xunit;

namespace ServerBackup.Core.Tests.Crypto;

public sealed class Argon2idKeyDerivationTests
{
    // RFC 9106 §5.3 Argon2id test vector — verified independently from the RFC text,
    // not from memory. Uses low-memory parameters (m=32 KiB) unrelated to the
    // production KDF parameters; this only confirms our parameter wiring
    // (secret/associated data/salt/output length) matches the reference algorithm.
    [Fact]
    public void DeriveKeyCore_matches_Rfc9106_Argon2id_test_vector()
    {
        var password = Enumerable.Repeat((byte)0x01, 32).ToArray();
        var salt = Enumerable.Repeat((byte)0x02, 16).ToArray();
        var secret = Enumerable.Repeat((byte)0x03, 8).ToArray();
        var associatedData = Enumerable.Repeat((byte)0x04, 12).ToArray();

        var tag = Argon2idKeyDerivation.DeriveKeyCore(
            password,
            salt,
            secret,
            associatedData,
            memorySizeKiB: 32,
            iterations: 3,
            degreeOfParallelism: 4,
            outputSizeBytes: 32);

        Convert.ToHexStringLower(tag).Should().Be(
            "0d640df58d78766c08c037a34a8b53c9d01ef0452d75b65eb52520e96b01e659");
    }

    [Fact]
    public void DeriveKey_is_deterministic_for_same_password_and_salt()
    {
        var password = "correct horse battery staple"u8.ToArray();
        var salt = RandomNumberGeneratorFixture.Bytes(Argon2idParameters.SaltSizeBytes);

        var key1 = Argon2idKeyDerivation.DeriveKey(password, salt);
        var key2 = Argon2idKeyDerivation.DeriveKey(password, salt);

        key1.Should().Equal(key2);
    }

    [Fact]
    public void DeriveKey_produces_different_output_for_different_salt()
    {
        var password = "correct horse battery staple"u8.ToArray();
        var salt1 = RandomNumberGeneratorFixture.Bytes(Argon2idParameters.SaltSizeBytes);
        var salt2 = RandomNumberGeneratorFixture.Bytes(Argon2idParameters.SaltSizeBytes);

        var key1 = Argon2idKeyDerivation.DeriveKey(password, salt1);
        var key2 = Argon2idKeyDerivation.DeriveKey(password, salt2);

        key1.Should().NotEqual(key2);
    }

    [Fact]
    public void DeriveKey_rejects_wrong_salt_length()
    {
        var password = "password"u8.ToArray();
        var badSalt = new byte[8];

        var act = () => Argon2idKeyDerivation.DeriveKey(password, badSalt);

        act.Should().Throw<ArgumentException>();
    }
}
