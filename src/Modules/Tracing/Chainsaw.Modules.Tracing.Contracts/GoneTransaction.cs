using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// A transaction in the graph that the source no longer knows (sketch GR9a, <em>Finns inte längre</em>): the case
/// saw it in a block, and later answers, confirmed by the rule every chain shares, say the source does not know it.
/// Both answers stay in the case (principle 1); that it is gone is an assessment of what the source answered, made by
/// the analysis <see cref="Analysis"/> (principle 2), and the trail ends at it rather than follow money that is not in
/// the chain.
/// </summary>
/// <param name="Txid">The transaction.</param>
/// <param name="Change">The sealed answers it rests on: where the case first saw it, and the newest.</param>
/// <param name="Replacement">
/// What spends its coins now, as the case's sealed answers say; <see langword="null"/> when nothing has been asked.
/// </param>
public sealed record GoneTransaction(Txid Txid, BlockChange Change, TransactionReplacement? Replacement)
{
    /// <summary>The analysis that ends the trail at a gone transaction.</summary>
    public const string Analysis = "gone-tx";

    /// <summary>The analysis's version; raised whenever it may end the trail differently.</summary>
    public const int Version = 1;

    /// <summary>Gets the analysis and its version as the trail's end names it, for example <c>gone-tx v1</c>.</summary>
    public static string Source => string.Create(CultureInfo.InvariantCulture, $"{Analysis} v{Version}");

    /// <summary>Gets the transactions that spend its coins now, as far as the case knows.</summary>
    public IReadOnlyList<Txid> Replacers => Replacement?.Replacers ?? [];

    /// <summary>
    /// Gets whether the answers settle that it is gone, so that the trail ends at it: endpoints on two hosts said the
    /// source does not know it, or the case has only ever heard from one host (<see cref="RestsOnOneSource"/>), or
    /// answers about its coins from another host than those say another transaction spends them or they are free
    /// again (<see cref="ConfirmedByTheCoins"/>). Answers about the coins that still name it as their spender never
    /// settle it.
    /// </summary>
    public bool IsSettled => Replacement?.Kind != ReplacementKind.Conflicting
        && (Change.IsCorroborated || ConfirmedByTheCoins);

    /// <summary>
    /// Gets whether answers about its coins confirm that it is gone: they say another transaction spends them or they
    /// are free again, and every one came from another host than those that said the source does not know it.
    /// </summary>
    public bool ConfirmedByTheCoins => Replacement is
        { Kind: ReplacementKind.Replaced or ReplacementKind.PartlyReplaced or ReplacementKind.Dropped } replacement
        && replacement.Coins.All(coin => coin.Host is { } host && !Change.GoneHosts.Contains(host));

    /// <summary>
    /// Gets whether the end rests on a single source: the case has only ever heard from one host on the chain, so its
    /// word is all there is; the end says so, with the host.
    /// </summary>
    public bool RestsOnOneSource => IsSettled && Change.IsSingleSource && Change.GoneHosts.Count < 2
        && !ConfirmedByTheCoins;

    /// <summary>Gets the host a single-source end rests on, as sealed, or the endpoint's name.</summary>
    public string OneSource => Change.GoneHosts.FirstOrDefault() ?? Change.Now.Endpoint;

    /// <summary>
    /// Gets the end of the trail at it, with why, when the answers settle that it is gone (<see cref="IsSettled"/>);
    /// otherwise <see langword="null"/>, and the trace stops at it without ending (<see
    /// cref="TrailEndReason.GoneUncertain"/>).
    /// </summary>
    public TrailEndHint? Hint => IsSettled ? new(TrailEndReason.TransactionGone, Source, Explain()) : null;

    /// <summary>
    /// Explains in plain Swedish why the trail ends here (principle 3): where the case saw the transaction, what the
    /// source answered later, and what spends its coins now.
    /// </summary>
    /// <returns>The text.</returns>
    public string Explain()
    {
        var before = Change.Before;
        var where = before is { Height: { } height, Hash: { } hash }
            ? string.Create(CultureInfo.InvariantCulture, $"låg i block {height} ({hash})")
            : "väntade i mempool";
        var source = Change.Now.Endpoint.Length > 0 ? $" ({Change.Now.Endpoint})" : string.Empty;
        var answer = Change.Now.Sequence.ToString(CultureInfo.InvariantCulture);
        var head = $"Tx {Txid.Value} {where} när ärendet såg den, men källan{source} känner inte längre till den (svar "
            + $"{answer} i ärendet). ";
        if (!IsSettled)
        {
            return head + UnsettledText();
        }

        var single = RestsOnOneSource ? " " + OneSourceText(OneSource) : string.Empty;
        return head + "Den finns inte i kedjan, så spåret följs inte genom den; alla svar står kvar i ärendet. "
            + ReplacementText() + single;
    }

