namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// Plain Base58, without a checksum: how Solana writes its addresses (32 bytes), its transaction signatures (64 bytes)
/// and its block hashes (32 bytes), with Bitcoin's alphabet. Unlike <see cref="Base58Check"/> it carries no checksum,
/// so a single wrong character gives another valid value that belongs to nobody; only the length can be checked.
/// </summary>
public static class Base58
{
    /// <summary>
    /// The longest text that is decoded at all: a 64-byte signature is at most 88 characters. The limit keeps a long
    /// string from costing big-number arithmetic.
    /// </summary>
    private const int MaximumTextLength = 90;

    /// <summary>Writes bytes in Base58; each leading zero byte becomes a <c>1</c>.</summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The text.</returns>
    public static string Encode(ReadOnlySpan<byte> data) => Base58Check.EncodePlain(data);

    /// <summary>Reads Base58 text of an exact number of bytes, in its one canonical form.</summary>
    /// <param name="text">The text, without white space.</param>
    /// <param name="length">How many bytes it must hold.</param>
    /// <param name="data">The bytes, when the text holds exactly that many.</param>
    /// <returns>
    /// <see langword="true"/> when every character is in the alphabet, the text holds exactly <paramref name="length"/>
    /// bytes and is written as the encoding writes them (so that one value is never two texts).
    /// </returns>
    public static bool TryDecode(string? text, int length, out byte[] data)
    {
        data = [];
        if (string.IsNullOrEmpty(text) || text.Length > MaximumTextLength
            || !Base58Check.TryDecodePlain(text, out var raw) || raw.Length != length
            || !string.Equals(Encode(raw), text, StringComparison.Ordinal))
        {
            return false;
        }

        data = raw;
        return true;
    }
}
