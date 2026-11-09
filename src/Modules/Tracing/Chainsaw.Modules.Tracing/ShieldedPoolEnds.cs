using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// Ends the trail where money goes into one of Zcash's shielded pools (t→z) and marks where money comes out of one
/// (z→t): the amounts in and out are facts on the chain (<see cref="ShieldedFlow"/>), but who holds what inside the
/// pool cannot be read, so the trail ends with the reason <see cref="TrailEndReason.PrivacyCoin"/>. Which outputs of
/// the pool may belong to a payment into it is a proposal of its own (Checks, <c>shielded-pool v1</c>), never drawn
/// here.
/// </summary>
internal static class ShieldedPoolEnds
{
    /// <summary>The analysis's name and version, as a hint names its source.</summary>
    public const string Source = "zcash-shielded v1";

    /// <summary>
    /// Gives the hints of a transaction that touches a shielded pool, for its node: one for what goes in and one for
    /// what comes out, each with the amount the chain states.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The hints; empty when it touches no pool.</returns>
    public static IReadOnlyList<TrailEndHint> Hints(BitcoinTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.Shielded is not { } flow)
        {
            return [];
        }

        var hints = new List<TrailEndHint>();
        if (flow.IntoPool.Value > 0)
        {
            hints.Add(new TrailEndHint(TrailEndReason.PrivacyCoin, Source, IntoText(transaction, flow)));
        }

        if (flow.OutOfPool.Value > 0)
        {
            hints.Add(new TrailEndHint(TrailEndReason.PrivacyCoin, Source, OutOfText(transaction, flow)));
        }

        return hints;
    }

    /// <summary>Gives the end of traced money that went into a shielded pool in a transaction.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="amount">The traced amount that went in.</param>
    /// <param name="mayBeOutdated">Whether a step on the way may change.</param>
    /// <returns>The end.</returns>
    public static TraceEnd TraceEnd(BitcoinTransaction transaction, TracedAmount amount, bool mayBeOutdated)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var flow = transaction.Shielded ?? throw new ArgumentException(
            "The transaction touches no shielded pool.", nameof(transaction));
        var hint = new TrailEndHint(TrailEndReason.PrivacyCoin, Source, IntoText(transaction, flow));
        return new TraceEnd(
            TrailEndReason.PrivacyCoin,
            amount,
            null,
            transaction.Txid,
            null,
            hint.Explanation,
            [hint],
            [],
            mayBeOutdated);
    }

    /// <summary>Says what went into the pool, from the chain's own amounts.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="flow">Its flow.</param>
    /// <returns>The text.</returns>
    private static string IntoText(BitcoinTransaction transaction, ShieldedFlow flow) => string.Format(
        CultureInfo.InvariantCulture,
        "{0} ZEC gick in i Zcash skyddade pool ({1}) i tx {2}, enligt kedjans egna belopp (value balance). I poolen "
            + "syns varken belopp eller ägare, så spåret tar slut här.",
        Coins(flow.IntoPool),
        Names(flow),
        transaction.Txid.Value);

    /// <summary>Says what came out of the pool, from the chain's own amounts.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="flow">Its flow.</param>
    /// <returns>The text.</returns>
    private static string OutOfText(BitcoinTransaction transaction, ShieldedFlow flow) => string.Format(
        CultureInfo.InvariantCulture,
        "{0} ZEC kom ut ur Zcash skyddade pool ({1}) i tx {2}, enligt kedjans egna belopp (value balance). Varifrån "
            + "i poolen pengarna kom syns inte, så spåret kan inte följas bakåt härifrån.",
        Coins(flow.OutOfPool),
        Names(flow),
        transaction.Txid.Value);

    /// <summary>Writes an amount in whole coins with eight decimals.</summary>
    /// <param name="amount">The amount in zatoshi.</param>
    /// <returns>For example <c>1.25000000</c>.</returns>
    private static string Coins(Satoshi amount) => amount.ToString();

    /// <summary>Names the pools a flow touches.</summary>
    /// <param name="flow">The flow.</param>
    /// <returns>For example <c>Sapling</c> or <c>Sapling, Orchard</c>.</returns>
    private static string Names(ShieldedFlow flow) =>
        flow.Pools.Count == 0 ? "okänd pool" : string.Join(", ", flow.Pools);
}
