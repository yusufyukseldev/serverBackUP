using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using ServerBackup.Core.Crypto;
using Xunit;

namespace ServerBackup.Core.Tests.Crypto;

public sealed class PackNonceTests
{
    [Fact]
    public void ForBlobIndex_produces_a_12_byte_nonce()
    {
        PackNonce.ForBlobIndex(0).Should().HaveCount(AeadCipher.NonceSizeBytes);
    }

    [Fact]
    public void ForBlobIndex_first_four_bytes_are_always_zero()
    {
        var nonce = PackNonce.ForBlobIndex(123456789UL);

        nonce[..4].Should().Equal(new byte[4]);
    }

    [Property]
    public bool No_two_blob_indices_within_a_pack_produce_the_same_nonce(NonNegativeInt indexA, NonNegativeInt indexB)
    {
        var a = (ulong)indexA.Get;
        var b = (ulong)indexB.Get;
        var sameNonce = PackNonce.ForBlobIndex(a).AsSpan().SequenceEqual(PackNonce.ForBlobIndex(b));

        return (a != b) == !sameNonce;
    }

    [Property]
    public bool ForBlobIndex_is_deterministic(NonNegativeInt index)
    {
        var i = (ulong)index.Get;

        return PackNonce.ForBlobIndex(i).AsSpan().SequenceEqual(PackNonce.ForBlobIndex(i));
    }

    [Fact]
    public void HeaderBlobIndex_nonce_does_not_collide_with_any_ordinary_blob_index_nonce_in_practice()
    {
        // The header uses ulong.MaxValue as a reserved index. A pack can never contain
        // that many blobs, so this can't collide with real blob nonces in practice.
        var headerNonce = PackNonce.ForBlobIndex(PackNonce.HeaderBlobIndex);
        var firstBlobNonce = PackNonce.ForBlobIndex(0);

        headerNonce.Should().NotEqual(firstBlobNonce);
    }
}
