using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Shows the wallets the clustering proposes but did not merge (sketch KF1b, <em>Plånböcker</em>): addresses that a
/// heuristic says probably have the same owner, drawn as one grey area around their actors.
/// </summary>
internal sealed class WalletCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "wallet-proposals";

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 1);

    /// <inheritdoc/>
    public string DisplayName => "Plånböcker";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var proposals = input.Transactions
            .SelectMany(transaction => input.Conclusions.TryGetValue(transaction.Txid, out var conclusions)
                ? LatestFindings.Highest(conclusions.Evidence, evidence => evidence.Heuristic)
                    .Where(evidence => evidence.Strength == EvidenceStrength.Suggestion && evidence.Addresses.Count > 1)
                    .Select(evidence => (transaction, evidence))
                : [])
            .ToList();
        var wallets = new UnionFind<BitcoinAddress>();
        foreach (var (_, evidence) in proposals)
        {
            foreach (var address in evidence.Addresses.Skip(1))
            {
                wallets.Union(evidence.Addresses[0], address);
            }
        }

        return
        [
            .. wallets.Groups().Select(group =>
            {
                var members = group.ToHashSet();
                var mine = proposals.Where(item => item.evidence.Addresses.Any(members.Contains)).ToList();
                var heuristics = string.Join(
                    ", ", mine.Select(item => item.evidence.Heuristic.ToString()).Distinct(StringComparer.Ordinal));
                var why = $"Klustringen föreslår att {CheckText.Count(group.Count)} adresser har samma ägare "
                    + $"({heuristics}), men slog inte ihop dem: {mine[0].evidence.Reason} Ett förslag är en "
                    + "bedömning, inte ett faktum.";
                return new CheckFinding(
                    group.Select(address => address.Value).Min(StringComparer.Ordinal)!,
                    CheckKind.Check,
                    AreaSort.Wallet,
                    "Samma plånbok troligt",
                    heuristics,
                    why,
                    [.. group],
                    [],
                    [.. mine.Select(item => item.transaction).Distinct().Select(CheckBasis.Of)],
                    []);
            }),
        ];
    }
}
