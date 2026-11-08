using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Recognises many hops and delay (layering): a row of transactions where each takes one coin and passes almost all
/// of it on to one new address, so that the money only passes through, and how long it lay still between the hops.
/// A check with an area of the sort Obfuscation; a judgement, never a fact (principle 2).
/// </summary>
internal sealed class PassThroughCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "pass-through";

    /// <summary>How many hops in a row must only pass the money on to count.</summary>
    public const int MinimumHops = 3;

    /// <summary>The largest share of a hop's coin that may go elsewhere than on (the fee): 0.02 is two percent.</summary>
    public const double MostLostShare = 0.02;

    /// <summary>A pause between two hops at least this long is a delay.</summary>
    public static readonly TimeSpan DelayFrom = TimeSpan.FromDays(3);

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 1);

    /// <inheritdoc/>
    public string DisplayName => "Många hopp";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var hops = input.Transactions.Where(IsHop).ToDictionary(transaction => transaction.Txid);
        var spenders = input.Spenders();
        var findings = new List<CheckFinding>();
        foreach (var first in hops.Values.Where(hop => !IsContinued(hop, hops)))
        {
            var row = new List<BitcoinTransaction> { first };
            while (row.Count <= hops.Count
                && spenders.TryGetValue((row[^1].Txid, row[^1].PayingOutputs()[0].Index), out var next)
                && hops.ContainsKey(next.Txid))
            {
                row.Add(next);
            }

            if (row.Count >= MinimumHops)
            {
                findings.Add(Describe(row));
            }
        }

        return [.. findings.OrderBy(finding => finding.Key, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Says whether a transaction only passes money on: one input with a known amount, one output (with an address),
    /// and at most <see cref="MostLostShare"/> of the coin lost on the way.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool IsHop(BitcoinTransaction transaction) =>
        !transaction.IsCoinbase
        && transaction.Inputs.Count == 1
        && transaction.Inputs[0].Amount is { Value: > 0 } amount
        && transaction.Outputs.Count == 1
        && transaction.Outputs[0].Address is not null
        && transaction.Outputs[0].Amount.Value >= amount.Value * (1 - MostLostShare);

    /// <summary>Says whether a hop's coin came from another hop, so that it is not the first of its row.</summary>
    /// <param name="hop">The hop.</param>
    /// <param name="hops">Every hop.</param>
    /// <returns><see langword="true"/> when it continues a row.</returns>
    private static bool IsContinued(BitcoinTransaction hop, Dictionary<Txid, BitcoinTransaction> hops) =>
        hop.Inputs[0].PreviousTxid is { } previous && hops.ContainsKey(previous);

    /// <summary>Describes a row of hops.</summary>
    /// <param name="row">The hops, in order.</param>
    /// <returns>The check.</returns>
    private CheckFinding Describe(IReadOnlyList<BitcoinTransaction> row)
    {
        var gaps = row.Zip(row.Skip(1))
            .Select(pair => pair.Second.Block is { } later && pair.First.Block is { } earlier
                ? later.Time - earlier.Time
                : TimeSpan.Zero)
            .ToList();
        var longest = gaps.Count == 0 ? TimeSpan.Zero : gaps.Max();
        var delayed = longest >= DelayFrom;
        var span = row[^1].Block is { } last && row[0].Block is { } first ? last.Time - first.Time : TimeSpan.Zero;
        var (into, outOf) = (row[0].Inputs[0].Amount!.Value, row[^1].Outputs[0].Amount);
        var count = CheckText.Count(row.Count);
        var delay = delayed
            ? $" Pengarna låg still som längst {CheckText.Span(longest)} mellan två hopp: en fördröjning, som gör "
                + "att spåret sträcker sig långt i tiden."
            : string.Empty;
        var why = $"Analysen {this.Named()} ser {count} tx i rad som var och en tar ett mynt och skickar nästan allt "
            + $"vidare till en ny adress, under {CheckText.Span(span)}: pengarna bara passerar.{delay} Så lägger man "
            + "många hopp mellan brottet och uttaget. Hela raden kan läsas som en väg. Det är en bedömning, inte ett "
            + "faktum: en plånbok som flyttar sina pengar mellan egna adresser ser likadan ut.";
        var addresses = row.Select(hop => hop.Inputs[0].Address).OfType<BitcoinAddress>()
            .Append(row[^1].Outputs[0].Address!)
            .Distinct();
        return new CheckFinding(
            row[0].Txid.Value,
            CheckKind.Check,
            AreaSort.Obfuscation,
            delayed ? $"Många hopp, {count} i rad, med fördröjning" : $"Många hopp, {count} i rad",
            $"{CheckText.Btc(into)} BTC in, {CheckText.Btc(outOf)} BTC ut, längsta uppehåll {CheckText.Span(longest)}",
            why,
            [.. addresses],
            [],
            [.. row.Select(CheckBasis.Of)],
            []);
    }
}
