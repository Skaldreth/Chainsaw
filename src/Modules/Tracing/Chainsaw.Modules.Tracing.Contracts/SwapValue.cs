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
        text.Append(Follows
            ? string.Create(CultureInfo.InvariantCulture,
                $" Värdet följer med: samma andel av det som kom ut, {TracedBought}, enligt {SwapValue.Analysis}.")
            : " Spåret slutar i swappen, enligt användarens val.");
        text.Append(" Det är en bedömning, inte ett faktum på kedjan.");
        return text.ToString();
    }
}
