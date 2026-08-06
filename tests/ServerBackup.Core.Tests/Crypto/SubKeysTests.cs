using System.Security.Cryptography;
using FluentAssertions;
using ServerBackup.Core.Crypto;
using Xunit;

namespace ServerBackup.Core.Tests.Crypto;

public sealed class SubKeysTests
{
    // RFC 5869 Appendix A.1 "Test Case 1" (Basic test case with SHA-256).
    [Fact]
    public void Net_HKDF_matches_Rfc5869_test_case_1()
    {
        var ikm = Convert.FromHexString("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var salt = Convert.FromHexString("000102030405060708090a0b0c");
        var info = Convert.FromHexString("f0f1f2f3f4f5f6f7f8f9");

        var okm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 42, salt, info);

        Convert.ToHexStringLower(okm).Should().Be(
            "3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865");
    }

    [Fact]
    public void Derive_is_deterministic_for_same_master_key_and_info()
    {
        var masterKey = RandomNumberGeneratorFixture.Bytes(32);

        var key1 = SubKeys.Derive(masterKey, SubKeys.ChunkIdInfo);
        var key2 = SubKeys.Derive(masterKey, SubKeys.ChunkIdInfo);

        key1.Should().Equal(key2);
    }

    [Fact]
    public void Derive_produces_distinct_keys_for_each_named_purpose()
    {
        var masterKey = RandomNumberGeneratorFixture.Bytes(32);

        var chunkId = SubKeys.Derive(masterKey, SubKeys.ChunkIdInfo);
        var packKey = SubKeys.Derive(masterKey, SubKeys.PackKeyInfo);
        var gearSeed = SubKeys.Derive(masterKey, SubKeys.GearSeedInfo);
        var meta = SubKeys.Derive(masterKey, SubKeys.MetaInfo);

        var all = new[] { chunkId, packKey, gearSeed, meta };
        all.Select(Convert.ToHexString).Distinct().Should().HaveCount(4);
    }

    [Fact]
    public void DerivePackKey_produces_distinct_keys_for_distinct_salts()
    {
        var masterKey = RandomNumberGeneratorFixture.Bytes(32);
        var packSubKey = SubKeys.Derive(masterKey, SubKeys.PackKeyInfo);

        var key1 = SubKeys.DerivePackKey(packSubKey, RandomNumberGeneratorFixture.Bytes(16));
        var key2 = SubKeys.DerivePackKey(packSubKey, RandomNumberGeneratorFixture.Bytes(16));

        key1.Should().NotEqual(key2);
        key1.Should().HaveCount(AeadCipher.KeySizeBytes);
    }
}
