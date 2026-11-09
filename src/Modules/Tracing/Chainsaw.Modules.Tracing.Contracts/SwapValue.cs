using System.Globalization;
using System.Numerics;
using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// The rule for traced money through a swap (the user's decision 2026-11-08, sketch SW1a), written once so that the
/// graph's trace, the exposure and everything built on them follow the same rule: the traced <em>value</em> follows,
/// as an investigator thinks. The traced share of what went into the swap becomes the same share of what came out, in
/// the new asset. It is a judgement, the analysis <c>swap-value v1</c>, never a fact (principle 2): the chain shows
/// that the account sent one asset and got another, not that the one paid for the other. The user can choose per swap
/// that the value does not follow (<see cref="SwapStop"/>); the trail then ends in the swap.
/// </summary>
public static class SwapValue
{
    /// <summary>The analysis's name, as <em>Varför?</em> writes it.</summary>
    public const string AnalysisName = "swap-value";

    /// <summary>The analysis's version; raised whenever it may give other amounts.</summary>
    public const int AnalysisVersion = 1;

    /// <summary>Gets the analysis's name and version, <c>swap-value v1</c>.</summary>
    public static string Analysis { get; } =
        AnalysisName + " v" + AnalysisVersion.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The most the values of what went in and what came out may differ, as a share of the larger, for the value to
    /// follow. An investigator would never follow an absurd price: anyone can open a pool (Uniswap v4, Curve, Balancer
    /// or an AMM of one's own) and swap a real token at any price there, to move the traced value into a dust amount or
    /// to blow a dust amount up. A fifth leaves room for fees, slippage and the hour a rate is for.
    /// </summary>
    public const decimal MostPriceGap = 0.20m;

    /// <summary>
    /// Tests a swap's price (the check <c>swap-price v1</c>): the value of what went in against the value of what came
    /// out, at the swap's time, by the rates the case has. Without a rate for both, or without the swap's time, the
    /// price is not tested, and the check says so.
    /// </summary>
    /// <param name="swap">The swap.</param>
    /// <param name="pricing">The case's rates, or <see langword="null"/> when there are none.</param>
    /// <returns>The check.</returns>
    public static SwapPriceCheck CheckPrice(TokenSwap swap, SwapPricing? pricing)
    {
        ArgumentNullException.ThrowIfNull(swap);
        if (pricing is null || swap.Time is not { } time
            || pricing.ValueAt(swap.Sold, time) is not { } sold || pricing.ValueAt(swap.Bought, time) is not { } bought)
        {
            return new SwapPriceCheck(swap, SwapPriceVerdict.NotTested, null, null, pricing?.Currency);
        }

        var larger = Math.Max(sold, bought);
        var gap = larger <= 0 ? 0 : Math.Abs(sold - bought) / larger;
        var verdict = gap > MostPriceGap ? SwapPriceVerdict.Unreasonable : SwapPriceVerdict.Reasonable;
        return new SwapPriceCheck(swap, verdict, sold, bought, pricing.Currency);
    }

    /// <summary>Gives the user's choice in force that the value does not follow through a swap.</summary>
    /// <param name="swap">The swap.</param>
    /// <param name="stops">The choices in force.</param>
    /// <returns>The choice, or <see langword="null"/> when the value follows (the default).</returns>
    public static SwapStop? StopOf(TokenSwap swap, IEnumerable<SwapStop> stops)
    {
        ArgumentNullException.ThrowIfNull(swap);
        ArgumentNullException.ThrowIfNull(stops);
        return stops.FirstOrDefault(stop => stop.Is(swap));
    }

    /// <summary>
    /// Says whether a transfer's money goes into a swap, the one rule the trace and the exposure share: it is part of
    /// what the account sent into the swap (<see cref="TokenSwap.SoldMovements"/>), and the user has not linked it to
    /// another chain, since a link says the money continues there and it is never counted twice.
    /// </summary>
    /// <param name="swap">The swap.</param>
    /// <param name="transfer">The transfer, on one of the graph's lines.</param>
    /// <param name="links">The user's links to other chains in force.</param>
    /// <returns><see langword="true"/> when the money goes into the swap.</returns>
    public static bool GoesIn(TokenSwap swap, AssetTransfer transfer, IEnumerable<CrossChainLink> links)
    {
        ArgumentNullException.ThrowIfNull(swap);
        ArgumentNullException.ThrowIfNull(transfer);
        ArgumentNullException.ThrowIfNull(links);
        return swap.IsSold(transfer) && !links.Any(link => link.FromTransfer?.Is(transfer) == true);
    }

