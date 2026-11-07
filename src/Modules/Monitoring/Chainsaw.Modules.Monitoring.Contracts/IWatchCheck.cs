using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring.Contracts;

/// <summary>The block a transaction is in, as the source gave it (principle 1: height, hash and time).</summary>
/// <param name="Height">The block's height.</param>
/// <param name="Hash">The block's hash as the chain writes it (on Ethereum with <c>0x</c>).</param>
/// <param name="Time">The block's time in UTC.</param>
public sealed record WatchBlock(long Height, string Hash, DateTimeOffset Time);

/// <summary>One side of a movement: an address, when the script has one, and the amount.</summary>
/// <param name="Address">The address, or <see langword="null"/> (an OP_RETURN, a coinbase).</param>
/// <param name="Amount">The amount.</param>
public sealed record WatchLeg(ChainAddress? Address, AssetAmount Amount);

/// <summary>
/// One movement of value in a transaction, in the form every chain fits: Bitcoin's whole transaction (its inputs and
/// outputs), or one Ethereum transfer (one sender and one receiver).
/// </summary>
/// <param name="From">Where the value left.</param>
/// <param name="To">Where it went.</param>
public sealed record WatchFlow(IReadOnlyList<WatchLeg> From, IReadOnlyList<WatchLeg> To);

/// <summary>
/// A transaction as a check reads it, without the case: the facts a watch is matched against, from whichever source
/// fed them (the case's sources now, the background process later).
/// </summary>
/// <param name="Chain">The chain.</param>
/// <param name="Id">The txid or transaction hash.</param>
/// <param name="Block">The block, or <see langword="null"/> while it is unconfirmed.</param>
/// <param name="Flows">Its movements of value.</param>
public sealed record WatchTransaction(Chain Chain, string Id, WatchBlock? Block, IReadOnlyList<WatchFlow> Flows);

/// <summary>
/// Money that moved the way a watch looks for: a fact from the chain (principle 2) that the watch picked out; what it
/// means is the user's judgement.
/// </summary>
/// <param name="Transaction">The txid or transaction hash.</param>
/// <param name="Block">The block it is in.</param>
/// <param name="Direction">In or out (never both).</param>
/// <param name="Amount">How much moved that way, in one asset.</param>
/// <param name="Counterparties">The other side: the receivers of money out, the senders of money in.</param>
public sealed record WatchMatch(
    string Transaction,
    WatchBlock Block,
    WatchDirection Direction,
    AssetAmount Amount,
    IReadOnlyList<ChainAddress> Counterparties);

/// <summary>How a feed answered for one address.</summary>
public enum WatchFeedStatus
{
    /// <summary>The source was asked and answered; the transactions are fresh.</summary>
    Read,

    /// <summary>
    /// The source answered, but not everything back to where the check was could be read (too many pages, a history
    /// the source has not finished, a block without its hash); what was read counts, and the next check reads from the
    /// same place again.
    /// </summary>
    Partial,

    /// <summary>
    /// The source could not be asked (it was offline, or only the case answered); nothing new is known. A lookup with no
    /// endpoint in step is <see cref="Failed"/>, with its problem saying so.
    /// </summary>
    NotAsked,

    /// <summary>The source was asked and failed.</summary>
    Failed,
}

/// <summary>What a feed read about one address.</summary>
/// <param name="Status">How it answered.</param>
/// <param name="Transactions">The transactions read, newest first; empty unless read.</param>
/// <param name="Origins">The sealed raw responses it rests on, when the feed seals them; empty otherwise.</param>
/// <param name="IsCutShort">Whether it stopped before reaching back to where it was asked to (too many pages).</param>
/// <param name="Problem">What went wrong or was left out, in Swedish, or <see langword="null"/>.</param>
public sealed record WatchFeedPage(
    WatchFeedStatus Status,
    IReadOnlyList<WatchTransaction> Transactions,
    IReadOnlyList<long> Origins,
    bool IsCutShort,
    string? Problem)
{
    /// <summary>
    /// Gets, for a partial read, the block above which everything was read: blocks below it, down to where the check
    /// was asked to read, were not. <see langword="null"/> when nothing reliable was read, so the check stays where it
    /// was; for a full read it is not used.
    /// </summary>
    public long? CompleteAbove { get; init; }
}

