using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// A transaction an expansion of an address held back (sketch EX1): read from the address's history and sealed in the
/// case like every other response, but not in the graph until the user picks it out. Everything on it is a fact from
/// the chain, seen from the address.
/// </summary>
/// <param name="Txid">The transaction.</param>
/// <param name="Block">The block it was confirmed in, or <see langword="null"/> while unconfirmed.</param>
/// <param name="Side">
/// <see cref="PileSide.Out"/> when the address pays in it, <see cref="PileSide.In"/> when it only receives.
/// </param>
/// <param name="Amount">
/// What moved: on the out side, what the transaction paid to other addresses; on the in side, what it paid the address.
/// </param>
/// <param name="Counterparty">
/// The other address that took or gave the most, or <see langword="null"/> when there is none with an address (a
/// coinbase, or only scripts without one).
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
}

/// <summary>What an address's expansions held back, read from its history (sketch EX1).</summary>
/// <param name="Address">The address.</param>
/// <param name="Transactions">The transactions held back, newest first.</param>
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
}
