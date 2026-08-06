using System.Security.Cryptography;

namespace ServerBackup.Core.Crypto;

/// <summary>
/// Wraps a byte array holding key material and guarantees it is zeroed when
/// disposed, even on an exception path. Use for master keys and KEKs that
/// live longer than a single method call.
/// </summary>
public sealed class SecureBuffer : IDisposable
{
    private byte[]? _buffer;

    public SecureBuffer(int length) => _buffer = new byte[length];

    public SecureBuffer(byte[] buffer) => _buffer = buffer;

    public Span<byte> Span => (_buffer ?? throw new ObjectDisposedException(nameof(SecureBuffer))).AsSpan();

    public ReadOnlySpan<byte> ReadOnlySpan => Span;

    public int Length => (_buffer ?? throw new ObjectDisposedException(nameof(SecureBuffer))).Length;

    public void Dispose()
    {
        if (_buffer is { } buffer)
        {
            CryptographicOperations.ZeroMemory(buffer);
            _buffer = null;
        }
    }
}