    /// <summary>
    /// Gives how much of one transfer that came out of a swap follows the traced money: the traced share of what went
    /// in, of the transfer's amount, rounded down to whole units so that the rule never creates traced money.
    /// </summary>
    /// <param name="tracedSold">The traced units of what the account sent into the swap.</param>
    /// <param name="sold">All the account sent into the swap.</param>
    /// <param name="bought">The units of one transfer that came out of it.</param>
    /// <returns>The traced units of that transfer, at most its amount.</returns>
    public static BigInteger Follow(BigInteger tracedSold, AssetAmount sold, BigInteger bought)
    {
        if (sold.Units.Sign <= 0 || tracedSold.Sign <= 0 || bought.Sign <= 0)
        {
            return BigInteger.Zero;
        }

        return bought * BigInteger.Min(tracedSold, sold.Units) / sold.Units;
    }

    /// <summary>Gives a share as a percentage, as <em>Varför?</em> writes it, for example <c>62,2 %</c>.</summary>
    /// <param name="part">The part.</param>
    /// <param name="whole">The whole.</param>
    /// <returns>The percentage with one decimal and a decimal comma.</returns>
    public static string Percent(BigInteger part, BigInteger whole)
    {
        if (whole.Sign <= 0)
        {
            return "0,0 %";
        }

        var tenths = (double)(part * 1000 / whole) / 10;
        return tenths.ToString("0.0", CultureInfo.GetCultureInfo("sv-SE")) + " %";
    }
}

/// <summary>
/// The user's choice that the traced value does not follow through a swap (sketch SW1a, <em>Följer inte</em>): the
/// trail ends in the swap. It is a judgement of the user's own, sealed in the case with who and when and an optional
/// reason (principle 1), and can be taken back like the user's other judgements (sketch AN1b); the value then follows
/// again, which is the default.
/// </summary>
/// <param name="Sequence">The record's number in the sealed chain <c>tracing.swap_stops</c>.</param>
/// <param name="Transaction">The swap's transaction.</param>
/// <param name="Account">The account that swapped.</param>
/// <param name="Reason">Why, or <see langword="null"/>; the reason is optional.</param>
/// <param name="JournalSequence">The journal entry that records who chose it and when.</param>
/// <param name="RecordedUtc">When it was chosen.</param>
public sealed record SwapStop(
    long Sequence,
    EthereumTxHash Transaction,
    ChainAddress Account,
    string? Reason,
    long JournalSequence,
    DateTimeOffset RecordedUtc)
{
    /// <summary>Says whether the choice is about a swap.</summary>
    /// <param name="swap">The swap.</param>
    /// <returns><see langword="true"/> when it is the same transaction and account.</returns>
    public bool Is(TokenSwap swap)
    {
        ArgumentNullException.ThrowIfNull(swap);
        return swap.Transaction == Transaction && swap.Account == Account;
    }
}

/// <summary>That a choice not to follow a swap was taken back (sketch AN1b): a sealed record of its own.</summary>
/// <param name="Sequence">The record's number in the sealed chain <c>tracing.swap_stops</c>.</param>
/// <param name="Reason">Why, or <see langword="null"/>; the reason is optional.</param>
/// <param name="JournalSequence">The journal entry: who took it back and when.</param>
/// <param name="RecordedUtc">When it was taken back.</param>
public sealed record SwapStopUndo(long Sequence, string? Reason, long JournalSequence, DateTimeOffset RecordedUtc);

/// <summary>A choice not to follow a swap in the case's history, with its taking back when it has been taken back.</summary>
/// <param name="Stop">The choice.</param>
/// <param name="Undo">Its taking back, or <see langword="null"/> while it is in force.</param>
public sealed record SwapStopHistoryEntry(SwapStop Stop, SwapStopUndo? Undo)
{
    /// <summary>Gets whether the choice is taken back, so that the value follows the swap again.</summary>
    public bool IsTakenBack => Undo is not null;
}

