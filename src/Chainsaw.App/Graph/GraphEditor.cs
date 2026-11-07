using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>
/// Where the inspector's forms (sketch IN1a) write in the case: a label through Attribution, a split through
/// Clustering and a link to another chain through Tracing. Each module seals what it writes and adds a line to the
/// journal (principle 1); the graph is then read again from the case. Every method works off the UI thread
/// (principle 6).
/// </summary>
public interface IGraphEditor
{
    /// <summary>Reads the cluster an address belongs to, with why each member is there.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The cluster.</returns>
    Task<Cluster> ReadClusterAsync(ICase @case, BitcoinAddress address, CancellationToken cancellationToken);

    /// <summary>Sets a label, sealed with a line in the journal.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="draft">The label.</param>
    /// <returns>A task that completes when it is stored.</returns>
    Task AddLabelAsync(ICase @case, LabelDraft draft);

    /// <summary>
    /// Stores the user's splits one after the other, each sealed with a line in the journal, and stops at the first
    /// that fails; those stored before it hold, since each is a decision of its own.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="splits">The splits.</param>
    /// <returns>How many were stored, and the failure that stopped the rest, if any.</returns>
    Task<SplitOutcome> SplitAsync(ICase @case, IReadOnlyList<SplitRequest> splits);

    /// <summary>Links a coin to the transaction where the money continues on another chain, sealed.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="coin">The coin that went into the bridge or swap.</param>
    /// <param name="target">The transaction on the other chain.</param>
    /// <param name="reason">Why.</param>
    /// <returns>A task that completes when it is stored.</returns>
    Task LinkCrossChainAsync(ICase @case, Outpoint coin, ChainTransactionRef target, string reason);
}

/// <summary>How a breaking out went: the splits stored, and what stopped the rest.</summary>
/// <param name="Stored">How many splits were stored.</param>
/// <param name="Failure">What stopped the rest, or <see langword="null"/> when every split was stored.</param>
public sealed record SplitOutcome(int Stored, Exception? Failure);

/// <summary>The inspector's way into the modules for its forms (<see cref="IGraphEditor"/>).</summary>
/// <param name="tracing">The links to other chains.</param>
/// <param name="attribution">The labels.</param>
/// <param name="clustering">The clusters and the splits.</param>
public sealed class GraphEditor(
    ITracingService tracing, IAttributionService attribution, IClusteringService clustering) : IGraphEditor
{
    /// <inheritdoc/>
    public Task<Cluster> ReadClusterAsync(ICase @case, BitcoinAddress address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(address);
        return Task.Run(() => clustering.GetCluster(@case, address), cancellationToken);
    }

    /// <inheritdoc/>
    public Task AddLabelAsync(ICase @case, LabelDraft draft)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(draft);
        return Task.Run(() => attribution.Add(@case, draft));
    }

    /// <inheritdoc/>
    public Task<SplitOutcome> SplitAsync(ICase @case, IReadOnlyList<SplitRequest> splits)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(splits);
        return Task.Run(() =>
        {
            var stored = 0;
            foreach (var split in splits)
            {
                try
                {
                    clustering.Split(@case, split);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    return new SplitOutcome(stored, exception);
                }

                stored++;
            }

            return new SplitOutcome(stored, null);
        });
    }

    /// <inheritdoc/>
    public Task LinkCrossChainAsync(ICase @case, Outpoint coin, ChainTransactionRef target, string reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(coin);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(reason);
        return Task.Run(() => tracing.LinkCrossChain(@case, coin, target, reason));
    }
}
