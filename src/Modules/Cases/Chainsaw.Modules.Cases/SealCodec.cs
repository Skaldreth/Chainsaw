using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// The hash of a sealed record: SHA-256 over a fixed domain, the previous record's hash, the chain's name, the
/// record's sequence number and its values, each value with a type tag and, for text and bytes, its length. The
/// encoding is unambiguous: two different records can never give the same bytes.
/// </summary>
internal static class SealCodec
{
    /// <summary>The size of a hash.</summary>
    public const int HashSize = 32;

    /// <summary>The domain that every sealed hash starts with, so that it cannot be mistaken for another hash.</summary>
    private const string Domain = "chainsaw/seal/v1";

    /// <summary>The tag of SQL NULL.</summary>
    private const byte NullTag = 0;

    /// <summary>The tag of a 64-bit integer, followed by 8 bytes big-endian.</summary>
    private const byte IntegerTag = 1;

    /// <summary>The tag of a 64-bit float, followed by its 8 IEEE 754 bytes big-endian.</summary>
    private const byte RealTag = 2;

    /// <summary>The tag of text, followed by its UTF-8 length (4 bytes big-endian) and bytes.</summary>
    private const byte TextTag = 3;

    /// <summary>The tag of bytes, followed by their length (4 bytes big-endian) and the bytes.</summary>
    private const byte BlobTag = 4;

    /// <summary>The UTF-8 encoding that refuses lone surrogates instead of replacing them.</summary>
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Gets the hash before the first record of every chain: 32 zero bytes.</summary>
    public static byte[] Genesis => new byte[HashSize];

    /// <summary>Computes a record's hash.</summary>
    /// <param name="previous">The previous record's hash, or <see cref="Genesis"/> for the first.</param>
    /// <param name="chain">The chain's name.</param>
    /// <param name="sequence">The record's sequence number.</param>
    /// <param name="values">
    /// The content values: <see langword="null"/>, <see cref="DBNull"/>, <see cref="long"/>, <see cref="double"/>,
    /// <see cref="string"/> or a <see cref="byte"/> array.
    /// </param>
    /// <returns>The 32-byte hash.</returns>
    /// <exception cref="ArgumentException">A value has another type, or text is not valid Unicode.</exception>
    public static byte[] Hash(ReadOnlySpan<byte> previous, string chain, long sequence, IReadOnlyList<object?> values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, Domain);
        hash.AppendData(previous);
        AppendText(hash, chain);
        AppendInt64(hash, sequence);
        AppendInt32(hash, values.Count);
        foreach (var value in values)
        {
            AppendValue(hash, value);
        }

        return hash.GetHashAndReset();
    }

    /// <summary>Appends one tagged value.</summary>
    /// <param name="hash">The hash being computed.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="ArgumentException">The value has a type that cannot be sealed.</exception>
    private static void AppendValue(IncrementalHash hash, object? value)
    {
        switch (value)
        {
            case null or DBNull:
                hash.AppendData([NullTag]);
                break;
            case long integer:
                hash.AppendData([IntegerTag]);
                AppendInt64(hash, integer);
                break;
            case double real:
                hash.AppendData([RealTag]);
                AppendInt64(hash, BitConverter.DoubleToInt64Bits(real));
                break;
            case string text:
                hash.AppendData([TextTag]);
                AppendText(hash, text);
                break;
            case byte[] bytes:
                hash.AppendData([BlobTag]);
                AppendInt32(hash, bytes.Length);
                hash.AppendData(bytes);
                break;
            default:
                throw new ArgumentException($"A value of type {value.GetType().Name} cannot be sealed.", nameof(value));
        }
    }

    /// <summary>Appends text as its length and UTF-8 bytes.</summary>
    /// <param name="hash">The hash being computed.</param>
    /// <param name="text">The text.</param>
    private static void AppendText(IncrementalHash hash, string text)
    {
        byte[] bytes;
        try
        {
            bytes = StrictUtf8.GetBytes(text);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("Text to be sealed must be valid Unicode.", nameof(text), exception);
        }

        AppendInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    /// <summary>Appends a 64-bit integer big-endian.</summary>
    /// <param name="hash">The hash being computed.</param>
    /// <param name="value">The value.</param>
    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        hash.AppendData(bytes);
    }

    /// <summary>Appends a 32-bit integer big-endian.</summary>
    /// <param name="hash">The hash being computed.</param>
    /// <param name="value">The value.</param>
    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        hash.AppendData(bytes);
    }
}
