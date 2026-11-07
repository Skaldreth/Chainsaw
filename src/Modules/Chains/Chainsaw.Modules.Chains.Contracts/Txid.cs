using System.Diagnostics.CodeAnalysis;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// A transaction's id: the double SHA-256 of the transaction (without witness data), written as 64 hex characters in
/// the byte order block explorers show. Always lower case.
/// </summary>
public sealed record Txid
{
    /// <summary>Creates a checked id.</summary>
    /// <param name="value">The id in lower case.</param>
    private Txid(string value) => Value = value;

    /// <summary>Gets the id as 64 lower-case hex characters.</summary>
    public string Value { get; }

    /// <summary>Checks a text and makes an id of it.</summary>
    /// <param name="text">The text, 64 hex characters in either case.</param>
    /// <param name="txid">The id, when the text is valid.</param>
    /// <returns><see langword="true"/> when the text is 64 hex characters.</returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out Txid? txid)
    {
        txid = Hash256Text.TryNormalise(text, out var value) ? new Txid(value) : null;
        return txid is not null;
    }

    /// <summary>Makes an id of a text that must be valid.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The id.</returns>
    /// <exception cref="FormatException">The text is not 64 hex characters.</exception>
    public static Txid Parse(string text) =>
        TryParse(text, out var txid) ? txid : throw new FormatException("A txid is 64 hex characters.");

    /// <summary>Gives the id.</summary>
    /// <returns><see cref="Value"/>.</returns>
    public override string ToString() => Value;
}
