using System.Security.Cryptography;
using FluentAssertions;
using ServerBackup.Core.Crypto;
using Xunit;

namespace ServerBackup.Core.Tests.Crypto;

public sealed class MasterKeyFileTests
{
    [Fact]
    public void CreateNew_then_Unwrap_recovers_the_same_master_key()
    {
        var password = "correct horse battery staple"u8.ToArray();

        var (masterKey, keyFile) = MasterKeyFile.CreateNew(password);
        var recovered = MasterKeyFile.Unwrap(keyFile, password);

        recovered.Should().Equal(masterKey);
    }

    [Fact]
    public void Unwrap_throws_for_wrong_password()
    {
        var (_, keyFile) = MasterKeyFile.CreateNew("right-password"u8.ToArray());

        var act = () => MasterKeyFile.Unwrap(keyFile, "wrong-password"u8.ToArray());

        act.Should().Throw<AuthenticationTagMismatchException>();
    }

    [Fact]
    public void Unwrap_throws_for_unsupported_format_version()
    {
        var (_, keyFile) = MasterKeyFile.CreateNew("password"u8.ToArray());
        var futureVersion = keyFile with { FormatVersion = 99 };

        var act = () => MasterKeyFile.Unwrap(futureVersion, "password"u8.ToArray());

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Wrap_allows_the_same_master_key_to_be_recovered_via_a_second_password()
    {
        var masterKey = RandomNumberGeneratorFixture.Bytes(AeadCipher.KeySizeBytes);

        var keyFileA = MasterKeyFile.Wrap(masterKey, "password-A"u8.ToArray());
        var keyFileB = MasterKeyFile.Wrap(masterKey, "password-B"u8.ToArray());

        MasterKeyFile.Unwrap(keyFileA, "password-A"u8.ToArray()).Should().Equal(masterKey);
        MasterKeyFile.Unwrap(keyFileB, "password-B"u8.ToArray()).Should().Equal(masterKey);
        keyFileA.KeyId.Should().NotBe(keyFileB.KeyId);
    }

    [Fact]
    public void CreateNew_generates_a_full_size_random_master_key()
    {
        var (masterKey, _) = MasterKeyFile.CreateNew("password"u8.ToArray());

        masterKey.Should().HaveCount(AeadCipher.KeySizeBytes);
        masterKey.Any(b => b != 0).Should().BeTrue();
    }
}
