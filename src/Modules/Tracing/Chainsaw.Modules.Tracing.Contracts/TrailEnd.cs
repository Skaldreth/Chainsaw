using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// Why the traced money stops where it does. An end is never silent (CLAUDE.md, <em>Spåret tar slut</em>): every
/// traced satoshi ends in exactly one of these, and the reason says whether the trail really ends or only is not
/// followed further yet.
/// </summary>
public enum TrailEndReason
{
    /// <summary>
    /// Not an end: the next step is not in the graph yet (the coin's spending is not known or its transaction is
    /// not fetched). Expanding the graph, or a lookup when the exit is connected, takes the trace further.
    /// </summary>
    NotExpanded,

    /// <summary>
    /// Not an end: the coin was unspent when the case last asked. The money is still there, which matters before a
    /// seizure; it may have moved since.
    /// </summary>
    Unspent,

    /// <summary>The money went to the miner as the transaction's fee.</summary>
    MinerFee,

    /// <summary>
    /// The coin can never be spent (for example an OP_RETURN output with an amount): the money is gone.
    /// </summary>
    Unspendable,

    /// <summary>A CoinJoin: many owners pay in together, so whose output is whose cannot be read.</summary>
    CoinJoin,

    /// <summary>A mixer: a service that mixes customers' money and pays it out from other coins.</summary>
    Mixer,

    /// <summary>The money went to a privacy coin (Monero, Zcash, Litecoin MWEB), where it cannot be followed.</summary>
    PrivacyCoin,

    /// <summary>
    /// A bridge or a swap to another chain; the trace continues there when the user has linked the other chain's
    /// transaction (<see cref="CrossChainLink"/>).
    /// </summary>
    OtherChain,

    /// <summary>An exchange: the withdrawal point is found, and a request for information can be made.</summary>
    Exchange,

    /// <summary>A service that is not known: the money goes on inside it in ways the chain does not show.</summary>
    UnknownService,
}

/// <summary>Reads what a <see cref="TrailEndReason"/> means for the trace.</summary>
public static class TrailEndReasons
{
    /// <summary>
    /// Says whether the trail really ends: <see langword="false"/> for <see cref="TrailEndReason.NotExpanded"/> and
    /// <see cref="TrailEndReason.Unspent"/>, where the money can still be followed.
    /// </summary>
    /// <param name="reason">The reason.</param>
    /// <returns>Whether the trail ends.</returns>
    public static bool EndsTrail(this TrailEndReason reason) =>
        reason is not (TrailEndReason.NotExpanded or TrailEndReason.Unspent);

    /// <summary>Gives the reason as the user reads it, in Swedish.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>A short text.</returns>
    public static string Describe(this TrailEndReason reason) => reason switch
    {
        TrailEndReason.NotExpanded => "Inte expanderad ännu",
        TrailEndReason.Unspent => "Ligger kvar (ospenderad)",
        TrailEndReason.MinerFee => "Avgift till minerare",
        TrailEndReason.Unspendable => "Kan aldrig spenderas",
        TrailEndReason.CoinJoin => "CoinJoin",
        TrailEndReason.Mixer => "Mixer",
        TrailEndReason.PrivacyCoin => "Privacy coin",
        TrailEndReason.OtherChain => "Fortsätter på annan kedja",
        TrailEndReason.Exchange => "Börs, uttag funnet",
        TrailEndReason.UnknownService => "Okänd tjänst",
        _ => reason.ToString(),
    };
}

/// <summary>
/// That the trail ends at an address or a transaction, and why: a conclusion from an analysis or a label, never a
/// fact (principle 2).
/// </summary>
/// <param name="Reason">Why the trail ends; never <see cref="TrailEndReason.NotExpanded"/> or
/// <see cref="TrailEndReason.Unspent"/>, which the trace itself decides.</param>
/// <param name="Source">What says so, with its version, for example <c>coinjoin v1</c> or a label's source.</param>
/// <param name="Explanation">Why, in plain Swedish.</param>
public sealed record TrailEndHint(TrailEndReason Reason, string Source, string Explanation)
{
    /// <summary>Gets why the trail ends.</summary>
    public TrailEndReason Reason { get; } = Reason is TrailEndReason.NotExpanded or TrailEndReason.Unspent
        || !Enum.IsDefined(Reason)
        ? throw new ArgumentOutOfRangeException(nameof(Reason), "A hint names a real end of the trail.")
        : Reason;

    /// <summary>Gets what says so.</summary>
    public string Source { get; } = string.IsNullOrWhiteSpace(Source)
        ? throw new ArgumentException("A hint says where it comes from.", nameof(Source))
        : Source;

    /// <summary>Gets why, in plain Swedish.</summary>
    public string Explanation { get; } = string.IsNullOrWhiteSpace(Explanation)
        ? throw new ArgumentException("A hint says why.", nameof(Explanation))
        : Explanation;
}

/// <summary>
/// Recognises where the trail ends: CoinJoin from Clustering, and later exchanges, mixers, bridges and sanctioned
/// addresses from Attribution. Every registered classifier is asked, so that a new source of knowledge is added by
/// registering one more, without anything else changing (principle 8).
/// </summary>
/// <remarks>The methods run off the UI thread and may read the case; they must not write to it.</remarks>
public interface ITrailEndClassifier
{
    /// <summary>Says whether money that reaches an address has reached the end of the trail.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address.</param>
    /// <returns>The hints; empty when the classifier knows nothing.</returns>
    IReadOnlyList<TrailEndHint> ClassifyAddress(ICase @case, BitcoinAddress address);

    /// <summary>Says whether money that is spent into a transaction has reached the end of the trail.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The hints; empty when the classifier knows nothing.</returns>
    IReadOnlyList<TrailEndHint> ClassifyTransaction(ICase @case, BitcoinTransaction transaction);
}
