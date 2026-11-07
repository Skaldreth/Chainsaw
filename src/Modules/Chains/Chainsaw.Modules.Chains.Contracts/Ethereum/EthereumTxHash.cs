using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// An Ethereum transaction's hash: <c>0x</c> and 32 bytes in hex, kept in lower case. The same form names a block's
/// hash (<see cref="EthereumBlockReference.Hash"/>).
/// </summary>
public sealed record EthereumTxHash
{
    /// <summary>The number of hex digits after <c>0x</c>.</summary>
    public const int Digits = 64;

    /// <summary>The hex digits in both cases.</summary>
    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    /// <summary>Creates a hash from its checked lower-case form.</summary>
    /// <param name="value">The hash.</param>
    private EthereumTxHash(string value) => Value = value;

    /// <summary>Gets the hash in lower case with <c>0x</c>.</summary>
    public string Value { get; }

    /// <summary>Reads a hash.</summary>
    /// <param name="text">The text; white space around it is ignored.</param>
    /// <param name="hash">The hash, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the text is <c>0x</c> and 64 hex digits.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out EthereumTxHash? hash)
    {
        hash = IsHash(text?.Trim()) ? new EthereumTxHash(text!.Trim().ToLowerInvariant()) : null;
        return hash is not null;
    }

    /// <summary>Reads a hash that must be one, as a source's answer gives it.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The hash.</returns>
    /// <exception cref="FormatException">The text is not a hash.</exception>
    public static EthereumTxHash Parse(string text) =>
        TryParse(text, out var hash) ? hash : throw new FormatException("A hash is 0x and 64 hex digits.");

    /// <summary>Tells whether a text is <c>0x</c> and 64 hex digits.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    internal static bool IsHash([NotNullWhen(true)] string? text) =>
        text is { Length: Digits + 2 } && text.StartsWith("0x", StringComparison.Ordinal)
        && !text.AsSpan(2).ContainsAnyExcept(HexDigits);

    /// <inheritdoc/>
    public override string ToString() => Value;
}
