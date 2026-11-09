using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

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

    /// <summary>
    /// Gets the actors that labels say are exchanges or other services (not mixers); empty when none. An analysis
    /// reads them to say where a pattern ends, for example a peel chain that ends at an exchange.
    /// </summary>
    public IReadOnlyList<KnownService> Services { get; init; } = [];

    /// <summary>
    /// Gets the owner of each address in the graph as the clustering counts it now: the smallest address of its
    /// cluster, so that addresses with the same owner key are in the same cluster. An address missing here is its own
    /// owner. The clusters are judgements, so what an analysis concludes from them is too (principle 2).
    /// </summary>
    public IReadOnlyDictionary<BitcoinAddress, BitcoinAddress> Owners { get; init; } =
        new Dictionary<BitcoinAddress, BitcoinAddress>();

    /// <summary>
    /// Gets the addresses of the case's start points (and the payers of a start transaction) and of the actors a label
    /// says are victims. An analysis never counts them, or what they paid, as part of a service's business (for
    /// example as a customer's deposit).
    /// </summary>
    public IReadOnlySet<BitcoinAddress> Protected { get; init; } = new HashSet<BitcoinAddress>();

    /// <summary>
    /// Gets the addresses on every chain that a payment to or from is never a continuation of another payment: the
    /// start points' and those a label says are a victim's.
    /// </summary>
    public IReadOnlySet<ChainAddress> ProtectedAccounts { get; init; } = new HashSet<ChainAddress>();

    /// <summary>
    /// Gets the graph the input was built from, for an analysis that reads more than the Bitcoin transactions (the
    /// cross-chain analysis reads every chain's payments and the links to other chains); <see langword="null"/> when
    /// the input was built without one.
    /// </summary>
    public TraceGraph? Graph { get; init; }

    /// <summary>
    /// Gets what the cross-chain search read from the case of the other chains (<see cref="ICrossChainLookup"/>);
    /// empty when nothing.
    /// </summary>
    public CrossChainArrivals CrossChain { get; init; } = CrossChainArrivals.Empty;

    /// <summary>Gives the owner of an address (<see cref="Owners"/>).</summary>
    /// <param name="address">The address.</param>
    /// <returns>Its owner key; the address itself when the clustering links it to no other.</returns>
    public BitcoinAddress OwnerOf(BitcoinAddress address) => Owners.GetValueOrDefault(address) ?? address;
}

/// <summary>An actor that a label says is an exchange or another service, but not a mixer.</summary>
/// <param name="Addresses">The service's known addresses; never empty.</param>
/// <param name="IsExchange">Whether the label says it is an exchange, where money is taken out.</param>
/// <param name="Name">What the label calls it, or <see langword="null"/>.</param>
public sealed record KnownService(IReadOnlyList<BitcoinAddress> Addresses, bool IsExchange, string? Name = null);

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
