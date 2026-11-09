using System.Numerics;
using System.Security.Cryptography;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// Base58Check, the encoding of Bitcoin's original addresses (P2PKH and P2SH): a version byte and a payload, followed
/// by the first four bytes of SHA-256(SHA-256(version and payload)) as a checksum, written with 58 characters that
/// leave out <c>0</c>, <c>O</c>, <c>I</c> and <c>l</c>.
/// </summary>
public static class Base58Check
{
    /// <summary>The 58 characters, in the order of their values.</summary>
    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    /// <summary>The length of the checksum in bytes.</summary>
    private const int ChecksumLength = 4;

    /// <summary>
    /// The longest text that is decoded at all. A Bitcoin address is at most 35 characters; the limit keeps a long
    /// string from costing big-number arithmetic.
    /// </summary>
    private const int MaximumTextLength = 64;

    /// <summary>Encodes bytes with their checksum.</summary>
    /// <param name="data">The version byte and the payload.</param>
    /// <returns>The text.</returns>
    public static string Encode(ReadOnlySpan<byte> data)
    {
        var withChecksum = new byte[data.Length + ChecksumLength];
        data.CopyTo(withChecksum);
        Checksum(data).CopyTo(withChecksum.AsSpan(data.Length));
        return EncodePlain(withChecksum);
    }

    /// <summary>Decodes a text and checks its checksum.</summary>
    /// <param name="text">The text.</param>
    /// <param name="data">The version byte and the payload, without the checksum, when the text is valid.</param>
    /// <returns><see langword="true"/> when the text is Base58 and the checksum holds.</returns>
    public static bool TryDecode(string text, out byte[] data)
    {
        data = [];
        if (string.IsNullOrEmpty(text) || text.Length > MaximumTextLength
            || !TryDecodePlain(text, out var raw) || raw.Length <= ChecksumLength)
        {
            return false;
        }

        var payload = raw.AsSpan(0, raw.Length - ChecksumLength);
        if (!Checksum(payload).SequenceEqual(raw.AsSpan(raw.Length - ChecksumLength)))
        {
            return false;
        }

        data = payload.ToArray();
        return true;
    }

    /// <summary>Computes the checksum of some bytes.</summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The first four bytes of SHA-256(SHA-256(data)).</returns>
    private static byte[] Checksum(ReadOnlySpan<byte> data) =>
        SHA256.HashData(SHA256.HashData(data))[..ChecksumLength];

    /// <summary>Writes bytes in Base58 without a checksum; each leading zero byte becomes a <c>1</c>.</summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The text.</returns>
    internal static string EncodePlain(ReadOnlySpan<byte> data)
    {
        var leadingZeros = 0;
        while (leadingZeros < data.Length && data[leadingZeros] == 0)
        {
            leadingZeros++;
        }

        var value = new BigInteger(data, isUnsigned: true, isBigEndian: true);
        var digits = new Stack<char>();
        while (value > 0)
        {
            value = BigInteger.DivRem(value, Alphabet.Length, out var remainder);
            digits.Push(Alphabet[(int)remainder]);
        }

        return new string('1', leadingZeros) + new string([.. digits]);
    }

    /// <summary>Reads Base58 text into bytes; each leading <c>1</c> becomes a zero byte.</summary>
    /// <param name="text">The text.</param>
    /// <param name="data">The bytes, when every character is in the alphabet.</param>
    /// <returns><see langword="true"/> when every character is in the alphabet.</returns>
    internal static bool TryDecodePlain(string text, out byte[] data)
    {
        data = [];
        var value = BigInteger.Zero;
        foreach (var character in text)
        {
            var digit = Alphabet.IndexOf(character, StringComparison.Ordinal);
            if (digit < 0)
            {
                return false;
            }

            value = (value * Alphabet.Length) + digit;
        }

        var leadingOnes = text.TakeWhile(character => character == '1').Count();
        var body = value.IsZero ? [] : value.ToByteArray(isUnsigned: true, isBigEndian: true);
        data = [.. new byte[leadingOnes], .. body];
        return true;
    }
}
