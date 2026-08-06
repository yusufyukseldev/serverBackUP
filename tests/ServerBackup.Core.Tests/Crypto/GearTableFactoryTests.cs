using FluentAssertions;
using ServerBackup.Core.Crypto;
using Xunit;

namespace ServerBackup.Core.Tests.Crypto;

public sealed class GearTableFactoryTests
{
    [Fact]
    public void Derive_produces_256_entries()
    {
        var masterKey = RandomNumberGeneratorFixture.Bytes(32);

        var table = GearTableFactory.Derive(masterKey);

        table.Should().HaveCount(GearTableFactory.EntryCount);
    }

    [Fact]
    public void Derive_is_deterministic_for_the_same_master_key()
    {
        var masterKey = RandomNumberGeneratorFixture.Bytes(32);

        var table1 = GearTableFactory.Derive(masterKey);
        var table2 = GearTableFactory.Derive(masterKey);

        table1.Should().Equal(table2);
    }

    [Fact]
    public void Derive_differs_for_different_master_keys()
    {
        var table1 = GearTableFactory.Derive(RandomNumberGeneratorFixture.Bytes(32));
        var table2 = GearTableFactory.Derive(RandomNumberGeneratorFixture.Bytes(32));

        table1.Should().NotEqual(table2);
    }

    [Fact]
    public void Derive_entries_are_effectively_unique()
    {
        var masterKey = RandomNumberGeneratorFixture.Bytes(32);

        var table = GearTableFactory.Derive(masterKey);

        // Collisions are possible in principle (256 random 64-bit values), but the
        // probability is astronomically small; any duplicate here signals a bug
        // (e.g. reading the same HKDF output slice twice).
        table.Distinct().Should().HaveCount(GearTableFactory.EntryCount);
    }
}
