using System.Diagnostics.CodeAnalysis;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>A block's hash, written as 64 hex characters as block explorers show it. Always lower case.</summary>
public sealed record BlockHash
{
    /// <summary>Creates a checked hash.</summary>
    /// <param name="value">The hash in lower case.</param>
    private BlockHash(string value) => Value = value;

    /// <summary>Gets the hash as 64 lower-case hex characters.</summary>
    public string Value { get; }

    /// <summary>Checks a text and makes a hash of it.</summary>
    /// <param name="text">The text, 64 hex characters in either case.</param>
    /// <param name="hash">The hash, when the text is valid.</param>
    /// <returns><see langword="true"/> when the text is 64 hex characters.</returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out BlockHash? hash)
    {
        hash = Hash256Text.TryNormalise(text, out var value) ? new BlockHash(value) : null;
        return hash is not null;
    }

    /// <summary>Makes a hash of a text that must be valid.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The hash.</returns>
    /// <exception cref="FormatException">The text is not 64 hex characters.</exception>
    public static BlockHash Parse(string text) =>
        TryParse(text, out var hash) ? hash : throw new FormatException("A block hash is 64 hex characters.");

    /// <summary>
    /// Reads an Ethereum block's hash: <c>0x</c> and 64 hex digits, kept in lower case with its prefix, as Ethereum's
    /// tools write it, so that a step can be checked against the chain as it is shown.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="hash">The hash, when the text is one.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool TryParseEthereum(string? text, [NotNullWhen(true)] out BlockHash? hash)
    {
        hash = EthereumTxHash.TryParse(text, out var parsed) ? new BlockHash(parsed.Value) : null;
        return hash is not null;
    }

    /// <summary>Gives the hash.</summary>
    /// <returns><see cref="Value"/>.</returns>
    public override string ToString() => Value;
}
