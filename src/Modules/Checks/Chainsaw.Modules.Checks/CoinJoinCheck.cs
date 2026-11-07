using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Shows the clustering's CoinJoin findings as checks with an area (sketch KF1b): the CoinJoin and the actors that paid
/// into it. It finds nothing of its own; the clustering's <c>coinjoin</c> heuristic has already done the work.
/// </summary>
internal sealed class CoinJoinCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "coinjoin-area";

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 1);

    /// <inheritdoc/>
    public string DisplayName => "CoinJoin";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return
        [
            .. LatestFindings.Of<CoinJoinFinding>(input).Select(found =>
            {
                var (transaction, stored, finding) = found;
                var recognised = finding.Certainty == CoinJoinCertainty.Recognised;
                var title = recognised ? $"CoinJoin, {PatternName(finding.Pattern)}" : "Möjlig CoinJoin";
                var summary = $"{CheckText.Count(finding.EqualOutputs)} × {CheckText.Btc(finding.Denomination)}, "
                    + $"{CheckText.Count(finding.Inputs)} inputs";
                var ends = recognised
                    ? "Spåret tar slut här, och inget i tx:en slås ihop."
                    : "Spåret går vidare, men klustringen gör bara förslag i tx:en.";
                var why = $"Klustringens {stored.Heuristic} känner igen tx {CheckText.Short(transaction.Txid)} som "
                    + $"{title.ToLowerInvariant()}: {finding.Reason} {ends} Det är en bedömning, inte ett faktum.";
                return new CheckFinding(
                    transaction.Txid.Value,
                    CheckKind.Check,
                    AreaSort.Obfuscation,
                    title,
                    summary,
                    why,
                    [.. transaction.Inputs.Select(i => i.Address).OfType<Chains.Contracts.BitcoinAddress>().Distinct()],
                    [transaction.Txid],
                    [CheckBasis.Of(transaction)],
                    []);
            }),
        ];
    }

    /// <summary>Gives a CoinJoin pattern's name.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <returns>For example <c>Wasabi</c>.</returns>
    private static string PatternName(CoinJoinPattern pattern) => pattern switch
    {
        CoinJoinPattern.Whirlpool => "Whirlpool",
        CoinJoinPattern.Wasabi => "Wasabi",
        CoinJoinPattern.JoinMarket => "JoinMarket",
        _ => "okänt mönster",
    };
}
