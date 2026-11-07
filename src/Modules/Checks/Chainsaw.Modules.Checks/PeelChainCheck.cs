using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Shows the clustering's peel-chain findings as one check per chain, with an area around the hops (sketch KF1b). It
/// finds nothing of its own; the clustering's <c>peel-chain</c> heuristic has already done the work, and the hops it
/// names are joined into one chain here.
/// </summary>
internal sealed class PeelChainCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "peel-chain-area";

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 1);

    /// <inheritdoc/>
    public string DisplayName => "Peel chain";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var found = LatestFindings.Of<PeelChainFinding>(input).ToList();
        if (found.Count == 0)
        {
            return [];
        }

        var byId = input.Transactions.ToDictionary(transaction => transaction.Txid);
        var chains = new UnionFind<string>();
        foreach (var (transaction, _, finding) in found)
        {
            chains.Add(transaction.Txid.Value);
            foreach (var hop in finding.Hops)
            {
                chains.Union(transaction.Txid.Value, hop);
            }
        }

        var findings = new List<CheckFinding>();
        foreach (var group in chains.Groups())
        {
            var hops = group
                .Select(id => Txid.TryParse(id, out var txid) && byId.TryGetValue(txid, out var known) ? known : null)
                .OfType<BitcoinTransaction>()
                .OrderBy(hop => hop.Block?.Height ?? long.MaxValue)
                .ThenBy(hop => hop.Txid.Value, StringComparer.Ordinal)
                .ToList();
            if (hops.Count == 0)
            {
                continue;
            }

            findings.Add(Describe(group.Count, hops, found));
        }

        return [.. findings.OrderBy(finding => finding.Key, StringComparer.Ordinal)];
    }

    /// <summary>Describes one chain.</summary>
    /// <param name="length">How many hops the clustering knows of, also those not in the graph.</param>
    /// <param name="hops">The hops in the graph, earliest first.</param>
    /// <param name="found">Every peel-chain finding.</param>
    /// <returns>The check.</returns>
    private CheckFinding Describe(
        int length,
        IReadOnlyList<BitcoinTransaction> hops,
        IReadOnlyList<(BitcoinTransaction Transaction, StoredFinding Stored, PeelChainFinding Finding)> found)
    {
        var inChain = hops.Select(hop => hop.Txid).ToHashSet();
        var mine = found.Where(item => inChain.Contains(item.Transaction.Txid)).ToList();
        var peeled = mine.Aggregate(
            Satoshi.Zero, (sum, item) => sum + item.Transaction.Outputs[item.Finding.PeelOutput].Amount);
        var addresses = hops
            .SelectMany(hop => hop.Inputs.Select(input => input.Address))
            .OfType<BitcoinAddress>()
            .ToList();
        var last = mine.LastOrDefault(item => item.Transaction.Txid == hops[^1].Txid);
        if (last.Finding is { } finding && hops[^1].Outputs[finding.ContinuingOutput].Address is { } onward)
        {
            addresses.Add(onward);
        }

        var heuristic = mine.Select(item => item.Stored.Heuristic.ToString()).FirstOrDefault() ?? "peel-chain";
        var count = CheckText.Count(length);
        var why = $"Klustringens {heuristic} känner igen {count} hopp i rad där lite skalas av och resten går vidare "
            + $"som växel; {CheckText.Btc(peeled)} skalades av i de hopp grafen har. Det är en bedömning, inte ett "
            + "faktum: formen räcker inte för att se var ägaren byter, så inget slås ihop av den.";
        return new CheckFinding(
            hops[0].Txid.Value,
            CheckKind.Check,
            AreaSort.Obfuscation,
            $"Peel chain, {count} hopp",
            $"Växeln följd i varje hopp, {CheckText.Btc(peeled)} skalades av",
            why,
            [.. addresses.Distinct()],
            [],
            [.. hops.Select(CheckBasis.Of)],
            []);
    }
}
