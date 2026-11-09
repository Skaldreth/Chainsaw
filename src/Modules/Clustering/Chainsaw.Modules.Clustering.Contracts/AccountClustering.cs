using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>
/// The clustering of accounts (Ethereum and the chains on its machine): an account has no inputs to merge, so its
/// heuristics read how money moves between accounts (sketch KE1, CLAUDE.md <em>Klustra</em>). What they conclude is
/// sealed in the case like the clustering of addresses, and the clusters are built from it whenever they are asked for;
/// every conclusion is an assessment with its heuristic, version and transactions (principle 2).
/// </summary>
public interface IAccountClustering
{
    /// <summary>Gets the account heuristics, each with its name and version.</summary>
    IReadOnlyList<HeuristicId> Heuristics { get; }

    /// <summary>
    /// Runs the heuristics over what the graph has read of a chain, seals what is new under one journal entry, and
    /// gives the clusters and proposals that count now.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="chain">The chain, on Ethereum's machine.</param>
    /// <param name="activity">What the graph has read of the chain, and what is known about its accounts.</param>
    /// <returns>The clusters and the proposals.</returns>
    AccountClusteringRun Analyse(ICase @case, Chain chain, AccountActivity activity);

    /// <summary>Reads the clusters and proposals that count now, without running the heuristics.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="chain">The chain.</param>
    /// <returns>The clusters and the proposals.</returns>
    AccountClusteringRun Read(ICaseReader reader, Chain chain);
}

/// <summary>
/// What the graph has read of a chain on Ethereum's machine, for the account heuristics. The caller leaves out the
/// transactions that are gone or may be (GR9a), so that no conclusion rests on them.
/// </summary>
/// <param name="Transfers">The confirmed and pending transfers read.</param>
/// <param name="Fees">The fees paid in those transactions.</param>
/// <param name="FullyRead">
/// The accounts whose whole history is read; only they can be said to have sent nothing else, or to have got nothing
/// before.
/// </param>
/// <param name="Services">
/// The accounts with a label of a service that takes deposits (an exchange and the like): only a sweep into one of them
/// merges by itself.
/// </param>
/// <param name="Contracts">
/// The accounts known to be contracts (pools, routers, mixers, bridges), never a collector.
/// </param>
public sealed record AccountActivity(
    IReadOnlyCollection<EthereumTransfer> Transfers,
    IReadOnlyCollection<EthereumFee> Fees,
    IReadOnlySet<ChainAddress> FullyRead,
    IReadOnlySet<ChainAddress> Services,
    IReadOnlySet<ChainAddress> Contracts);

/// <summary>The account clusters and the proposals that count now.</summary>
/// <param name="Clusters">The clusters the heuristics merged by themselves, each of two or more accounts.</param>
/// <param name="Proposals">
/// What a heuristic proposes but leaves to the user (KF2b), such as the same payer of gas.
/// </param>
public sealed record AccountClusteringRun(
    IReadOnlyList<AccountCluster> Clusters, IReadOnlyList<AccountEvidence> Proposals)
{
    /// <summary>Gets a run with nothing in it.</summary>
    public static AccountClusteringRun Empty { get; } = new([], []);

    /// <summary>Finds the cluster an account is in.</summary>
    /// <param name="account">The account.</param>
    /// <returns>The cluster, or <see langword="null"/> when it is in none.</returns>
    public AccountCluster? ClusterOf(ChainAddress account) =>
        Clusters.FirstOrDefault(cluster => cluster.Members.Contains(account));
}

/// <summary>Accounts merged by the account heuristics, with what they rest on.</summary>
/// <param name="Members">The accounts, ordered by their text.</param>
/// <param name="Collector">
/// The account the others empty into (a service's hot wallet), or <see langword="null"/>.
/// </param>
/// <param name="Evidence">The sealed conclusions the cluster rests on, in sequence order.</param>
public sealed record AccountCluster(
    IReadOnlyList<ChainAddress> Members, ChainAddress? Collector, IReadOnlyList<AccountEvidence> Evidence);

/// <summary>A sealed conclusion of an account heuristic.</summary>
/// <param name="Sequence">Its sequence in the sealed chain.</param>
/// <param name="Heuristic">The heuristic and its version.</param>
/// <param name="Chain">The chain.</param>
/// <param name="Transactions">The transactions it rests on.</param>
/// <param name="Accounts">The accounts it says have the same owner; the collector first for a sweep.</param>
/// <param name="Strength">Whether it merges by itself or is a proposal.</param>
/// <param name="Reason">Why, in plain Swedish.</param>
/// <param name="JournalSequence">The journal entry it was sealed under.</param>
/// <param name="RecordedUtc">When it was sealed.</param>
public sealed record AccountEvidence(
    long Sequence,
    HeuristicId Heuristic,
    Chain Chain,
    IReadOnlyList<EthereumTxHash> Transactions,
    IReadOnlyList<ChainAddress> Accounts,
    EvidenceStrength Strength,
    string Reason,
    long JournalSequence,
    DateTimeOffset RecordedUtc);

/// <summary>The account heuristics' stable names.</summary>
public static class AccountHeuristicNames
{
    /// <summary>
    /// Deposit addresses that are emptied into the same service's account are that service's (Victor 2020, "Address
    /// clustering heuristics for Ethereum"); into an account without such a label, only a proposal.
    /// </summary>
    public const string DepositSweep = "deposit-sweep";

    /// <summary>
    /// Accounts whose first gas came from the same account, before they sent anything, may share an owner.
    /// </summary>
    public const string GasFunding = "gas-funding";
}
