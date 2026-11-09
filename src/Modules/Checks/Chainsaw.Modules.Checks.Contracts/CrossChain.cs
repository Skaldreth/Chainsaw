using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks.Contracts;

/// <summary>
/// One movement of money on any chain as the chain recorded it (principle 2): the transaction, who paid, who received,
/// the amount in its asset and the block, so that every step can be checked against the chain (principle 1). On a
/// UTXO chain it is a coin (<see cref="Coin"/>), on an account chain a movement (<see cref="Movement"/>).
/// </summary>
/// <param name="Transaction">The transaction it moved in.</param>
/// <param name="From">
/// Who paid: on an account chain the sender, on Bitcoin the first input's address; <see langword="null"/> when the
/// chain names none (a coinbase, or a script without an address).
/// </param>
/// <param name="To">Who received it.</param>
/// <param name="Amount">The amount, in its asset.</param>
/// <param name="Height">The block's height, or <see langword="null"/> while unconfirmed.</param>
/// <param name="BlockHash">The block's hash, or <see langword="null"/> while unconfirmed or not given.</param>
/// <param name="Time">The block's time, or <see langword="null"/> while unconfirmed.</param>
public sealed record ChainMovement(
    ChainTransactionRef Transaction,
    ChainAddress? From,
    ChainAddress To,
    AssetAmount Amount,
    long? Height,
    string? BlockHash,
    DateTimeOffset? Time)
{
    /// <summary>Gets the coin on a UTXO chain (the output it is), or <see langword="null"/>.</summary>
    public Outpoint? Coin { get; init; }

    /// <summary>Gets the movement on an account chain, or <see langword="null"/>.</summary>
    public AccountSeed? Movement { get; init; }

    /// <summary>
    /// Gets the movement's stable key, the same whatever was read with it: the chain, the transaction and the output
    /// (<c>Bitcoin:txid:1</c>) or the movement's kind and position (<c>Tron:token:hash#3</c>).
    /// </summary>
    public string Key => Coin is { } coin
        ? string.Create(CultureInfo.InvariantCulture, $"{Transaction.Chain}:{coin.Txid.Value}:{coin.Index}")
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{Transaction.Chain}:{Movement?.Kind.ToString().ToLowerInvariant()}:{Transaction.Id}#{Movement?.Position}");
}

/// <summary>
/// What the cross-chain search (<see cref="ICrossChainLookup"/>) has of the other chains for the money that reached a
/// bridge or a swap: the payments it read into the case, and what it could not read and why, so that the suggestion
/// says honestly what it rests on (principles 3 and 9).
/// </summary>
/// <param name="Arrivals">The payments read from the case that the graph does not hold, in a stable order.</param>
/// <param name="Targets">The addresses the search reads, each with whether it has them.</param>
public sealed record CrossChainArrivals(IReadOnlyList<ChainMovement> Arrivals, IReadOnlyList<CrossChainTarget> Targets)
{
    /// <summary>Gets nothing read.</summary>
    public static CrossChainArrivals Empty { get; } = new([], []);

    /// <summary>
    /// Gets what each payment that the analysis weighs was worth at its block's time, by the case's rates, in
    /// <see cref="Currency"/>, by the payment's key (<see cref="ChainMovement.Key"/>); a payment the case has no rate
    /// for is missing, and its amount cannot be compared with another asset's.
    /// </summary>
    public IReadOnlyDictionary<string, decimal> Values { get; init; } = new Dictionary<string, decimal>();

    /// <summary>Gets the currency of <see cref="Values"/>, for example <c>SEK</c>, or <see langword="null"/>.</summary>
    public string? Currency { get; init; }

    /// <summary>Gives what a payment was worth, when the case has its rate.</summary>
    /// <param name="movement">The payment.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public decimal? ValueOf(ChainMovement movement)
    {
        ArgumentNullException.ThrowIfNull(movement);
        return Values.TryGetValue(movement.Key, out var value) ? value : null;
    }
}

/// <summary>Whether the search has an address's history on the other chain.</summary>
public enum CrossChainTargetState
{
    /// <summary>The case has it (fetched now or before).</summary>
    Read,

    /// <summary>It has not been fetched; a run that may ask through the exit fetches it.</summary>
    NotFetched,

    /// <summary>It could not be fetched now: no exit, the chain is off, no source, or the source failed.</summary>
    Unreachable,
}

/// <summary>
/// An address on another chain that the cross-chain search reads, because what is on the chain names it: the
/// recipient a swap's memo gives (the bridge's own event).
/// </summary>
/// <param name="Address">The address, with its chain.</param>
/// <param name="State">Whether its history is read.</param>
/// <param name="Problem">Why it could not be read, in Swedish, or <see langword="null"/>.</param>
public sealed record CrossChainTarget(ChainAddress Address, CrossChainTargetState State, string? Problem);

/// <summary>
/// Reads, and when asked fetches through the exit, what the cross-chain analysis needs of the other chains: the
/// history of the recipients that the chain itself names (a swap's memo), and the case's rates for the amounts. Only
/// the address is sent, never the case (principle 4); a chain that is off, a missing exit or source is said, never
/// hidden (principle 9).
/// </summary>
public interface ICrossChainLookup
{
    /// <summary>Finds the payments on the other chains that the graph does not hold.</summary>
    /// <param name="case">The open case the answers are sealed in.</param>
    /// <param name="graph">The graph.</param>
    /// <param name="mode">
    /// <see cref="LookupMode.CaseOnly"/> to read only what the case has, sending nothing;
    /// <see cref="LookupMode.AsNeeded"/> to fetch what is missing through the exit (and the rates the amounts need).
    /// </param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>What was found and what could not be read.</returns>
    Task<CrossChainArrivals> FindAsync(
        ICase @case, TraceGraph graph, LookupMode mode, CancellationToken cancellationToken = default);
}
