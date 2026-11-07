using System.Globalization;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>What an expansion brought, and the graph after it.</summary>
/// <param name="Scene">The graph after the expansion.</param>
/// <param name="Outcome">How it went, over every lookup it made.</param>
/// <param name="NewTransactions">How many transactions came into the graph.</param>
/// <param name="Problems">What went wrong, in Swedish; empty when nothing did.</param>
public sealed record GraphExpansion(
    GraphScene Scene, ExpansionOutcome Outcome, int NewTransactions, IReadOnlyList<string> Problems);

/// <summary>
/// Where the graph view reads the case and asks for more: Tracing, Attribution and Clustering through their
/// contracts. Every method does its work off the UI thread (principle 6).
/// </summary>
public interface IGraphSource
{
    /// <summary>Reads the graph from the case, with its labels and the trace by a method.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="method">The tracing method.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The scene.</returns>
    Task<GraphScene> LoadAsync(ICase @case, TracingMethodKind method, CancellationToken cancellationToken);

    /// <summary>Expands the graph by one step for each lookup, one after the other, and reads it again.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="requests">The lookups.</param>
    /// <param name="method">The tracing method.</param>
    /// <param name="cancellationToken">Stops the work between lookups.</param>
    /// <returns>What the expansion brought.</returns>
    Task<GraphExpansion> ExpandAsync(
        ICase @case,
        IReadOnlyList<ExpansionRequest> requests,
        TracingMethodKind method,
        CancellationToken cancellationToken);

    /// <summary>Explains a cluster node: which heuristic merged which addresses, from which transaction.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="node">The node; anything but a cluster gives nothing.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The answers to <em>Varför?</em>.</returns>
    Task<IReadOnlyList<WhyLine>> ExplainClusterAsync(ICase @case, GraphNode node, CancellationToken cancellationToken);

    /// <summary>Remembers where the user put a node in a layout.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="nodeKey">The node.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="place">The place.</param>
    /// <returns>A task that completes when it is stored.</returns>
    Task SavePositionAsync(ICase @case, string nodeKey, GraphLayout layout, GraphPoint place);

    /// <summary>Reads how the user left the graph in the case: method, layout, amounts and the part on screen.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The view; the defaults when none is stored.</returns>
    Task<GraphViewState> LoadViewAsync(ICase @case, CancellationToken cancellationToken);

    /// <summary>Stores how the user left the graph; a change of method gets a line in the journal.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="view">The view.</param>
    /// <returns>A task that completes when it is stored.</returns>
    Task SaveViewAsync(ICase @case, GraphViewState view);
}

