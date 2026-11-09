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

    /// <summary>Gets the end of the trail at it, with why.</summary>
    public TrailEndHint Hint => new(TrailEndReason.TransactionGone, Source, Explain());

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
        return $"Tx {Txid.Value} {where} när ärendet såg den, men källan{source} känner inte längre till den (svar "
            + $"{answer} i ärendet). Den finns inte i kedjan, så spåret följs inte genom den; båda svaren står kvar i "
            + "ärendet. " + ReplacementText();
    }

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
