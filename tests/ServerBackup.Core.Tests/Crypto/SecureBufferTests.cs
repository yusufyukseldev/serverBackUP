using FluentAssertions;
using ServerBackup.Core.Crypto;
using Xunit;

namespace ServerBackup.Core.Tests.Crypto;

public sealed class SecureBufferTests
{
    [Fact]
    public void Dispose_zeroes_the_underlying_buffer()
    {
        var buffer = RandomNumberGeneratorFixture.Bytes(32);
        var secure = new SecureBuffer(buffer);

        secure.Dispose();

        buffer.Should().OnlyContain(b => b == 0);
    }

    [Fact]
    public void Span_reflects_writes()
    {
        using var secure = new SecureBuffer(4);
        secure.Span[0] = 0xAB;

        secure.Span[0].Should().Be(0xAB);
    }

    [Fact]
    public void Accessing_Span_after_Dispose_throws()
    {
        var secure = new SecureBuffer(4);
        secure.Dispose();

        var act = () => { _ = secure.Span.Length; };

        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var secure = new SecureBuffer(4);

        secure.Dispose();
        var act = () => secure.Dispose();

        act.Should().NotThrow();
    }
}
