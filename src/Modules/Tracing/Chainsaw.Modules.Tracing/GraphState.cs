using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.Modules.Tracing;

/// <summary>The graph's state as the case holds it, read in one go.</summary>
/// <param name="StartPoints">The start points, in the order they were added.</param>
/// <param name="Expansions">What has been expanded, in the order it was expanded.</param>
/// <param name="Links">The user's links to other chains that are in force, oldest first.</param>
/// <param name="Positions">The places, by position key and layout.</param>
internal sealed record GraphState(
    IReadOnlyList<TraceStartPoint> StartPoints,
    IReadOnlyList<Expansion> Expansions,
    IReadOnlyList<CrossChainLink> Links,
    IReadOnlyDictionary<string, IReadOnlyDictionary<GraphLayout, StoredPosition>> Positions)
{
    /// <summary>
    /// Gets the member keys the user picked out of the piles (see <see cref="NodeKeys.Members"/>); empty when none.
    /// </summary>
    public IReadOnlySet<string> Picked { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Gets the user's choices in force not to follow a swap (sketch SW1a), oldest first.</summary>
    public IReadOnlyList<SwapStop> SwapStops { get; init; } = [];

    /// <summary>
    /// Gets the user's choices in force to follow the replacer of a gone transaction (sketch GR9a), oldest first.
    /// </summary>
    public IReadOnlyList<ReplacementFollow> Replacements { get; init; } = [];

    /// <summary>
    /// Gets every transaction the graph holds: the start points' transactions, those the expansions brought in and
    /// the replacers the user chose to follow, each once, in the order they came.
    /// </summary>
    public IReadOnlyList<Txid> Transactions =>
    [
        .. StartPoints.Where(start => start.Txid is not null).Select(start => start.Txid!)
            .Concat(Expansions.SelectMany(expansion => expansion.Transactions))
            .Concat(Replacements.Select(follow => follow.Replacer))
            .Distinct(),
    ];

    /// <summary>
    /// Gets every Ethereum transaction the graph holds whole: the start points' transactions and those expanded, each
    /// once, in the order they came. The transactions an address's history brought in are read from its pages
    /// (<see cref="EthereumHistories"/>).
    /// </summary>
    public IReadOnlyList<EthereumTxHash> EthereumTransactions =>
    [
        .. StartPoints.Where(start => start.EthereumTransaction is not null).Select(start => start.EthereumTransaction!)
            .Concat(Expansions.Where(expansion => expansion.EthereumTransaction is not null)
                .SelectMany(expansion => expansion.EthereumTransactions))
            .Distinct(),
    ];

    /// <summary>Gets the Ethereum addresses whose history was brought in, with the transactions it brought.</summary>
    public IReadOnlyList<Expansion> EthereumHistories =>
        [.. Expansions.Where(expansion => expansion.EthereumAddress is not null)];
}

/// <summary>A stored place and when it was set.</summary>
/// <param name="Position">The place.</param>
/// <param name="UpdatedUtc">When the user put it there.</param>
internal sealed record StoredPosition(NodePosition Position, DateTimeOffset UpdatedUtc);
