using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// A transaction an expansion of an address held back (sketch EX1): read from the address's history and sealed in the
/// case like every other response, but not in the graph until the user picks it out. What it says is read from the
/// chain, seen from the address; the amount is a fact unless <see cref="IsShared"/> says that it is the address's
/// share, worked out (principle 2).
/// </summary>
/// <param name="Txid">The transaction.</param>
/// <param name="Block">The block it was confirmed in, or <see langword="null"/> while unconfirmed.</param>
/// <param name="Side">
/// <see cref="PileSide.Out"/> when the address pays in it, <see cref="PileSide.In"/> when it only receives (or, in a
/// transaction others paid into too, got more back than it put in).
/// </param>
/// <param name="Amount">
/// What moved: on the out side, what the transaction paid to other addresses (the address's share of it when others
/// paid in too); on the in side, what it paid the address; for a transfer to itself, what came back to it.
/// </param>
/// <param name="Counterparty">
/// The other address that took or gave the most, or <see langword="null"/> when there is none with an address (a
/// coinbase, a transfer to itself, or only scripts without one).
/// </param>
/// <param name="Counterparties">How many other addresses took part on the transaction's other side.</param>
public sealed record HeldBackTransaction(
    Txid Txid,
    BlockReference? Block,
    PileSide Side,
    Satoshi Amount,
    BitcoinAddress? Counterparty,
    int Counterparties)
{
    /// <summary>Gets when the transaction was confirmed, or <see langword="null"/> while unconfirmed.</summary>
    public DateTimeOffset? Time => Block?.Time;

    /// <summary>
    /// Gets the address whose history the transaction was read from, or <see langword="null"/> when it is not known
    /// (then it is the clue's own address).
    /// </summary>
    public BitcoinAddress? Address { get; init; }

    /// <summary>
    /// Gets every other address on the transaction's other side, the one that took or gave the most first, so that a
    /// negative label on any of them is seen, not only on the largest.
    /// </summary>
    public IReadOnlyList<BitcoinAddress> Parties { get; init; } = [];

    /// <summary>
    /// Gets whether other addresses paid into the transaction too (a CoinJoin, a batch): then <see cref="Amount"/> is
    /// the address's share, worked out from what it put in and got back, a judgement and not a fact, which the view
    /// draws as uncertain (principle 2).
    /// </summary>
    public bool IsShared { get; init; }

    /// <summary>
    /// Gets whether the address only paid itself (a consolidation): nothing left it, and <see cref="Amount"/> is what
    /// came back to it.
    /// </summary>
    public bool IsSelf { get; init; }

    /// <summary>Gets the transaction's fee as the chain has it; zero for a coinbase.</summary>
    public Satoshi Fee { get; init; }
}

/// <summary>What an address's expansions held back, read from its history (sketch EX1).</summary>
/// <param name="Address">The address.</param>
/// <param name="Transactions">The transactions held back, newest first (the unconfirmed first of all).</param>
/// <param name="IsComplete">
/// Whether the whole history was read; <see langword="false"/> when a lookup failed or the history is longer than
/// <see cref="Most"/>, which <paramref name="Problem"/> says.
/// </param>
/// <param name="Problem">What the user should know, in Swedish, or <see langword="null"/>.</param>
public sealed record HeldBackList(
    BitcoinAddress Address,
    IReadOnlyList<HeldBackTransaction> Transactions,
    bool IsComplete,
    string? Problem)
{
    /// <summary>
    /// The most transactions of an address's history read to find what was held back: a few dozen lookups at the
    /// source's pace, enough for the addresses an investigation follows; an exchange's hot wallet with millions is
    /// never read whole.
    /// </summary>
    public const int Most = 2_000;

    /// <summary>
    /// The most transactions read back from near the traced arrival when the newest <see cref="Most"/> do not reach
    /// it, so that the time round the arrival, which the view shows first, is not empty on a busy address.
    /// </summary>
    public const int MostAroundArrival = 500;

    /// <summary>How far round the traced arrival the history is read: the view's default window, ±48 hours.</summary>
    public static readonly TimeSpan AroundArrival = TimeSpan.FromHours(48);
}
