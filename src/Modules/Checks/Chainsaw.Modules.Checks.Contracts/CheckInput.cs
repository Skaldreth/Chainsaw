using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;

namespace Chainsaw.Modules.Checks.Contracts;

/// <summary>What an analysis runs on: the case's transactions in the graph, the mixers it knows, what the
/// clustering concluded and the user's decisions in force about candidates, all read from the case.</summary>
/// <param name="Transactions">The transactions, in a stable order.</param>
/// <param name="Mixers">The actors that labels say are mixers.</param>
/// <param name="Conclusions">What the clustering stored about each transaction that it has analysed.</param>
/// <param name="Approved">
/// The candidates whose approval is in force, ordered by analysis, suggestion and coin; <see langword="null"/> for
/// none. An analysis that follows the user's judgements (for example <c>mixer-chain</c>) reads them; they stay the
/// user's judgements (principle 2).
/// </param>
/// <param name="Dismissed">
/// The dismissals in force, of a candidate or of a whole suggestion, in the same order; <see langword="null"/> for
/// none. An analysis never builds on what the user dismissed.
/// </param>
public sealed record CheckInput(
    IReadOnlyList<BitcoinTransaction> Transactions,
    IReadOnlyList<KnownMixer> Mixers,
    IReadOnlyDictionary<Txid, TransactionConclusions> Conclusions,
    IReadOnlyList<ApprovedCandidate>? Approved = null,
    IReadOnlyList<DismissedDecision>? Dismissed = null)
{
    /// <summary>Gets an input with nothing in it.</summary>
    public static CheckInput Empty { get; } = new([], [], new Dictionary<Txid, TransactionConclusions>());

    /// <summary>Gets the candidates whose approval is in force; empty when none.</summary>
    public IReadOnlyList<ApprovedCandidate> Approved { get; init; } = Approved ?? [];

    /// <summary>Gets the dismissals in force; empty when none.</summary>
    public IReadOnlyList<DismissedDecision> Dismissed { get; init; } = Dismissed ?? [];

    /// <summary>
    /// Gets the wallets whose approval is in force, with the merges it made in the clustering; empty when none. The
    /// wallet analysis shows each as approved and suggests only what is new around it.
    /// </summary>
    public IReadOnlyList<ApprovedWallet> ApprovedWallets { get; init; } = [];
}

/// <summary>
/// A wallet the user approved (<see cref="CheckAnalysisNames.Wallets"/>), and not taken back, as the clustering holds
/// it: the addresses its merges (<c>user v1</c>) joined and whether they still stand together.
/// </summary>
/// <param name="Analysis">The analysis and version whose suggestion was approved.</param>
/// <param name="FindingKey">The suggestion's key.</param>
/// <param name="Addresses">The addresses the approval's merges in force hold, in order.</param>
/// <param name="Basis">The transactions the merges rest on.</param>
/// <param name="IsBroken">
/// Whether the user has broken the merge up since (the addresses no longer stand in one cluster).
/// </param>
public sealed record ApprovedWallet(
    HeuristicId Analysis,
    string FindingKey,
    IReadOnlyList<BitcoinAddress> Addresses,
    IReadOnlyList<Txid> Basis,
    bool IsBroken);

/// <summary>An actor that a label says is a mixer: its addresses and which service the label names.</summary>
/// <param name="Addresses">The mixer's known addresses; never empty.</param>
/// <param name="Service">
/// What the label says the mixer is, so that two actors with the same service are known as the same mixer;
/// <see langword="null"/> when not known.
/// </param>
public sealed record KnownMixer(IReadOnlyList<BitcoinAddress> Addresses, string? Service = null);

/// <summary>A candidate of a suggestion that the user has approved, and not taken back: the coin it names.</summary>
/// <param name="Analysis">The analysis and version whose suggestion it belongs to.</param>
/// <param name="FindingKey">The suggestion's key, for example the deposit's coin <c>txid:index</c>.</param>
/// <param name="Txid">The transaction the candidate came in.</param>
/// <param name="Output">The output's index.</param>
public sealed record ApprovedCandidate(HeuristicId Analysis, string FindingKey, Txid Txid, int Output);

/// <summary>A dismissal the user made, and has not taken back: of one candidate, or of a whole suggestion.</summary>
/// <param name="Analysis">The analysis and version whose suggestion it is about.</param>
/// <param name="FindingKey">The suggestion's key.</param>
/// <param name="CandidateKey">The candidate's key, or <see langword="null"/> for the whole suggestion.</param>
public sealed record DismissedDecision(HeuristicId Analysis, string FindingKey, string? CandidateKey);
