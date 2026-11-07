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
    /// Gets every transaction the graph holds: the start points' transactions and those the expansions brought in,
    /// each once, in the order they came.
    /// </summary>
    public IReadOnlyList<Txid> Transactions =>
    [
        .. StartPoints.Where(start => start.Txid is not null).Select(start => start.Txid!)
            .Concat(Expansions.SelectMany(expansion => expansion.Transactions))
            .Distinct(),
    ];
}

/// <summary>A stored place and when it was set.</summary>
/// <param name="Position">The place.</param>
/// <param name="UpdatedUtc">When the user put it there.</param>
internal sealed record StoredPosition(NodePosition Position, DateTimeOffset UpdatedUtc);