/// <summary>
/// Where a check reads an address's newest transactions. The case's feed asks the chain's source with
/// <c>LookupMode.InStep</c> and seals every response; the background process (stage 27) brings its own.
/// </summary>
public interface IWatchFeed
{
    /// <summary>
    /// Reads an address's transactions, newest first, back to a block: at least those above
    /// <paramref name="downTo"/>, as far as <see cref="WatchRules.MaximumPages"/> pages reach.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="asset">
    /// The watched asset, so that a feed may leave out the rest; <see langword="null"/> for all.
    /// </param>
    /// <param name="downTo">The block below which nothing is needed.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    /// <returns>What was read.</returns>
    Task<WatchFeedPage> ReadAsync(
        ChainAddress address, Asset? asset, long downTo, CancellationToken cancellationToken = default);
}

/// <summary>What one check of a watch found.</summary>
/// <param name="Matches">The money that moved the way the watch looks for, above the given block; oldest first.</param>
/// <param name="Status">
/// <see cref="WatchFeedStatus.Read"/> when every address was read; otherwise the worst status of them.
/// </param>
/// <param name="Origins">The sealed raw responses the check rests on.</param>
/// <param name="Highest">The highest block seen in what was read, or <see langword="null"/>.</param>
/// <param name="Problems">What went wrong or was left out, in Swedish; empty when nothing.</param>
public sealed record WatchCheckResult(
    IReadOnlyList<WatchMatch> Matches,
    WatchFeedStatus Status,
    IReadOnlyList<long> Origins,
    long? Highest,
    IReadOnlyList<string> Problems)
{
    /// <summary>
    /// Gets, for a partial check, the block above which every address was read completely (the highest of the
    /// addresses' <see cref="WatchFeedPage.CompleteAbove"/>), or <see langword="null"/> when that is not known. Since
    /// the highest counts, a feed that holds a check back below what it read (a block without its hash) holds it only
    /// where one address is read; a watch with several addresses on such a chain needs the holds kept apart.
    /// </summary>
    public long? CompleteAbove { get; init; }
}

/// <summary>
/// The check of a watch, without the case (only addresses and a filter in, matches out), so that the background
/// process of stage 27 runs the very same check as the open Chainsaw.
/// </summary>
public interface IWatchCheck
{
    /// <summary>Picks out the money in a transaction that moved the way a watch looks for.</summary>
    /// <param name="addresses">The watched addresses: one, or a cluster's.</param>
    /// <param name="filter">What the watch looks for.</param>
    /// <param name="transaction">The transaction; an unconfirmed one gives nothing.</param>
    /// <returns>At most one match per direction and asset.</returns>
    IReadOnlyList<WatchMatch> Match(
        IReadOnlySet<ChainAddress> addresses, WatchFilter filter, WatchTransaction transaction);

    /// <summary>
    /// Reads each watched address through the feed and gives the matches in blocks above
    /// <paramref name="after"/>, each transaction once.
    /// </summary>
    /// <param name="addresses">The watched addresses.</param>
    /// <param name="filter">What the watch looks for.</param>
    /// <param name="feed">Where the transactions are read.</param>
    /// <param name="after">The block above which money counts (the watch's start).</param>
    /// <param name="downTo">
    /// How far back the feed reads (the last check, less <see cref="WatchRules.Lookback"/>).
    /// </param>
    /// <param name="cancellationToken">Stops the check.</param>
    /// <returns>What the check found.</returns>
    Task<WatchCheckResult> CheckAsync(
        IReadOnlyCollection<ChainAddress> addresses,
        WatchFilter filter,
        IWatchFeed feed,
        long after,
        long downTo,
        CancellationToken cancellationToken = default);
}
