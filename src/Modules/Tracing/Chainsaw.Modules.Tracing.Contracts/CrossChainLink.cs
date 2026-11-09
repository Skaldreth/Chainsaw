using System.Text.RegularExpressions;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// A transaction on any chain, named by the chain and its id as that chain writes it: the transaction of a transfer
/// in the graph, or one on another chain that the user links by hand (Chainsaw cannot fetch those yet).
/// </summary>
public sealed partial record ChainTransactionRef
{
    /// <summary>Creates a reference after checking the id against the chain's format.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="id">
    /// The id; letters are kept in lower case, except on Solana, whose signature is Base58 and kept as written.
    /// </param>
    /// <exception cref="ArgumentException">The id does not have the chain's format.</exception>
    public ChainTransactionRef(Chain chain, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var normalised = chain == Chain.Solana ? id.Trim() : id.Trim().ToLowerInvariant();
        var valid = chain switch
        {
            Chain.Bitcoin or Chain.Tron or Chain.Litecoin => PlainHash().IsMatch(normalised),
            Chain.Ethereum or Chain.Bsc => PrefixedHash().IsMatch(normalised),
            Chain.Solana => EthereumTxHash.TryParse(Chain.Solana, normalised, out _),

            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException("The id does not have the chain's format.", nameof(id));
        }

        (Chain, Id) = (chain, normalised);
    }

    /// <summary>Gives the reference of a Bitcoin transaction.</summary>
    /// <param name="txid">The transaction's id.</param>
    /// <returns>The reference.</returns>
    public static ChainTransactionRef Of(Txid txid)
    {
        ArgumentNullException.ThrowIfNull(txid);
        return new ChainTransactionRef(Chain.Bitcoin, txid.Value);
    }

    /// <summary>Gets the chain.</summary>
    public Chain Chain { get; }

    /// <summary>Gets the id, in lower case (on Solana as written).</summary>
    public string Id { get; }

    /// <summary>Writes the chain and the id, for example <c>Ethereum:0x…</c>.</summary>
    /// <returns>The chain, a colon and the id.</returns>
    public override string ToString() => Chain + ":" + Id;

    /// <summary>A hash in 64 hex digits, as Bitcoin and TRON write transaction ids.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex PlainHash();

    /// <summary>A hash in 64 hex digits after <c>0x</c>, as Ethereum writes transaction hashes.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex("^0x[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixedHash();
}

/// <summary>
/// The user's link from a Bitcoin coin or an Ethereum movement (the payment into a bridge or a swap) to the
/// transaction where the money continues on another chain. It is the user's judgement, sealed in the case with who
/// made it and why. Exactly one of <see cref="From"/> and <see cref="FromTransfer"/> is set.
/// </summary>
/// <param name="Sequence">The record's number in the sealed chain <c>tracing.cross_chain</c>.</param>
/// <param name="From">The Bitcoin coin that went into the bridge or swap, or <see langword="null"/> on Ethereum.</param>
/// <param name="To">The transaction on the other chain.</param>
/// <param name="Reason">Why the user links them, in their words.</param>
/// <param name="JournalSequence">The journal entry that records who linked them and when.</param>
/// <param name="RecordedUtc">When the link was made.</param>
public sealed record CrossChainLink(
    long Sequence,
    Outpoint? From,
    ChainTransactionRef To,
    string Reason,
    long JournalSequence,
    DateTimeOffset RecordedUtc)
{
    /// <summary>
    /// Gets the Ethereum movement that went into the bridge or swap, or <see langword="null"/> for a Bitcoin coin.
    /// </summary>
    public AccountSeed? FromTransfer { get; init; }

    /// <summary>Gets the chain the money left.</summary>
    public Chain FromChain => FromTransfer?.Transaction.Chain ?? Chain.Bitcoin;

    /// <summary>Writes where the money left: the coin, or the movement's kind and transaction.</summary>
    public string FromText => From?.ToString()
        ?? string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{FromTransfer!.Kind.ToString().ToLowerInvariant()}:{FromTransfer.Transaction.Value}"
            + $"#{FromTransfer.Position}");
}