/// <summary>The graph view's way into the modules (<see cref="IGraphSource"/>).</summary>
/// <param name="tracing">The graph and the trace.</param>
/// <param name="attribution">The labels.</param>
/// <param name="clustering">The clusters, for <em>Varför?</em>.</param>
/// <param name="library">
/// The label library, which gives the graph's addresses the labels set in other cases (CLAUDE.md, <em>Etiketter
/// smittar</em>); <see langword="null"/> where there is none.
/// </param>
public sealed class GraphSource(
    ITracingService tracing,
    IAttributionService attribution,
    IClusteringService clustering,
    ILabelLibrary? library = null) : IGraphSource
{
    /// <summary>The most links of a cluster that <em>Varför?</em> writes out.</summary>
    public const int MostClusterLinks = 12;

    /// <summary>What the trace says when the seeds cannot be traced.</summary>
    public const string TraceFailedText =
        "Spårningen kunde inte räknas från startpunkternas mynt; grafen visas utan spårade belopp.";

    /// <summary>
    /// The most transactions one expansion brings in over all its lookups, so that a cluster with many addresses does
    /// not flood the graph (principle 7); the rest is left for the next press, and the expansion says so.
    /// </summary>
    public const int MostNewTransactions = 100;

    /// <summary>The most lookups one expansion makes, so that a large cluster is not asked address by address.</summary>
    public const int MostLookups = 10;

    /// <summary>
    /// What an expansion says when it stopped at <see cref="MostNewTransactions"/> or <see cref="MostLookups"/>.
    /// </summary>
    public const string LimitReachedText =
        "Gränsen för en expansion nåddes; expandera igen för resten.";

    /// <summary>What the trace says when the graph does not yet show a coin to start in.</summary>
    public const string NothingToTraceText =
        "Inget att spåra än: startpunktens mynt finns inte i grafen. Välj startpunkten och expandera den.";

    /// <inheritdoc/>
    public Task<GraphScene> LoadAsync(ICase @case, TracingMethodKind method, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return Task.Run(
            async () =>
            {
                var graph = await tracing.LoadAsync(@case, cancellationToken).ConfigureAwait(false);
                graph = await WithSharedLabelsAsync(@case, graph, cancellationToken).ConfigureAwait(false);
                return await ComposeAsync(@case, graph, method, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<GraphExpansion> ExpandAsync(
        ICase @case,
        IReadOnlyList<ExpansionRequest> requests,
        TracingMethodKind method,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(requests);
        return Task.Run(
            async () =>
            {
                var outcomes = new List<ExpansionOutcome>();
                var problems = new List<string>();
                var added = 0;
                TraceGraph? graph = null;
                foreach (var request in requests)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (added >= MostNewTransactions || outcomes.Count >= MostLookups)
                    {
                        outcomes.Add(ExpansionOutcome.NotExpanded);
                        problems.Add(LimitReachedText);
                        break;
                    }

                    var result = await tracing.ExpandAsync(@case, request, cancellationToken).ConfigureAwait(false);
                    outcomes.Add(result.Outcome);
                    added += result.NewTransactions.Count;
                    graph = result.Graph;
                    if (result.Problem is { } problem && !problems.Contains(problem))
                    {
                        problems.Add(problem);
                    }
                }

                graph ??= await tracing.LoadAsync(@case, cancellationToken).ConfigureAwait(false);
                graph = await WithSharedLabelsAsync(@case, graph, cancellationToken).ConfigureAwait(false);
                var scene = await ComposeAsync(@case, graph, method, cancellationToken).ConfigureAwait(false);
                return new GraphExpansion(scene, Combine(outcomes), added, problems);
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<WhyLine>> ExplainClusterAsync(
        ICase @case, GraphNode node, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(node);
        if (node.Kind != GraphNodeKind.Cluster || node.GraphAddresses.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<WhyLine>>([]);
        }

        return Task.Run(() => Explain(clustering.GetCluster(@case, node.GraphAddresses[0])), cancellationToken);
    }

    /// <inheritdoc/>
    public Task SavePositionAsync(ICase @case, string nodeKey, GraphLayout layout, GraphPoint place)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return Task.Run(() => tracing.SavePosition(@case, nodeKey, layout, new NodePosition(place.X, place.Y)));
    }

    /// <inheritdoc/>
    public Task<GraphViewState> LoadViewAsync(ICase @case, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return Task.Run(() => tracing.GetView(@case), cancellationToken);
    }

    /// <inheritdoc/>
    public Task SaveViewAsync(ICase @case, GraphViewState view)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(view);
        return Task.Run(() => tracing.SaveView(@case, view));
    }

    /// <summary>
    /// Gives the graph's addresses the label library's labels from other cases, sealed in this case with a line in
    /// the journal, and reads the graph again when any came in, so that its ends of the trail include them. Without a
    /// library, with a locked vault, without an operator profile or with a library that cannot be read, the graph
    /// stays as it is: the labels come the next time it is read.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="graph">The graph.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The graph, read again when labels came in.</returns>
    private async Task<TraceGraph> WithSharedLabelsAsync(
        ICase @case, TraceGraph graph, CancellationToken cancellationToken)
    {
        var addresses = graph.Nodes.SelectMany(node => node.GraphAddresses).Distinct().ToList();
        if (library is null || addresses.Count == 0)
        {
            return graph;
        }

        try
        {
            if (library.ShareInto(@case, addresses).Count == 0)
            {
                return graph;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not OperationCanceledException)
        {
            // A locked vault, no operator profile or a library that cannot be read must never keep the case's own
            // graph from being shown; the labels from other cases come the next time it can be read.
            return graph;
        }

        return await tracing.LoadAsync(@case, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gives what a whole expansion came to: expanded when every lookup was, not expanded when none was, otherwise
    /// partly.
    /// </summary>
    /// <param name="outcomes">Each lookup's outcome.</param>
    /// <returns>The whole's outcome.</returns>
    public static ExpansionOutcome Combine(IReadOnlyList<ExpansionOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        if (outcomes.Count == 0 || outcomes.All(outcome => outcome == ExpansionOutcome.NotExpanded))
        {
            return ExpansionOutcome.NotExpanded;
        }

        return outcomes.All(outcome => outcome == ExpansionOutcome.Expanded)
            ? ExpansionOutcome.Expanded
            : ExpansionOutcome.Partly;
    }

    /// <summary>Explains a cluster: that it is a conclusion, and each link that merged it.</summary>
    /// <param name="cluster">The cluster.</param>
    /// <returns>The answers.</returns>
    public static IReadOnlyList<WhyLine> Explain(Cluster cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);
        var why = new List<WhyLine>
        {
            new(
                "Klustret är klustringens slutsats, inte ett faktum: "
                + cluster.Members.Count.ToString(CultureInfo.InvariantCulture)
                + " adresser i ärendet har enligt heuristikerna samma ägare.",
                null),
        };
        var links = cluster.Links.DistinctBy(link => link.Evidence.Sequence).ToList();
        foreach (var link in links.Take(MostClusterLinks))
        {
            var evidence = link.Evidence;
            var adjusted = evidence.Adjustment is { } adjustment ? $" {adjustment}" : string.Empty;
            why.Add(new WhyLine(
                $"{evidence.Heuristic}, tx {GraphScene.Shorten(evidence.Txid.Value)}: {evidence.Reason}{adjusted}",
                GuideTopics.ForHeuristic(evidence.Heuristic.Name)));
        }

        if (links.Count > MostClusterLinks)
        {
            why.Add(new WhyLine(
                string.Create(CultureInfo.InvariantCulture, $"och {links.Count - MostClusterLinks} länkar till."),
                null));
        }

        if (cluster.Splits.Count > 0)
        {
            why.Add(new WhyLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Användaren har brutit upp {cluster.Splits.Count} sammanslagningar som rör klustret."),
                null));
        }

        return why;
    }

    /// <summary>
    /// Gives the scene for a graph: its labels (read once for all its addresses), the seeds and the trace by the
    /// method, counted off the calling thread and stopped when the work is.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="graph">The graph.</param>
    /// <param name="method">The tracing method.</param>
    /// <param name="cancellationToken">Stops the trace.</param>
    /// <returns>The scene.</returns>
    private async Task<GraphScene> ComposeAsync(
        ICase @case, TraceGraph graph, TracingMethodKind method, CancellationToken cancellationToken)
    {
        var byAddress = attribution.GetLabels(
            @case, graph.Nodes.SelectMany(node => node.GraphAddresses).Distinct().ToList());
        var labels = new Dictionary<string, IReadOnlyList<Label>>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes.Where(node => node.GraphAddresses.Count > 0))
        {
            var found = node.GraphAddresses
                .SelectMany(address => byAddress.GetValueOrDefault(address) ?? [])
                .DistinctBy(label => (label.Subject, label.Kind, label.Name))
                .OrderByDescending(label => label.IndependentOrigins)
                .ToList();
            if (found.Count > 0)
            {
                labels[node.Key] = found;
            }
        }

        var seeds = SeedPicker.Pick(graph);
        TraceResult? trace = null;
        var problem = seeds.Count == 0 && graph.StartPoints.Count > 0 ? NothingToTraceText : null;
        if (seeds.Count > 0)
        {
            try
            {
                trace = await tracing.TraceAsync(graph, new TraceRequest(method, seeds), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                // The seeds do not hold together (for example a coin whose amount the case lacks), or the trace
                // refused its own result (a method that would create or lose money, transactions in a ring); the
                // graph is still shown, honestly without traced amounts.
                problem = TraceFailedText;
            }
        }

        return GraphScene.Build(new GraphSnapshot(graph, labels, method, seeds, trace, problem));
    }
}
