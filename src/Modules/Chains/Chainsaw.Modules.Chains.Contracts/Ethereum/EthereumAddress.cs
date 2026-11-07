using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// An Ethereum address: <c>0x</c> and 20 bytes in hex. It can only be made by <c>TryParse</c>, which checks the
/// EIP-55 checksum when the text has one: an address written in mixed case carries its checksum in the letters' case,
/// and a single wrong letter makes it another address that belongs to nobody, so a mixed-case address whose case does
/// not match is refused. All lower or all upper case has no checksum and is taken as written.
/// </summary>
public sealed record EthereumAddress
{
    /// <summary>The number of hex digits after <c>0x</c>.</summary>
    public const int Digits = 40;

    /// <summary>The hex digits in both cases.</summary>
    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    /// <summary>Creates an address from its checked lower-case form.</summary>
    /// <param name="lower">The address in lower case with <c>0x</c>.</param>
    private EthereumAddress(string lower)
    {
        Value = lower;
        Checksummed = Checksum(lower);
    }

    /// <summary>Gets the address in lower case, as the chain compares it, for example <c>0x5aae…</c>.</summary>
    public string Value { get; }

    /// <summary>Gets the address with its EIP-55 checksum in the letters' case, as it is shown.</summary>
    public string Checksummed { get; }

    /// <summary>Gets the address as an address on any chain.</summary>
    public ChainAddress ToChainAddress() => new(Chain.Ethereum, Value);

    /// <summary>Reads an address, checking the EIP-55 checksum when the letters are in mixed case.</summary>
    /// <param name="text">The text; white space around it is ignored.</param>
    /// <param name="address">The address, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the text is an address.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out EthereumAddress? address) =>
        TryParse(text, out address, out _);

    /// <summary>Reads an address, and says why it is not one.</summary>
    /// <param name="text">The text; white space around it is ignored.</param>
    /// <param name="address">The address, or <see langword="null"/>.</param>
    /// <param name="problem">Why it is not an address, in Swedish, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the text is an address.</returns>
    public static bool TryParse(
        [NotNullWhen(true)] string? text, [NotNullWhen(true)] out EthereumAddress? address, out string? problem)
    {
        address = null;
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length != Digits + 2 || !trimmed.StartsWith("0x", StringComparison.Ordinal)
            || trimmed.AsSpan(2).ContainsAnyExcept(HexDigits))
        {
            problem = "En Ethereum-adress är 0x och 40 hexadecimala tecken.";
            return false;
        }

        var digits = trimmed[2..];
        var lower = "0x" + digits.ToLowerInvariant();
        var mixed = digits.Any(char.IsLower) && digits.Any(char.IsUpper);
        if (mixed && !string.Equals(Checksum(lower), trimmed, StringComparison.Ordinal))
        {
            problem = "Adressens kontrollsumma (EIP-55, stora och små bokstäver) stämmer inte. Ett tecken kan vara fel.";
            return false;
        }

        problem = null;
        address = new EthereumAddress(lower);
        return true;
    }

    /// <summary>Reads an address that must be one, as a source's answer gives it.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The address.</returns>
    /// <exception cref="FormatException">The text is not an address.</exception>
    public static EthereumAddress Parse(string text) =>
        TryParse(text, out var address, out var problem) ? address : throw new FormatException(problem);

    /// <summary>Writes the address with its EIP-55 checksum.</summary>
    /// <returns>The checksummed address.</returns>
    public override string ToString() => Checksummed;

    /// <summary>
    /// Gives the EIP-55 form: the Keccak-256 of the lower-case hex (without <c>0x</c>) decides each letter's case, upper
    /// where the hash's matching nibble is 8 or more.
    /// </summary>
    /// <param name="lower">The address in lower case with <c>0x</c>.</param>
    /// <returns>The checksummed address.</returns>
    private static string Checksum(string lower)
    {
        var hash = Keccak256.Hash(lower[2..]);
        return string.Create(lower.Length, (lower, hash), static (span, state) =>
        {
            var (text, digest) = state;
            span[0] = '0';
            span[1] = 'x';
            for (var i = 0; i < Digits; i++)
            {
                var nibble = (i % 2 == 0 ? digest[i / 2] >> 4 : digest[i / 2]) & 0x0F;
                var c = text[i + 2];
                span[i + 2] = nibble >= 8 ? char.ToUpperInvariant(c) : c;
            }
        });
    }
}
