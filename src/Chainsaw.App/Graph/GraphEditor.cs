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
    /// Stores the splits of one breaking out, all or none, each sealed, under one line in the journal, so that the
    /// history can show and undo them as one (sketch AN1b).
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

    /// <summary>Links an Ethereum movement to the transaction where the money continues on another chain.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="movement">The movement that went into the bridge or swap.</param>
    /// <param name="target">The other chain's transaction.</param>
    /// <param name="reason">Why.</param>
    /// <returns>A task that completes when it is sealed.</returns>
    Task LinkCrossChainAsync(ICase @case, AccountSeed movement, ChainTransactionRef target, string reason);

    /// <summary>
    /// Reads what the inspector's <em>Historik</em> shows for a node (sketch AN1b): its labels, splits and links, also
    /// those taken back, its cluster, and who wrote each journal entry they name.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="node">The node.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>What was read.</returns>
    Task<NodeHistoryData> ReadHistoryAsync(ICase @case, GraphNode node, CancellationToken cancellationToken);

    /// <summary>
    /// Takes a label back, sealed with a line in the journal, and then lets the label library follow at once, so that
    /// the label leaves it and its copies in other cases are taken back there.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="sequence">The label's sequence.</param>
    /// <param name="reason">Why, or <see langword="null"/>.</param>
    /// <returns>Whether it was taken back, and what kept the library from following, if anything.</returns>
    Task<UndoOutcome> RetractLabelAsync(ICase @case, long sequence, string? reason);

    /// <summary>
    /// Marks a label <em>Sprid inte</em>, or lets it spread again, sealed with a line in the journal, and then lets
    /// the label library follow at once (sketch ET1a).
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="sequence">The label's sequence.</param>
    /// <param name="doNotSpread">Whether the label stays in the case.</param>
    /// <returns>Whether it was marked, and what kept the library from following, if anything.</returns>
    Task<UndoOutcome> SetDoNotSpreadAsync(ICase @case, long sequence, bool doNotSpread);

    /// <summary>Says what undoing a breaking out would merge again, without storing anything.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="splits">The breaking out's splits.</param>
    /// <param name="from">The address the user looks from.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The preview, or <see langword="null"/> when none of the splits exists.</returns>
    Task<UndoSplitPreview?> PreviewUndoSplitsAsync(
        ICase @case, IReadOnlyList<long> splits, BitcoinAddress? from, CancellationToken cancellationToken);

    /// <summary>Undoes a breaking out, all its splits under one line in the journal.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="splits">The splits.</param>
    /// <param name="reason">Why, or <see langword="null"/>.</param>
    /// <returns>Whether anything was undone.</returns>
    Task<UndoOutcome> UndoSplitsAsync(ICase @case, IReadOnlyList<long> splits, string? reason);

    /// <summary>Takes a link to another chain back, sealed with a line in the journal.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="link">The link.</param>
    /// <param name="reason">Why, or <see langword="null"/>.</param>
    /// <returns>Whether it was taken back.</returns>
    Task<UndoOutcome> UnlinkAsync(ICase @case, CrossChainLink link, string? reason);
}

/// <summary>How a taking back or an undoing went.</summary>
/// <param name="Done">
/// Whether it was stored; <see langword="false"/> when it had already been taken back (for example in another
/// window) or no longer exists.
/// </param>
/// <param name="LibraryProblem">
/// Why the label library could not follow yet (for example a locked vault), or <see langword="null"/>; it follows the
/// next time the case is opened or closed.
/// </param>
public sealed record UndoOutcome(bool Done, string? LibraryProblem = null);

/// <summary>How a breaking out went: the splits stored, and why they were not.</summary>
/// <param name="Stored">How many splits were stored: all, or none when the case refused the breaking out.</param>
/// <param name="Failure">Why nothing was stored, or <see langword="null"/> when every split was.</param>
public sealed record SplitOutcome(int Stored, Exception? Failure);

