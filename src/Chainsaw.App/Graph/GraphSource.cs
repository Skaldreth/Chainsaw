using System.Globalization;
using System.Runtime.CompilerServices;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

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
    /// <param name="reading">The tracing method and whether the outputs that can never be spent are drawn.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The scene.</returns>
    Task<GraphScene> LoadAsync(ICase @case, GraphReading reading, CancellationToken cancellationToken);

    /// <summary>
    /// Values the scene's lines again, asking the source through the exit for at most
    /// <see cref="GraphSource.MostRateLookups"/> rates the case lacks (sketch KU1a), off the UI thread, so that the
    /// graph is shown at once with the case's rates and the rest come after. By default nothing is asked.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="scene">The scene shown.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The lines' values and why some have none.</returns>
    Task<GraphRates> FillRatesAsync(ICase @case, GraphScene scene, CancellationToken cancellationToken) =>
        Task.FromResult(new GraphRates(scene.Snapshot.Values, scene.Snapshot.ValueGaps, scene.Snapshot.Rates));

    /// <summary>Expands the graph by one step for each lookup, one after the other, and reads it again.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="requests">The lookups.</param>
    /// <param name="reading">The tracing method and whether the outputs that can never be spent are drawn.</param>
    /// <param name="cancellationToken">Stops the work between lookups.</param>
    /// <returns>What the expansion brought.</returns>
    Task<GraphExpansion> ExpandAsync(
        ICase @case,
        IReadOnlyList<ExpansionRequest> requests,
        GraphReading reading,
        CancellationToken cancellationToken);

    /// <summary>Explains a cluster node: which heuristic merged which addresses, from which transaction.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="node">The node; anything but a cluster gives nothing.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The answers to <em>Varför?</em>.</returns>
    Task<IReadOnlyList<WhyLine>> ExplainClusterAsync(ICase @case, GraphNode node, CancellationToken cancellationToken);

    /// <summary>
    /// Reads what an address's expansions held back (sketch EX1) with what the case knows about each counterparty
    /// (its label, whether it is in the graph), for the selection view.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="clue">The clue, whose <see cref="HeldBackClue.Addresses"/> are read, every one.</param>
    /// <param name="scene">The graph as it is drawn.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The choices.</returns>
    Task<HeldBackChoices> ListHeldBackAsync(
        ICase @case, HeldBackClue clue, GraphScene scene, CancellationToken cancellationToken);

    /// <summary>
    /// Brings chosen held-back transactions into the graph (sketch EX2), each into the expansion of the address it was
    /// read at, and reads it again.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="picks">The chosen transactions per expanded address.</param>
    /// <param name="reading">The tracing method and whether the outputs that can never be spent are drawn.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>What came in.</returns>
    Task<GraphExpansion> TakeHeldBackAsync(
        ICase @case,
        IReadOnlyDictionary<BitcoinAddress, IReadOnlyList<Txid>> picks,
        GraphReading reading,
        CancellationToken cancellationToken);

    /// <summary>Remembers where the user put a node in a layout.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="nodeKey">The node.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="place">The place.</param>
    /// <returns>A task that completes when it is stored.</returns>
    Task SavePositionAsync(ICase @case, string nodeKey, GraphLayout layout, GraphPoint place);

    /// <summary>Remembers where many nodes stand in a layout, in one write, as when the graph is sorted.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="places">The places by node.</param>
    /// <returns>A task that completes when they are stored.</returns>
    Task SavePositionsAsync(ICase @case, GraphLayout layout, IReadOnlyDictionary<string, GraphPoint> places);

    /// <summary>Picks actors out of their piles (sketch EX2b) and stores it in the case.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="nodeKeys">The actors.</param>
    /// <returns>A task that completes when it is stored.</returns>
    Task PickFromPileAsync(ICase @case, IReadOnlyCollection<string> nodeKeys);

    /// <summary>Puts actors that were picked out back in their piles, and stores it in the case.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="nodeKeys">The actors.</param>
    /// <returns>A task that completes when it is stored.</returns>
    Task ReturnToPileAsync(ICase @case, IReadOnlyCollection<string> nodeKeys);

    /// <summary>Reads how the user left the graph: method, layout, amounts and the part on screen.</summary>
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
/// <param name="prices">
/// The rates, which give the lines their value in the chosen currency at the time (sketches TK1a and KU1a);
/// <see langword="null"/> where there are none, and the lines then show no value.
/// </param>
/// <param name="priceDefaults">
/// The currency chosen in the settings, which a line without a value names (<c>– kr</c>); kronor where there is none.
/// </param>
/// <param name="clock">The clock, which tells a block time ahead of it; the computer's where none is given.</param>
public sealed class GraphSource(
    ITracingService tracing,
    IAttributionService attribution,
    IClusteringService clustering,
    ILabelLibrary? library = null,
    IPriceService? prices = null,
    IPriceDefaults? priceDefaults = null,
    IClock? clock = null) : IGraphSource
{
    /// <summary>The computer's clock, where none is given.</summary>
    private static readonly IClock SystemClockInstance = new SystemClock();

    /// <summary>
    /// The most rates one expansion asks the source for, so that a long history does not flood Pricing's source; the
    /// rest are asked for at the next expansion, and until then their lines show no value.
    /// </summary>
    public const int MostRateLookups = 30;

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

    /// <summary>What the trace says when the movements on Ethereum cannot be traced.</summary>
    public const string AccountTraceFailedText =
        "Spårningen på Ethereum kunde inte räknas: startpunkternas rörelser ligger i varandras flöde, så samma pengar "
        + "skulle räknas två gånger. Ethereums linjer visas utan spårade belopp.";

    /// <summary>What the trace says when the graph does not yet show a movement on Ethereum to start in.</summary>
    public const string NothingToTraceOnEthereumText =
        "Inget att spåra än: grafen visar ingen rörelse till startpunkten. Välj startpunkten och expandera den.";

    /// <summary>What the trace says when the graph does not yet show a coin to start in.</summary>
    public const string NothingToTraceText =
        "Inget att spåra än: startpunktens mynt finns inte i grafen. Välj startpunkten och expandera den.";

    /// <inheritdoc/>
    public Task<GraphRates> FillRatesAsync(ICase @case, GraphScene scene, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(scene);
        return Task.Run(
            async () =>
            {
                var latest = new Dictionary<Asset, Price>();
                var throttled = new StrongBox<bool>();
                var (values, gaps) = await ValuesAsync(
                    @case, scene.Snapshot.Graph, PriceLookupMode.AsNeeded, latest, cancellationToken, throttled)
                    .ConfigureAwait(false);
                return new GraphRates(values, gaps, latest) { IsThrottled = throttled.Value };
            },
            cancellationToken);
    }

    /// <summary>Gets the time now, which tells a block time ahead of the clock from a missing source.</summary>
    private DateTimeOffset Now => (clock ?? SystemClockInstance).UtcNow;

    /// <summary>Gets the code of the currency the lines are valued in, for example <c>SEK</c>.</summary>
    private string Currency => (priceDefaults?.Currency ?? FiatCurrency.Sek).Code();

    /// <inheritdoc/>
    public Task<GraphScene> LoadAsync(ICase @case, GraphReading reading, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(reading);
        return Task.Run(
            async () =>
            {
                var graph = await tracing.LoadAsync(@case, cancellationToken).ConfigureAwait(false);
                graph = await WithSharedLabelsAsync(@case, graph, cancellationToken).ConfigureAwait(false);
                return await ComposeAsync(@case, graph, reading, cancellationToken)
                    .ConfigureAwait(false);
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<GraphExpansion> ExpandAsync(
        ICase @case,
        IReadOnlyList<ExpansionRequest> requests,
        GraphReading reading,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(reading);
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
                    added += result.NewCount;
                    graph = result.Graph;
                    if (result.Problem is { } problem && !problems.Contains(problem))
                    {
                        problems.Add(problem);
                    }
                }

                graph ??= await tracing.LoadAsync(@case, cancellationToken).ConfigureAwait(false);
                graph = await WithSharedLabelsAsync(@case, graph, cancellationToken).ConfigureAwait(false);
                var scene = await ComposeAsync(@case, graph, reading, cancellationToken)
                    .ConfigureAwait(false);
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
    public Task<HeldBackChoices> ListHeldBackAsync(
        ICase @case, HeldBackClue clue, GraphScene scene, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(clue);
        ArgumentNullException.ThrowIfNull(scene);
        if (clue.Addresses.Count == 0)
        {
            throw new ArgumentException("Only an address's clue has a history to read.", nameof(clue));
        }

        var around = HeldBackChoices.ArrivalAt(scene, clue.Anchor);
        return Task.Run(
            async () =>
            {
                var lists = new List<HeldBackList>();
                foreach (var address in clue.Addresses)
                {
                    lists.Add(await tracing.ListHeldBackAsync(@case, address, around, cancellationToken)
                        .ConfigureAwait(false));
                }

                var counterparties = lists.SelectMany(list => list.Transactions)
                    .SelectMany(HeldBackChoices.PartiesOf).Distinct().ToList();
                return HeldBackChoices.Build(clue, lists, LabelsOf(@case, counterparties), scene);
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<GraphExpansion> TakeHeldBackAsync(
        ICase @case,
        IReadOnlyDictionary<BitcoinAddress, IReadOnlyList<Txid>> picks,
        GraphReading reading,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(picks);
        return Task.Run(
            async () =>
            {
                var outcomes = new List<ExpansionOutcome>();
                var problems = new List<string>();
                var added = 0;
                TraceGraph? graph = null;
                foreach (var (address, transactions) in picks)
                {
                    var result = await tracing.TakeHeldBackAsync(@case, address, [.. transactions], cancellationToken)
                        .ConfigureAwait(false);
                    outcomes.Add(result.Outcome);
                    added += result.NewCount;
                    graph = result.Graph;
                    if (result.Problem is { } problem && !problems.Contains(problem))
                    {
                        problems.Add(problem);
                    }
                }

                graph ??= await tracing.LoadAsync(@case, cancellationToken).ConfigureAwait(false);
                graph = await WithSharedLabelsAsync(@case, graph, cancellationToken).ConfigureAwait(false);
                var scene = await ComposeAsync(@case, graph, reading, cancellationToken)
                    .ConfigureAwait(false);
                return new GraphExpansion(scene, Combine(outcomes), added, problems);
            },
            cancellationToken);
    }

    /// <summary>
    /// Reads the labels the case has for counterparties. A failure is not hidden: a mixer drawn as unknown would say
    /// something the case does not, so the view does not open and says why.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="addresses">The counterparties.</param>
    /// <returns>The labels by address.</returns>
    private IReadOnlyDictionary<BitcoinAddress, IReadOnlyList<Label>> LabelsOf(
        ICase @case, IReadOnlyCollection<BitcoinAddress> addresses) => addresses.Count == 0
        ? new Dictionary<BitcoinAddress, IReadOnlyList<Label>>()
        : attribution.GetLabels(@case, addresses);

    /// <inheritdoc/>
    public Task SavePositionAsync(ICase @case, string nodeKey, GraphLayout layout, GraphPoint place)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return Task.Run(() => tracing.SavePosition(@case, nodeKey, layout, new NodePosition(place.X, place.Y)));
    }

    /// <inheritdoc/>
    public Task SavePositionsAsync(ICase @case, GraphLayout layout, IReadOnlyDictionary<string, GraphPoint> places)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(places);
        var positions = places.ToDictionary(
            pair => pair.Key, pair => new NodePosition(pair.Value.X, pair.Value.Y), StringComparer.Ordinal);
        return Task.Run(() => tracing.SavePositions(@case, layout, positions));
    }

    /// <inheritdoc/>
    public Task PickFromPileAsync(ICase @case, IReadOnlyCollection<string> nodeKeys)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return Task.Run(() => tracing.PickFromPile(@case, nodeKeys));
    }

    /// <inheritdoc/>
    public Task ReturnToPileAsync(ICase @case, IReadOnlyCollection<string> nodeKeys)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return Task.Run(() => tracing.ReturnToPile(@case, nodeKeys));
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
    /// the journal, and reads the graph again when any came in or the library took back copies whose label was
    /// withdrawn where it was set, so that its ends of the trail follow them. Without a library, with a locked vault,
    /// without an operator profile or with a library that cannot be read, the graph stays as it is: the labels come
    /// the next time it is read.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="graph">The graph.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The graph, read again when the case's labels changed.</returns>
    private async Task<TraceGraph> WithSharedLabelsAsync(
        ICase @case, TraceGraph graph, CancellationToken cancellationToken)
    {
        var addresses = graph.Nodes.SelectMany(node => node.GraphAddresses).Distinct().ToList();
        var accounts = graph.Nodes.SelectMany(node => node.Accounts).Distinct().ToList();
        if (library is null || addresses.Count + accounts.Count == 0)
        {
            return graph;
        }

        try
        {
            // ShareInto counts only the labels that came in; a copy it took back (its label was withdrawn where it
            // was set) shows as one label fewer in force, and the graph read before still ends the trail on it.
            var before = attribution.GetEntries(@case).Count;
            var shared = addresses.Count == 0 ? 0 : library.ShareInto(@case, addresses).Count;
            shared += accounts.Count == 0 ? 0 : library.ShareInto(@case, accounts).Count;
            if (shared == 0 && attribution.GetEntries(@case).Count == before)
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

    /// <summary>
    /// Says who merged a cluster, as its explanation writes it: the heuristics, and the user's judgement when a merge
    /// of the user's own (<c>user v1</c>) holds it together.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    /// <returns>For example <c>heuristikerna och användarens bedömning</c>.</returns>
    private static string WhoMerged(Cluster cluster)
    {
        var users = cluster.Links.Any(link => link.Evidence.Heuristic.Name == UserMerges.Name);
        var heuristics = cluster.Links.Any(link => link.Evidence.Heuristic.Name != UserMerges.Name);
        return (users, heuristics) switch
        {
            (true, true) => "heuristikerna och användarens bedömning",
            (true, false) => "användarens bedömning",
            _ => "heuristikerna",
        };
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
                + " adresser i ärendet har enligt " + WhoMerged(cluster) + " samma ägare.",
                null),
        };
        var links = cluster.Links.DistinctBy(link => (link.Evidence.Heuristic.Name, link.Evidence.Sequence)).ToList();
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

        // One breaking out stores a split per address that left, so the breakings out are counted, not the splits.
        var breakings = cluster.Splits.Select(split => split.JournalSequence).Distinct().Count();
        if (breakings > 0)
        {
            why.Add(new WhyLine(
                breakings == 1
                    ? "Användaren har gjort en uppbrytning som rör klustret."
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"Användaren har gjort {breakings} uppbrytningar som rör klustret."),
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
    /// <param name="reading">The tracing method and whether the outputs that can never be spent are drawn.</param>
    /// <param name="cancellationToken">Stops the trace.</param>
    /// <returns>The scene.</returns>
    private async Task<GraphScene> ComposeAsync(
        ICase @case,
        TraceGraph graph,
        GraphReading reading,
        CancellationToken cancellationToken)
    {
        var method = reading.Method;
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

        AddAccountLabels(@case, graph, labels);

        var seeds = SeedPicker.Pick(graph);
        var accountSeeds = AccountSeedPicker.Pick(graph);
        TraceResult? trace = null;
        AccountTraceResult? accountTrace = null;
        string? problem = null;
        if (seeds.Count + accountSeeds.Count == 0 && graph.StartPoints.Count > 0)
        {
            problem = graph.StartPoints.All(start => start.Chain == Chain.Ethereum)
                ? NothingToTraceOnEthereumText
                : NothingToTraceText;
        }

        if (accountSeeds.Count > 0)
        {
            try
            {
                accountTrace = await tracing.TraceAccountsAsync(
                    graph, new AccountTraceRequest(method, accountSeeds), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                // The seeds lie in each other's flow, or a method broke its contract; shown without traced amounts.
                problem = AccountTraceFailedText;
            }
        }

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
                problem = problem is null ? TraceFailedText : problem + " " + TraceFailedText;
            }
        }

        var latest = new Dictionary<Asset, Price>();
        var (values, gaps) = await ValuesAsync(@case, graph, PriceLookupMode.CaseOnly, latest, cancellationToken)
            .ConfigureAwait(false);
        return GraphScene.Build(new GraphSnapshot(graph, labels, method, seeds, trace, problem)
        {
            AccountSeeds = accountSeeds,
            AccountTrace = accountTrace,
            Values = values,
            ValueGaps = gaps,
            Rates = latest,
            KeptOutOfPiles = reading.KeptOutOfPiles,
        }, reading.ShowUnspendable);
    }

    /// <summary>Adds the labels on the graph's Ethereum addresses, read once for all of them.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="graph">The graph.</param>
    /// <param name="labels">The labels by node key, added to.</param>
    private void AddAccountLabels(ICase @case, TraceGraph graph, Dictionary<string, IReadOnlyList<Label>> labels)
    {
        var accounts = graph.Nodes.Where(node => node.Accounts.Count > 0).ToList();
        if (accounts.Count == 0)
        {
            return;
        }

        var byAccount = attribution.GetLabels(@case, accounts.SelectMany(node => node.Accounts).Distinct().ToList());
        foreach (var node in accounts)
        {
            var found = node.Accounts.SelectMany(account => byAccount.GetValueOrDefault(account) ?? [])
                .OrderByDescending(label => label.IndependentOrigins)
                .ToList();
            if (found.Count > 0)
            {
                labels[node.Key] = found;
            }
        }
    }

    /// <summary>
    /// Values the lines at the time of their transfers, in the currency Pricing is set to (sketches TK1a and KU1a):
    /// each transfer at its block's time, by the rate of its asset (bitcoin, ether, or USDT and USDC by contract). A
    /// line gets a value only when every transfer on it has a rate; otherwise it gets the reason, never a guess.
    /// The case is always asked first; with <see cref="PriceLookupMode.AsNeeded"/> only a rate the case lacks is
    /// asked of the source, at most <see cref="MostRateLookups"/> of them, so that rates the case has never count.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="graph">The graph.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="latest">
    /// Filled with the latest rate found per asset, which values what is left at an address (sketch TK1a).
    /// </param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <param name="throttled">
    /// Set when a source asked Chainsaw to wait (429): from then on no rate is asked of the source in this run, so that
    /// a refusing source is asked once and not once per line; <see langword="null"/> when it does not matter.
    /// </param>
    /// <returns>The values and the reasons a line has none, by line key.</returns>
    private async Task<(IReadOnlyDictionary<string, EdgeValue> Values, IReadOnlyDictionary<string, EdgeValueGap> Gaps)>
        ValuesAsync(
            ICase @case,
            TraceGraph graph,
            PriceLookupMode mode,
            Dictionary<Asset, Price> latest,
            CancellationToken cancellationToken,
            StrongBox<bool>? throttled = null)
    {
        var values = new Dictionary<string, EdgeValue>(StringComparer.Ordinal);
        var gaps = new Dictionary<string, EdgeValueGap>(StringComparer.Ordinal);
        if (prices is null)
        {
            return (values, gaps);
        }

        var asked = 0;
        var currency = Currency;
        var known = new Dictionary<(Asset, DateTimeOffset), PriceLookup<Price>>();
        foreach (var edge in graph.Edges.Where(edge => edge.Kind == GraphEdgeKind.Transfer && edge.Transfers.Count > 0))
        {
            var parts = new List<EdgeValuePart>();
            EdgeValueGap? gap = null;
            foreach (var transfer in edge.Transfers)
            {
                if (transfer.Time is not { } time)
                {
                    gap = EdgeValues.Unconfirmed(currency);
                    break;
                }

                var key = (transfer.Amount.Asset, time);
                if (!known.TryGetValue(key, out var lookup))
                {
                    var asset = transfer.Amount.Asset;
                    lookup = await LookUp(@case, asset, time, PriceLookupMode.CaseOnly, cancellationToken)
                        .ConfigureAwait(false);
                    if (mode == PriceLookupMode.AsNeeded && lookup.Status == PriceLookupStatus.NotInCase
                        && asked < MostRateLookups)
                    {
                        asked++;
                        lookup = await LookUp(@case, asset, time, PriceLookupMode.AsNeeded, cancellationToken)
                            .ConfigureAwait(false);
                        if (lookup.IsThrottled)
                        {
                            // The source asked to wait: ask it for nothing more now.
                            asked = MostRateLookups;
                            throttled?.Value = true;
                        }
                    }

                    known[key] = lookup;
                }

                if (lookup.Value is not { } price)
                {
                    gap = EdgeValues.GapOf(lookup, time, currency, Now);
                    break;
                }

                parts.Add(new EdgeValuePart(transfer, price.ValueOf(transfer.Amount)));
            }

            if (parts.Count == edge.Transfers.Count)
            {
                values[edge.Key] = EdgeValues.Sum(parts);
            }
            else if (gap is not null)
            {
                gaps[edge.Key] = gap;
            }
        }

        foreach (var price in known.Values.Select(lookup => lookup.Value).OfType<Price>())
        {
            if (!latest.TryGetValue(price.Asset, out var held) || price.At > held.At)
            {
                latest[price.Asset] = price;
            }
        }

        return (values, gaps);
    }

    /// <summary>Asks Pricing for an asset's rate at a time; a failure is no rate, never an error in the graph.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="asset">The asset.</param>
    /// <param name="at">The time.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The answer; a case that cannot be read is answered as a failed lookup.</returns>
    private async Task<PriceLookup<Price>> LookUp(
        ICase @case, Asset asset, DateTimeOffset at, PriceLookupMode mode, CancellationToken cancellationToken)
    {
        try
        {
            return await prices!.GetPriceAsync(
                @case, new PriceRequest(asset.Chain, at) { Asset = asset }, mode, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or CaseException)
        {
            return new PriceLookup<Price>(PriceLookupStatus.Failed, null, [], null);
        }
    }
}
