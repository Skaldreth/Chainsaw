using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;

namespace Chainsaw.Modules.Checks.Contracts;

/// <summary>What an analysis runs on: the case's transactions in the graph, the mixers it knows, what the
/// clustering concluded and the candidates the user has approved, all read from the case.</summary>
/// <param name="Transactions">The transactions, in a stable order.</param>
/// <param name="Mixers">The actors that labels say are mixers.</param>
/// <param name="Conclusions">What the clustering stored about each transaction that it has analysed.</param>
/// <param name="Approved">
/// The candidates whose approval is in force, ordered by coin; <see langword="null"/> for none. An analysis that
/// follows the user's judgements (for example <c>mixer-chain</c>) reads them; they stay the user's judgements
/// (principle 2).
/// </param>
public sealed record CheckInput(
    IReadOnlyList<BitcoinTransaction> Transactions,
    IReadOnlyList<KnownMixer> Mixers,
    IReadOnlyDictionary<Txid, TransactionConclusions> Conclusions,
    IReadOnlyList<ApprovedCandidate>? Approved = null)
{
    /// <summary>Gets an input with nothing in it.</summary>
    public static CheckInput Empty { get; } = new([], [], new Dictionary<Txid, TransactionConclusions>());

    /// <summary>Gets the candidates whose approval is in force; empty when none.</summary>
    public IReadOnlyList<ApprovedCandidate> Approved { get; init; } = Approved ?? [];
}

/// <summary>An actor that a label says is a mixer: its addresses in the graph.</summary>
/// <param name="Addresses">The mixer's known addresses; never empty.</param>
public sealed record KnownMixer(IReadOnlyList<BitcoinAddress> Addresses);

/// <summary>A candidate of a suggestion that the user has approved, and not taken back: the coin it names.</summary>
/// <param name="Analysis">The analysis and version whose suggestion it belongs to.</param>
/// <param name="FindingKey">The suggestion's key, for example the deposit's coin <c>txid:index</c>.</param>
/// <param name="Txid">The transaction the candidate came in.</param>
/// <param name="Output">The output's index.</param>
public sealed record ApprovedCandidate(HeuristicId Analysis, string FindingKey, Txid Txid, int Output);