/// <summary>
/// What the rule <see cref="SwapValue"/> did with the traced money at one swap in a trace: how much of what went in
/// was traced, and how much of what came out follows it. Every amount in it is a conclusion by the method and the
/// analysis, never a fact (principle 2).
/// </summary>
/// <param name="Swap">The swap, as the transaction shows it.</param>
/// <param name="TracedSold">How much of what the account sent into the swap was traced, by the method.</param>
/// <param name="TracedBought">
/// How much of what came out follows the traced value; zero when the user chose that it does not follow.
/// </param>
/// <param name="Method">The tracing method that traced what went in.</param>
/// <param name="Stop">The user's choice that the value does not follow, or <see langword="null"/> when it follows.</param>
public sealed record SwapConversion(
    TokenSwap Swap,
    AssetAmount TracedSold,
    AssetAmount TracedBought,
    TracingMethodId Method,
    SwapStop? Stop)
{
    /// <summary>Gets whether the traced value follows through the swap (the default).</summary>
    public bool Follows => Stop is null;

    /// <summary>Gets the traced share of what went in, as <em>Varför?</em> writes it, for example <c>62,2 %</c>.</summary>
    public string ShareText => SwapValue.Percent(TracedSold.Units, Swap.Sold.Units);

    /// <summary>
    /// Gets how much of the traced value that went in stays in the pool, by the price check: the share of what went in
    /// that what came out was not worth (fees and slippage), in the sold asset; zero when nothing is kept or the price
    /// was not tested.
    /// </summary>
    public AssetAmount KeptByPool { get; init; } = AssetAmount.Zero(Swap.Sold.Asset);

    /// <summary>Gets the price check the swap's value followed after, or <see langword="null"/>.</summary>
    public SwapPriceCheck? Price { get; init; }

    /// <summary>
    /// Explains the swap's step in plain Swedish, so that the user can ask <em>why?</em> (principle 3): what went in
    /// and how much of it was traced, the share, what came out and through which exchange, and what follows.
    /// </summary>
    /// <returns>The explanation.</returns>
    public string Explain()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"Swap i tx {Swap.Transaction.Value}: in {Swap.Sold}, varav spårat {TracedSold} enligt {Method} ");
        text.Append(CultureInfo.InvariantCulture, $"({ShareText}); ut {Swap.Bought}");
        text.Append(Swap.Venue is { } venue ? $" genom {venue.Name}." : ".");
        text.Append(Follows ? FollowsText() : " Spåret slutar i swappen, enligt användarens val.");
        text.Append(" Det är en bedömning, inte ett faktum på kedjan.");
        return text.ToString();
    }

    /// <summary>
    /// Says what follows through the swap, and what the price check took away: the part that stays in the pool when
    /// less value came out, or the part not traced when more came out.
    /// </summary>
    /// <returns>The sentences, each starting with a space.</returns>
    private string FollowsText()
    {
        if (Price?.FollowShare is not null)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $" Det som kom ut var värt mer än det som gick in ({Price.Summary}), så bara den andel som det spårade "
                + $"var värt följer med, {TracedBought}, enligt {SwapValue.Analysis}; resten är inte spårat.");
        }

        var kept = KeptByPool.Units.Sign > 0
            ? string.Create(CultureInfo.InvariantCulture, $" {KeptByPool} stannar i poolen ({Price!.Summary}).")
            : string.Empty;
        return string.Create(CultureInfo.InvariantCulture,
            $" Värdet följer med: samma andel av det som kom ut, {TracedBought}, enligt {SwapValue.Analysis}.{kept}");
    }
}

/// <summary>What a swap's price check found (<see cref="SwapValue.CheckPrice"/>).</summary>
public enum SwapPriceVerdict
{
    /// <summary>The case lacks a rate for what went in or came out, or the swap's time: the price is not tested.</summary>
    NotTested,

    /// <summary>The values in and out agree within <see cref="SwapValue.MostPriceGap"/>: the value follows.</summary>
    Reasonable,

    /// <summary>
    /// The values differ by more than <see cref="SwapValue.MostPriceGap"/>: an absurd price, so the value does not
    /// follow and the money goes on to the pool as an ordinary payment.
    /// </summary>
    Unreasonable,
}

/// <summary>
/// The case's rates for the swap's price check, given by the caller so that Tracing needs no rate source of its own:
/// what an amount was worth at a time, in one currency.
/// </summary>
/// <param name="ValueAt">Gives an amount's value at a time, or <see langword="null"/> when the case has no rate.</param>
/// <param name="Currency">The currency the values are in, for example <c>SEK</c>.</param>
public sealed record SwapPricing(Func<AssetAmount, DateTimeOffset, decimal?> ValueAt, string Currency);

