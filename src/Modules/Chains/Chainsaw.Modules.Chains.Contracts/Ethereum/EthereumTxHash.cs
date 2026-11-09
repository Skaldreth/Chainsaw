using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// A transaction's id in the account model, on Ethereum or on TRON (the type keeps Ethereum's name, as
/// <see cref="EthereumAddress"/> does). On Ethereum it is <c>0x</c> and 32 bytes in hex, kept in lower case, and the
/// same form names a block's hash (<see cref="EthereumBlockReference.Hash"/>). On TRON it is the 32 bytes in hex
/// without <c>0x</c>, in lower case, as TRON writes its transaction ids and block ids.
/// </summary>
public sealed record EthereumTxHash
{
    /// <summary>The number of hex digits (after <c>0x</c> on Ethereum).</summary>
    public const int Digits = 64;

    /// <summary>The hex digits in both cases.</summary>
    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    /// <summary>Creates a hash from its checked lower-case form.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="value">The hash.</param>
    private EthereumTxHash(Chain chain, string value) => (Chain, Value) = (chain, value);

    /// <summary>Gets the chain the transaction is on: <see cref="Chain.Ethereum"/> or <see cref="Chain.Tron"/>.</summary>
    public Chain Chain { get; }

    /// <summary>Gets the hash in lower case: with <c>0x</c> on Ethereum, without on TRON.</summary>
    public string Value { get; }

    /// <summary>Reads an Ethereum hash.</summary>
    /// <param name="text">The text; white space around it is ignored.</param>
    /// <param name="hash">The hash, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the text is <c>0x</c> and 64 hex digits.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out EthereumTxHash? hash)
    {
        hash = IsHash(text?.Trim()) ? new EthereumTxHash(Chain.Ethereum, text!.Trim().ToLowerInvariant()) : null;
        return hash is not null;
    }

    /// <summary>Reads a transaction's id on an account chain.</summary>
    /// <param name="chain">The chain: Ethereum (<c>0x</c> and 64 hex digits) or TRON (64 hex digits).</param>
    /// <param name="text">The text; white space around it is ignored.</param>
    /// <param name="hash">The hash, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the text is a transaction's id on the chain.</returns>
    public static bool TryParse(
        Chain chain, [NotNullWhen(true)] string? text, [NotNullWhen(true)] out EthereumTxHash? hash)
    {
        hash = null;
        var trimmed = text?.Trim();
        switch (chain)
        {
            case Chain.Ethereum:
                return TryParse(trimmed, out hash);
            case Chain.Tron when trimmed is { Length: Digits } && !trimmed.AsSpan().ContainsAnyExcept(HexDigits):
                hash = new EthereumTxHash(Chain.Tron, trimmed.ToLowerInvariant());
                return true;
            default:
                return false;
        }
    }

    /// <summary>Reads an Ethereum hash that must be one, as a source's answer gives it.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The hash.</returns>
    /// <exception cref="FormatException">The text is not a hash.</exception>
    public static EthereumTxHash Parse(string text) =>
        TryParse(text, out var hash) ? hash : throw new FormatException("A hash is 0x and 64 hex digits.");

    /// <summary>Reads a transaction's id on an account chain that must be one, as the case or a source gives it.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="text">The text.</param>
    /// <returns>The hash.</returns>
    /// <exception cref="FormatException">The text is not a transaction's id on the chain.</exception>
    public static EthereumTxHash Parse(Chain chain, string text) =>
        TryParse(chain, text, out var hash) ? hash : throw new FormatException($"Not a transaction's id on {chain}.");

    /// <summary>Tells whether a text is <c>0x</c> and 64 hex digits.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    internal static bool IsHash([NotNullWhen(true)] string? text) =>
        text is { Length: Digits + 2 } && text.StartsWith("0x", StringComparison.Ordinal)
        && !text.AsSpan(2).ContainsAnyExcept(HexDigits);

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <summary>
    /// Gives the hash shortened for a text the user reads, its first eight characters and an ellipsis (<c>0x</c>
    /// counts on Ethereum), so that a waiting text stays a sentence.
    /// </summary>
    /// <returns>The short hash.</returns>
    public string Brief() => Value[..BriefLength] + "…";

    /// <summary>
    /// Gives a list of hashes shortened for a text: the first few <see cref="Brief"/>, and how many more there are, for
    /// example <c>a1b2c3d4…, e5f6a7b8… och 4 till</c>.
    /// </summary>
    /// <param name="hashes">The hashes.</param>
    /// <returns>The text.</returns>
    public static string BriefList(IReadOnlyCollection<EthereumTxHash> hashes)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        var shown = string.Join(", ", hashes.Take(BriefShown).Select(hash => hash.Brief()));
        var more = hashes.Count - BriefShown;
        return more > 0 ? string.Create(CultureInfo.InvariantCulture, $"{shown} och {more} till") : shown;
    }

    /// <summary>How many characters of a hash a short form keeps.</summary>
    private const int BriefLength = 8;

    /// <summary>How many hashes a short list names before it counts the rest.</summary>
    private const int BriefShown = 3;
}
