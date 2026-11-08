using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Suggests the wallets the clustering proposes but did not merge (sketch KF1b, <em>Plånböcker</em>): addresses that a
/// heuristic says probably have the same owner, drawn as one grey area around their actors. The user approves or
/// dismisses it in the panel (sketch KF2b); an approval merges the addresses as the user's judgement
/// (<c>user v1</c> in Clustering), see <see cref="CheckService"/>.
/// </summary>
internal sealed class WalletCheck : ICheckAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = CheckAnalysisNames.Wallets;

    /// <summary>What a waiting wallet's key starts with when it only joins wallets the user has approved.</summary>
    public const string JoinPrefix = "joins:";

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 1);

    /// <inheritdoc/>
    public string DisplayName => "Plånböcker";

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <summary>
    /// Gives the proposals of a transaction that a wallet is made of: the suggestions of more than one address, of the
    /// highest version of each heuristic, as the clustering counts them.
    /// </summary>
    /// <param name="conclusions">What the clustering stored about the transaction.</param>
    /// <returns>The proposals, in the clustering's order.</returns>
    public static IEnumerable<ClusterEvidence> ProposalsOf(TransactionConclusions conclusions)
    {
        ArgumentNullException.ThrowIfNull(conclusions);
        return LatestFindings.Highest(conclusions.Evidence, evidence => evidence.Heuristic)
            .Where(evidence => evidence.Strength == EvidenceStrength.Suggestion && evidence.Addresses.Count > 1);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A wallet the user approved is shown as approved under the key it was approved by, and the proposals its merges
    /// cover are not suggested again; what is new around it (a proposal that reaches an address outside it) is a new
    /// suggestion, so that an approval never stands for addresses the user never saw (principle 2).
    /// </remarks>
    public IReadOnlyList<CheckFinding> Run(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var approved = input.ApprovedWallets.Where(wallet => wallet.Analysis == Id).ToList();
        var covered = approved.Select(wallet => wallet.Addresses.ToHashSet()).ToList();
        var proposals = input.Transactions
            .SelectMany(transaction => input.Conclusions.TryGetValue(transaction.Txid, out var conclusions)
                ? ProposalsOf(conclusions).Select(evidence => (transaction, evidence))
                : [])
            .Where(item => !covered.Any(set => item.evidence.Addresses.All(set.Contains)))
            .ToList();
        var wallets = new UnionFind<BitcoinAddress>();
        foreach (var (_, evidence) in proposals)
        {
            foreach (var address in evidence.Addresses.Skip(1))
            {
                wallets.Union(evidence.Addresses[0], address);
            }
        }

        var known = covered.SelectMany(set => set).ToHashSet();
        return
        [
            .. approved.Select(wallet => Approved(wallet, input)),
            .. wallets.Groups().Select(group => Suggested(group, proposals, known)),
        ];
    }

    /// <summary>Gives a wallet that waits for the user: the proposals that join its addresses.</summary>
    /// <param name="group">The wallet's addresses.</param>
    /// <param name="proposals">Every proposal still to decide, with its transaction.</param>
    /// <param name="known">The addresses of the wallets the user has approved.</param>
    /// <returns>The suggestion.</returns>
    private static CheckFinding Suggested(
        IReadOnlyCollection<BitcoinAddress> group,
        IReadOnlyList<(BitcoinTransaction Transaction, ClusterEvidence Evidence)> proposals,
        HashSet<BitcoinAddress> known)
    {
        var members = group.ToHashSet();
        var mine = proposals.Where(item => item.Evidence.Addresses.Any(members.Contains)).ToList();
        var heuristics = string.Join(
            ", ", mine.Select(item => item.Evidence.Heuristic.ToString()).Distinct(StringComparer.Ordinal));
        var why = $"Klustringen föreslår att {CheckText.Count(group.Count)} adresser har samma ägare "
            + $"({heuristics}), men slog inte ihop dem: {mine[0].Evidence.Reason} Godkänner du förslaget slås "
            + "de ihop som din bedömning (user v1), som kan brytas upp och ångras. Ett förslag är en "
            + "bedömning, inte ett faktum.";
        return new CheckFinding(
            KeyOf(group, known),
            CheckKind.Suggestion,
            AreaSort.Wallet,
            "Samma plånbok troligt",
            heuristics,
            why,
            [.. group],
            [],
            [.. mine.Select(item => item.Transaction).Distinct().Select(CheckBasis.Of)],
            []);
    }

    /// <summary>
    /// Gives a waiting wallet's key: its least address that no approved wallet holds, so that it is the same as
    /// before anything was approved and never the key of an approved wallet; a proposal that only joins approved
    /// wallets gets a key of its own.
    /// </summary>
    /// <param name="group">The wallet's addresses.</param>
    /// <param name="known">The addresses of the wallets the user has approved.</param>
    /// <returns>The key.</returns>
    private static string KeyOf(IReadOnlyCollection<BitcoinAddress> group, HashSet<BitcoinAddress> known)
    {
        var fresh = group.Where(address => !known.Contains(address)).Select(address => address.Value).ToList();
        return fresh.Count > 0
            ? fresh.Min(StringComparer.Ordinal)!
            : JoinPrefix + group.Select(address => address.Value).Min(StringComparer.Ordinal);
    }

    /// <summary>
    /// Gives a wallet the user has approved, under the key it was approved by: approved as the user's judgement, and
    /// marked when the merge has been broken up since.
    /// </summary>
    /// <param name="wallet">The approved wallet.</param>
    /// <param name="input">What the analysis runs on, for the transactions' blocks.</param>
    /// <returns>The finding.</returns>
    private static CheckFinding Approved(ApprovedWallet wallet, CheckInput input)
    {
        var why = $"Du godkände att {CheckText.Count(wallet.Addresses.Count)} adresser har samma ägare, och de är "
            + "sammanslagna som din bedömning (user v1), inte ett faktum.";
        if (wallet.IsBroken)
        {
            why += " Sammanslagningen är uppbruten sedan godkännandet, så adresserna står inte längre i ett kluster.";
        }

        var basis = wallet.Basis.ToHashSet();
        return new CheckFinding(
            wallet.FindingKey,
            CheckKind.Suggestion,
            AreaSort.Wallet,
            wallet.IsBroken ? "Godkänd plånbok, uppbruten" : "Godkänd plånbok",
            UserMerges.Id.ToString(),
            why,
            wallet.Addresses,
            [],
            [.. input.Transactions.Where(transaction => basis.Contains(transaction.Txid)).Select(CheckBasis.Of)],
            []);
    }
}