    /// <summary>Says that an end rests on a single source, naming it.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The sentence.</returns>
    public static string OneSourceText(string host) =>
        $"Slutet vilar på en enda källa, {host}: ärendet har inte hört från någon annan, så det är inte bekräftat "
        + "på annat håll.";

    /// <summary>Says why the answers do not settle that it is gone, in plain Swedish.</summary>
    /// <returns>The sentences.</returns>
    private string UnsettledText() => Replacement?.Kind == ReplacementKind.Conflicting
        ? "Men källan säger samtidigt att tx:en spenderar ett av mynten, så svaren går inte ihop. Spåret stannar här i "
            + "stället för att gissa; att fråga igen kan reda ut det."
        : "Bara en värd har sagt det, fast ärendet har svar från fler, och ingen annan värd har bekräftat vad som "
            + "spenderar mynten. Spåret stannar här i stället för att gissa; när en annan värd bekräftar det slutar "
            + "spåret vid tx:en.";

    /// <summary>Says what spends the coins now, in plain Swedish.</summary>
    /// <returns>The sentence.</returns>
    private string ReplacementText()
    {
        var replacers = string.Join(", ", Replacers.Select(txid => txid.Value));
        return (Replacement?.Kind ?? ReplacementKind.Unknown) switch
        {
            ReplacementKind.Replaced =>
                $"Tx {replacers} spenderar samma mynt i stället (ersatt med RBF eller en dubbelspendering). Att den "
                + "för samma pengar vidare är en bedömning, så spåret följer den bara om du väljer det.",
            ReplacementKind.PartlyReplaced =>
                $"Tx {replacers} spenderar en del av samma mynt i stället, och resten är ospenderade igen. Spåret "
                + "följer ersättaren bara om du väljer det.",
            ReplacementKind.Dropped =>
                "Ingen annan tx spenderar mynten: tx:en föll bort utan ersättare, och pengarna ligger kvar där de "
                + "kom ifrån.",
            ReplacementKind.Conflicting =>
                "Källan säger ändå att tx:en spenderar ett av mynten, så dess svar går inte ihop.",
            _ => "Vad som spenderar mynten nu är inte hämtat ännu.",
        };
    }
}

/// <summary>
/// The user's choice that the trace follows the transaction that replaced a gone one (sketch GR9a,
/// <em>Ersatt av</em>): a judgement that the replacer carries the same money on, sealed with who and when.
/// </summary>
/// <param name="Sequence">The record's number in the sealed chain.</param>
/// <param name="Gone">The transaction the source no longer knows.</param>
/// <param name="Replacer">The transaction the trace follows in its place.</param>
/// <param name="Reason">Why, or <see langword="null"/>; the reason is optional.</param>
/// <param name="JournalSequence">The journal entry that records it.</param>
/// <param name="RecordedUtc">When it was chosen.</param>
public sealed record ReplacementFollow(
    long Sequence, Txid Gone, Txid Replacer, string? Reason, long JournalSequence, DateTimeOffset RecordedUtc);

/// <summary>
/// The replacer of a gone transaction could not be had, from the source or the case, so the choice to follow it was not
/// sealed. The message says why in Swedish, as the lookup said it (for example that the exit is not connected), and
/// never names anything of the case.
/// </summary>
public sealed class ReplacementUnavailableException : InvalidOperationException
{
    /// <summary>Creates the exception with no reason given.</summary>
    public ReplacementUnavailableException()
        : this("Tx:en som ersatte den gick inte att hämta.")
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why, in Swedish.</param>
    public ReplacementUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with its cause.</summary>
    /// <param name="message">Why, in Swedish.</param>
    /// <param name="innerException">The cause.</param>
    public ReplacementUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