/// <summary>The inspector's way into the modules for its forms (<see cref="IGraphEditor"/>).</summary>
/// <param name="tracing">The links to other chains.</param>
/// <param name="attribution">The labels.</param>
/// <param name="clustering">The clusters and the splits.</param>
/// <param name="library">The label library, which follows a label taken back.</param>
public sealed class GraphEditor(
    ITracingService tracing,
    IAttributionService attribution,
    IClusteringService clustering,
    ILabelLibrary library) : IGraphEditor
{
    /// <summary>What the inspector says when the label library could not follow a label taken back.</summary>
    public const string LibraryLaterText =
        "Etikettbiblioteket kunde inte uppdateras nu; det följer med när ärendet öppnas eller stängs.";

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
            if (splits.Count == 0)
            {
                return new SplitOutcome(0, null);
            }

            try
            {
                // One breaking out is one decision: all its splits under one journal entry, undone as one.
                return new SplitOutcome(clustering.Split(@case, splits).Count, null);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return new SplitOutcome(0, exception);
            }
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

    /// <inheritdoc/>
    public Task LinkCrossChainAsync(ICase @case, AccountSeed movement, ChainTransactionRef target, string reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(movement);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(reason);
        return Task.Run(() => tracing.LinkCrossChain(@case, movement, target, reason));
    }

    /// <inheritdoc/>
    public Task<NodeHistoryData> ReadHistoryAsync(ICase @case, GraphNode node, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(node);
        return Task.Run(
            () =>
            {
                var cluster = node is { Kind: GraphNodeKind.Cluster, GraphAddresses: [var first, ..] }
                    ? clustering.GetCluster(@case, first)
                    : null;
                IReadOnlyCollection<BitcoinAddress> addresses = cluster is null
                    ? node.GraphAddresses
                    : [.. cluster.Addresses.Union(node.GraphAddresses)];
                var labels = node.Accounts.Count > 0
                    ? attribution.GetHistory(@case, node.Accounts)
                    : attribution.GetHistory(@case, addresses);
                var splits = node.Accounts.Count > 0
                    ? []
                    : WholeBreakingsOut(clustering.GetSplitHistory(@case), addresses.ToHashSet());
                var links = tracing.GetCrossChainHistory(@case);
                cancellationToken.ThrowIfCancellationRequested();
                var operators = new Dictionary<long, string>();
                foreach (var sequence in NodeHistoryData.JournalNeeds(labels, splits, links))
                {
                    if (@case.ReadJournal(sequence, 1) is [var entry] && entry.Sequence == sequence)
                    {
                        operators[sequence] = entry.Operator.Name;
                    }
                }

                return new NodeHistoryData(labels, splits, links, cluster, operators);
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<UndoOutcome> RetractLabelAsync(ICase @case, long sequence, string? reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return Task.Run(() => attribution.Retract(@case, sequence, reason)
            ? FollowLibrary(@case)
            : new UndoOutcome(false));
    }

    /// <inheritdoc/>
    public Task<UndoOutcome> SetDoNotSpreadAsync(ICase @case, long sequence, bool doNotSpread)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return Task.Run(() => attribution.SetDoNotSpread(@case, sequence, doNotSpread)
            ? FollowLibrary(@case)
            : new UndoOutcome(false));
    }

    /// <summary>
    /// Lets the label library follow a change to a label that is already sealed: the case is absorbed again.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <returns>Done, with what kept the library from following, if anything.</returns>
    private UndoOutcome FollowLibrary(ICase @case)
    {
        try
        {
            library.Absorb(@case);
            return new UndoOutcome(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The change is sealed; the library learns it the next time the case is absorbed.
            return new UndoOutcome(true, LibraryLaterText);
        }
    }

    /// <inheritdoc/>
    public Task<UndoSplitPreview?> PreviewUndoSplitsAsync(
        ICase @case, IReadOnlyList<long> splits, BitcoinAddress? from, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(splits);
        return Task.Run(() => clustering.PreviewUndoSplits(@case, splits, from), cancellationToken);
    }

    /// <inheritdoc/>
    public Task<UndoOutcome> UndoSplitsAsync(ICase @case, IReadOnlyList<long> splits, string? reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(splits);
        return Task.Run(() => new UndoOutcome(clustering.UndoSplits(@case, splits, reason) > 0));
    }

    /// <inheritdoc/>
    public Task<UndoOutcome> UnlinkAsync(ICase @case, CrossChainLink link, string? reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(link);
        return Task.Run(() => new UndoOutcome(tracing.UnlinkCrossChain(@case, link, reason)));
    }

    /// <summary>
    /// Gives the whole breakings out (the splits stored under the same journal entry) that touch a node's addresses,
    /// so that a breaking out is shown and undone as one from any of its addresses.
    /// </summary>
    /// <param name="all">Every split in the case, read once.</param>
    /// <param name="addresses">The node's addresses.</param>
    /// <returns>Every split of those breakings out, oldest first.</returns>
    private static IReadOnlyList<SplitHistoryEntry> WholeBreakingsOut(
        IReadOnlyList<SplitHistoryEntry> all, IReadOnlySet<BitcoinAddress> addresses)
    {
        var journals = all
            .Where(entry => addresses.Contains(entry.Split.First) || addresses.Contains(entry.Split.Second))
            .Select(entry => entry.Split.JournalSequence)
            .ToHashSet();
        return [.. all.Where(entry => journals.Contains(entry.Split.JournalSequence))];
    }
}
