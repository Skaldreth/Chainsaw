using System.Text.RegularExpressions;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// A transaction on another chain, named by the chain and its id as that chain writes it. Chainsaw cannot fetch it
/// yet; it is what the user links by hand.
/// </summary>
public sealed partial record ChainTransactionRef
{
    /// <summary>Creates a reference after checking the id against the chain's format.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="id">The id; letters are kept in lower case.</param>
    /// <exception cref="ArgumentException">The id does not have the chain's format.</exception>
    public ChainTransactionRef(Chain chain, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var normalised = id.Trim().ToLowerInvariant();
        var valid = chain switch
        {
            Chain.Bitcoin or Chain.Tron => PlainHash().IsMatch(normalised),
            Chain.Ethereum => PrefixedHash().IsMatch(normalised),
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException("The id does not have the chain's format.", nameof(id));
        }

        (Chain, Id) = (chain, normalised);
    }

    /// <summary>Gets the chain.</summary>
    public Chain Chain { get; }

    /// <summary>Gets the id, in lower case.</summary>
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
/// The user's link from a Bitcoin coin (the payment into a bridge or a swap) to the transaction where the money
/// continues on another chain. It is the user's judgement, sealed in the case with who made it and why.
/// </summary>
/// <param name="Sequence">The record's number in the sealed chain <c>tracing.cross_chain</c>.</param>
/// <param name="From">The Bitcoin coin that went into the bridge or swap.</param>
/// <param name="To">The transaction on the other chain.</param>
/// <param name="Reason">Why the user links them, in their words.</param>
/// <param name="JournalSequence">The journal entry that records who linked them and when.</param>
/// <param name="RecordedUtc">When the link was made.</param>
public sealed record CrossChainLink(
    long Sequence,
    Outpoint From,
    ChainTransactionRef To,
    string Reason,
    long JournalSequence,
    DateTimeOffset RecordedUtc);
