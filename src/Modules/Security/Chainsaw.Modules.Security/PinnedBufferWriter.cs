using System.Buffers;
using System.Security.Cryptography;

namespace Chainsaw.Modules.Security;

/// <summary>
/// A buffer for plaintext that the garbage collector never moves or copies: it grows into new pinned arrays and clears
/// every array it leaves behind, and <see cref="Dispose"/> clears the last one. The vault serializes its content into
/// it, so that the plaintext JSON exists in one place that is wiped, instead of in the growing, unpinned buffers of
/// <c>JsonSerializer.SerializeToUtf8Bytes</c>.
/// </summary>
internal sealed class PinnedBufferWriter : IBufferWriter<byte>, IDisposable
{
    /// <summary>The first size of the buffer; a vault is a few kilobytes.</summary>
    private const int InitialSize = 16 * 1024;

    /// <summary>The pinned buffer.</summary>
    private byte[] _buffer = VaultCrypto.AllocateSecret(InitialSize);

    /// <summary>How many bytes have been written.</summary>
    private int _written;

    /// <summary>Gets the bytes written so far.</summary>
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

    /// <inheritdoc />
    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, _buffer.Length - _written);
        _written += count;
    }

    /// <inheritdoc />
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Grow(sizeHint);
        return _buffer.AsMemory(_written);
    }

    /// <inheritdoc />
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Grow(sizeHint);
        return _buffer.AsSpan(_written);
    }

    /// <summary>Clears the buffer.</summary>
    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_buffer);
        _written = 0;
    }

    /// <summary>Makes room for at least the hinted number of bytes, clearing the array that is left.</summary>
    /// <param name="sizeHint">The bytes wanted; 0 means at least one.</param>
    private void Grow(int sizeHint)
    {
        var needed = Math.Max(sizeHint, 1);
        if (_buffer.Length - _written >= needed)
        {
            return;
        }

        var larger = VaultCrypto.AllocateSecret(Math.Max(_buffer.Length * 2, _written + needed));
        _buffer.AsSpan(0, _written).CopyTo(larger);
        CryptographicOperations.ZeroMemory(_buffer);
        _buffer = larger;
    }
}
