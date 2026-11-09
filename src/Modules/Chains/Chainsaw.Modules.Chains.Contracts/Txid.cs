using System.Diagnostics.CodeAnalysis;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// A transaction's id on a chain of unspent outputs: 64 hex characters in the byte order block explorers show, always
/// lower case, and the chain it is on (<see cref="Chain"/>). Bitcoin's and Litecoin's ids are the double SHA-256 of
/// the transaction without witness data; Zcash's newer ones (version 5) are a BLAKE2b tree over its parts. Two ids
/// with the same hex on two chains are two transactions, so the chain is part of the id and of its equality.
/// </summary>
public sealed record Txid
{
    /// <summary>Creates a checked id.</summary>
    /// <param name="value">The id in lower case.</param>
    /// <param name="chain">The chain, one of unspent outputs.</param>
    private Txid(string value, Chain chain) => (Value, Chain) = (value, chain);

    /// <summary>Gets the id as 64 lower-case hex characters.</summary>
    public string Value { get; }

    /// <summary>Gets the chain the transaction is on; Bitcoin unless the id was read for another chain.</summary>
    public Chain Chain { get; }

    /// <summary>Checks a text and makes an id of it on Bitcoin.</summary>
    /// <param name="text">The text, 64 hex characters in either case.</param>
    /// <param name="txid">The id, when the text is valid.</param>
    /// <returns><see langword="true"/> when the text is 64 hex characters.</returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out Txid? txid) =>
        TryParse(Chain.Bitcoin, text, out txid);

    /// <summary>Checks a text and makes an id of it on a chain of unspent outputs.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="text">The text, 64 hex characters in either case.</param>
    /// <param name="txid">The id, when the text is valid.</param>
    /// <returns><see langword="true"/> when the text is 64 hex characters.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The chain does not keep unspent outputs.</exception>
    public static bool TryParse(Chain chain, string? text, [NotNullWhen(true)] out Txid? txid)
    {
        CheckChain(chain);
        txid = Hash256Text.TryNormalise(text, out var value) ? new Txid(value, chain) : null;
        return txid is not null;
    }

    /// <summary>Makes an id on Bitcoin of a text that must be valid.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The id.</returns>
    /// <exception cref="FormatException">The text is not 64 hex characters.</exception>
    public static Txid Parse(string text) => Parse(Chain.Bitcoin, text);

    /// <summary>Makes an id on a chain of unspent outputs of a text that must be valid.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="text">The text.</param>
    /// <returns>The id.</returns>
    /// <exception cref="FormatException">The text is not 64 hex characters.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The chain does not keep unspent outputs.</exception>
    public static Txid Parse(Chain chain, string text) =>
        TryParse(chain, text, out var txid) ? txid : throw new FormatException("A txid is 64 hex characters.");

    /// <summary>Gives the same id on another chain of unspent outputs, as a source for that chain reads it.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>The id on the chain; this one when it is already on it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The chain does not keep unspent outputs.</exception>
    public Txid On(Chain chain)
    {
        CheckChain(chain);
        return chain == Chain ? this : new Txid(Value, chain);
    }

    /// <summary>
    /// Gets the id as the case stores it where no column says the chain: Bitcoin's as it always was, and another
    /// chain's with the chain before it (<c>litecoin:…</c>, <c>zcash:…</c>), so that what is stored about Bitcoin never
    /// changes and the text still says its chain. <see cref="TryParseStored"/> reads it back.
    /// </summary>
    public string Stored => Chain == Chain.Bitcoin ? Value : Chain.ToString().ToLowerInvariant() + ":" + Value;

    /// <summary>Reads an id as <see cref="Stored"/> wrote it.</summary>
    /// <param name="text">The stored text.</param>
    /// <param name="txid">The id on its chain, when the text is one.</param>
    /// <returns><see langword="true"/> when the text is an id, bare on Bitcoin or with a UTXO chain before it.</returns>
    public static bool TryParseStored(string? text, [NotNullWhen(true)] out Txid? txid)
    {
        txid = null;
        if (text is null)
        {
            return false;
        }

        var colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return TryParse(Chain.Bitcoin, text, out txid);
        }

        var name = text[..colon];
        var chain = Enum.GetValues<Chain>().Where(known => known != Chain.Bitcoin && known.Model() == LedgerModel.Utxo)
            .Select(known => (Chain?)known)
            .FirstOrDefault(known => known!.Value.ToString().ToLowerInvariant() == name);
        return chain is { } on && TryParse(on, text[(colon + 1)..], out txid);
    }

    /// <summary>Gives the id.</summary>
    /// <returns><see cref="Value"/>, without the chain, as explorers write it.</returns>
    public override string ToString() => Value;

    /// <summary>Checks that a chain keeps unspent outputs, the only kind whose transactions this id names.</summary>
    /// <param name="chain">The chain.</param>
    /// <exception cref="ArgumentOutOfRangeException">It does not.</exception>
    private static void CheckChain(Chain chain)
    {
        if (!Enum.IsDefined(chain) || chain.Model() != LedgerModel.Utxo)
        {
            throw new ArgumentOutOfRangeException(nameof(chain), chain, "A txid is on a chain of unspent outputs.");
        }
    }
}