/// <summary>
/// The check <c>swap-price v1</c> on one swap: what went in and what came out were worth at the swap's time, and
/// whether the price is reasonable. It is a judgement on rates, never a fact on the chain (principle 2).
/// </summary>
/// <param name="Swap">The swap.</param>
/// <param name="Verdict">What the check found.</param>
/// <param name="SoldValue">What went in was worth, or <see langword="null"/> when not tested.</param>
/// <param name="BoughtValue">What came out was worth, or <see langword="null"/> when not tested.</param>
/// <param name="Currency">The currency, or <see langword="null"/> without rates.</param>
public sealed record SwapPriceCheck(
    TokenSwap Swap, SwapPriceVerdict Verdict, decimal? SoldValue, decimal? BoughtValue, string? Currency)
{
    /// <summary>The check's name and version, as <em>Varför?</em> writes it.</summary>
    public const string Check = "swap-price v1";

    /// <summary>Gets whether the price is absurd, so that the value does not follow.</summary>
    public bool IsUnreasonable => Verdict == SwapPriceVerdict.Unreasonable;

    /// <summary>
    /// Gets what came out was worth as a share of what went in, when the price is tested and less came out, for
    /// example 0.999; <see langword="null"/> otherwise. The rest of the traced value stays in the pool.
    /// </summary>
    public decimal? KeptShare => Verdict == SwapPriceVerdict.Reasonable && SoldValue > 0 && BoughtValue < SoldValue
        ? BoughtValue / SoldValue
        : null;

    /// <summary>
    /// Gets what went in was worth as a share of what came out, when the price is tested and more came out, for
    /// example 0.8; <see langword="null"/> otherwise. Only that share of what came out follows as traced, so that the
    /// traced value never grows through a swap; the rest is not traced.
    /// </summary>
    public decimal? FollowShare => Verdict == SwapPriceVerdict.Reasonable && BoughtValue > 0 && SoldValue < BoughtValue
        ? SoldValue / BoughtValue
        : null;

    /// <summary>Gets whether the tested price changes the traced amounts: an absurd price, or values that differ.</summary>
    public bool ChangesAmounts => IsUnreasonable || KeptShare is not null || FollowShare is not null;

    /// <summary>Takes a share of an amount in whole units, rounded down, so that the share never creates value.</summary>
    /// <param name="units">The units.</param>
    /// <param name="share">The share, from 0 to 1, or <see langword="null"/> for all of it.</param>
    /// <returns>The share of the units.</returns>
    public static BigInteger ShareOf(BigInteger units, decimal? share)
    {
        if (share is not { } part || part >= 1)
        {
            return units;
        }

        const long Scale = 1_000_000_000_000;
        return units * new BigInteger(Math.Floor(Math.Max(part, 0) * Scale)) / Scale;
    }

    /// <summary>
    /// Gets how much the values in and out differ, as a share of the larger, for example 0.001; <see langword="null"/>
    /// when the price is not tested.
    /// </summary>
    public decimal? Gap
    {
        get
        {
            if (Verdict == SwapPriceVerdict.NotTested || SoldValue is not { } sold || BoughtValue is not { } bought)
            {
                return null;
            }

            var larger = Math.Max(sold, bought);
            return larger <= 0 ? 0 : Math.Abs(sold - bought) / larger;
        }
    }

    /// <summary>
    /// Gets the check in a few words, for a report: <c>priset prövat, 0,1 % skillnad</c> or <c>priset inte prövat</c>,
    /// with the check's name; never the value's following without whether its price was tested.
    /// </summary>
    public string Summary
    {
        get
        {
            if (Gap is not { } gap)
            {
                return $"priset inte prövat, kursen vid tx:ens tid saknas i ärendet ({Check})";
            }

            var text = (gap * 100).ToString("0.0", CultureInfo.GetCultureInfo("sv-SE"));
            return IsUnreasonable
                ? $"orimligt pris, {text} % skillnad ({Check})"
                : $"priset prövat, {text} % skillnad ({Check})";
        }
    }

    /// <summary>Explains the check in plain Swedish (principle 3): both amounts, both values and what follows.</summary>
    /// <returns>The explanation.</returns>
    public string Explain()
    {
        var swedish = CultureInfo.GetCultureInfo("sv-SE");
        var limit = (SwapValue.MostPriceGap * 100).ToString("0", swedish);
        var sold = $"{SoldValue?.ToString("N2", swedish)} {Currency}";
        var bought = $"{BoughtValue?.ToString("N2", swedish)} {Currency}";
        return Verdict switch
        {
            SwapPriceVerdict.Unreasonable =>
                $"Orimligt pris i swappen ({Check}): in {Swap.Sold}, värt {sold}, ut {Swap.Bought}, värt {bought}, "
                + $"vid tx:ens tid. Värdena skiljer mer än {limit} %, så värdet följer inte med, och pengarna går "
                + "vidare till poolen som en vanlig betalning. Det är en bedömning på kurserna, inte ett faktum på "
                + "kedjan.",
            SwapPriceVerdict.Reasonable =>
                $"Priset prövat ({Check}): in värt {sold}, ut värt {bought}, inom {limit} %.",
            _ => $"Priset i swappen är inte prövat ({Check}): ärendet saknar kursen för det som gick in eller kom ut "
                + "vid tx:ens tid. Värdet följer ändå med, som förval.",
        };
    }
}
