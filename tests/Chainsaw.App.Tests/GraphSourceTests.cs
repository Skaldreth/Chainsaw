using Chainsaw.App.Graph;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

/// <summary>Labels by address, as Attribution would give them; nothing else is used by the graph.</summary>
internal sealed class TestAttribution : IAttributionService
{
    public Dictionary<BitcoinAddress, IReadOnlyList<Label>> Labels { get; } = [];

    /// <summary>How many times one address was asked about.</summary>
    public int SingleCalls { get; private set; }

    /// <summary>How many times many addresses were asked about at once.</summary>
    public int BatchCalls { get; private set; }

    public IReadOnlyList<Label> GetLabels(ICase @case, BitcoinAddress address)
    {
        SingleCalls++;
        return Labels.GetValueOrDefault(address) ?? [];
    }

    public IReadOnlyDictionary<BitcoinAddress, IReadOnlyList<Label>> GetLabels(
        ICase @case, IReadOnlyCollection<BitcoinAddress> addresses)
    {
        BatchCalls++;
        return addresses.Distinct().Where(Labels.ContainsKey)
            .ToDictionary(address => address, address => Labels[address]);
    }

    public Dictionary<ChainAddress, IReadOnlyList<Label>> AccountLabels { get; } = [];

    public IReadOnlyDictionary<ChainAddress, IReadOnlyList<Label>> GetLabels(
        ICase @case, IReadOnlyCollection<ChainAddress> addresses) =>
        addresses.Distinct().Where(AccountLabels.ContainsKey)
            .ToDictionary(address => address, address => AccountLabels[address]);

    public LabelEntry Add(ICase @case, LabelDraft draft) => throw new NotSupportedException();

    /// <summary>Gets or sets what taking a label back answers; unset, it is not supported.</summary>
    public bool? RetractAnswer { get; set; }

    /// <summary>Gets what was taken back: the sequence and the reason.</summary>
    public List<(long Sequence, string? Reason)> Retracted { get; } = [];

    public bool Retract(ICase @case, long sequence, string? reason)
    {
        var answer = RetractAnswer ?? throw new NotSupportedException();
        Retracted.Add((sequence, reason));
        return answer;
    }

    public IReadOnlyList<LabelHistoryEntry> GetHistory(ICase @case, IReadOnlyCollection<BitcoinAddress> addresses) =>
        [];

    /// <summary>Gets or sets every label in the case, as the view Etiketter reads them.</summary>
    public List<LabelHistoryEntry> History { get; set; } = [];

    /// <summary>Gets or sets a failure reading the whole history throws.</summary>
    public Exception? HistoryFailure { get; set; }

    public IReadOnlyList<LabelHistoryEntry> GetHistory(ICase @case) =>
        HistoryFailure is null ? [.. History] : throw HistoryFailure;

    /// <summary>Gets what was marked <em>Sprid inte</em> or let spread: the sequence and the marking.</summary>
    public List<(long Sequence, bool DoNotSpread)> SpreadChanges { get; } = [];

    public bool SetDoNotSpread(ICase @case, long sequence, bool doNotSpread)
    {
        SpreadChanges.Add((sequence, doNotSpread));
        return true;
    }

    public IReadOnlyList<LabelHistoryEntry> GetHistory(ICase @case, IReadOnlyCollection<ChainAddress> addresses) =>
        [];

    public IReadOnlyList<LabelEntry> GetEntries(ICase @case) => throw new NotSupportedException();

    public IReadOnlyList<Label> GetLabels(ICase @case) => throw new NotSupportedException();

    /// <summary>The addresses checked against the sanction list, each with its chain, in order.</summary>
    public List<ChainAddress> SanctionChecked { get; } = [];

    /// <summary>Whether checking Bitcoin addresses against the sanction list fails.</summary>
    public bool BitcoinSanctionsFail { get; set; }

    public SanctionCheck CheckSanctions(ICase @case, IReadOnlyCollection<BitcoinAddress> addresses)
    {
        if (BitcoinSanctionsFail)
        {
            throw new IOException("Listan är låst.");
        }

        SanctionChecked.AddRange(addresses.Select(address => address.ToChainAddress()));
        return new SanctionCheck(null, [], 0);
    }

    public SanctionCheck CheckSanctions(ICase @case, IReadOnlyCollection<EthereumAddress> addresses)
    {
        SanctionChecked.AddRange(addresses.Select(address => address.ToChainAddress()));
        return new SanctionCheck(null, [], 0);
    }
}

/// <summary>A clustering that knows one cluster; nothing else is used by the graph.</summary>
internal sealed class TestClustering : IClusteringService
{
    public List<BitcoinAddress> Asked { get; } = [];

    public IReadOnlyList<HeuristicId> Heuristics => [];

    public Cluster GetCluster(ICase @case, BitcoinAddress address)
    {
        Asked.Add(address);
        return new Cluster(address, [new ClusterMember(address, null, null)], [], [], [], []);
    }

    public IReadOnlyDictionary<BitcoinAddress, Cluster> GetClusters(
        ICase @case, IReadOnlyCollection<BitcoinAddress> addresses) =>
        addresses.Distinct().ToDictionary(address => address, address => GetCluster(@case, address));

    public ClusteringRun Analyse(ICase @case, IReadOnlyCollection<BitcoinTransaction> transactions) =>
        throw new NotSupportedException();

    public ClusterSplit Split(ICase @case, SplitRequest request) => throw new NotSupportedException();

    public IReadOnlyList<ClusterSplit> Split(ICase @case, IReadOnlyList<SplitRequest> requests) =>
        throw new NotSupportedException();

    public int UndoSplits(ICase @case, IReadOnlyCollection<long> splitSequences, string? reason) =>
        throw new NotSupportedException();

    public UndoSplitPreview? PreviewUndoSplits(
        ICase @case, IReadOnlyCollection<long> splitSequences, BitcoinAddress? from) =>
        throw new NotSupportedException();

    /// <summary>Gets the splits the case holds.</summary>
    public List<SplitHistoryEntry> SplitHistory { get; } = [];

    public IReadOnlyList<SplitHistoryEntry> GetSplitHistory(
        ICase @case, IReadOnlyCollection<BitcoinAddress> addresses) =>
        [
            .. SplitHistory.Where(entry =>
                addresses.Contains(entry.Split.First) || addresses.Contains(entry.Split.Second)),
        ];

    public IReadOnlyList<SplitHistoryEntry> GetSplitHistory(ICase @case) => SplitHistory;

    public IReadOnlyList<ClusterLink>? ExplainConnection(ICase @case, BitcoinAddress from, BitcoinAddress to) =>
        throw new NotSupportedException();

    public TransactionConclusions GetConclusions(ICase @case, Txid txid) => throw new NotSupportedException();

    public TransactionConclusions GetConclusions(ICaseReader reader, Txid txid) => throw new NotSupportedException();

    public IReadOnlyList<ClusterMerge> Merge(
        ICaseTransaction transaction, IReadOnlyList<MergeRequest> requests, string? reason, MergeOrigin? origin) =>
        throw new NotSupportedException();

    public int UndoMerges(ICaseTransaction transaction, IReadOnlyCollection<long> mergeSequences, string? reason) =>
        throw new NotSupportedException();

    public UndoMergePreview? PreviewUndoMerges(ICase @case, IReadOnlyCollection<long> mergeSequences) =>
        throw new NotSupportedException();

    /// <summary>Gets the user's merges the case holds.</summary>
    public List<MergeHistoryEntry> MergeHistory { get; } = [];

    public IReadOnlyList<MergeHistoryEntry> GetMergeHistory(ICase @case) => MergeHistory;

    public IReadOnlyList<MergeHistoryEntry> GetMergeHistory(ICaseReader reader) => MergeHistory;

    public VersionWithdrawal Withdraw(ICase @case, HeuristicId heuristic, string? reason) =>
        throw new NotSupportedException();

    public IReadOnlyList<VersionWithdrawal> GetWithdrawals(ICase @case) => [];
}

/// <summary>The graph's way into the modules: labels, seeds, the trace and the expansion, off the UI thread.</summary>
public sealed class GraphSourceTests
{
    /// <summary>The fakes and the source.</summary>
    private readonly TestTracing _tracing = new() { Graph = GraphSamples.Graph() };

    /// <summary>The labels.</summary>
    private readonly TestAttribution _attribution = new();

    /// <summary>The clusters.</summary>
    private readonly TestClustering _clustering = new();

    /// <summary>The case.</summary>
    private readonly TestCase _case = new("2026-0001");

    /// <summary>Gives the source over the fakes.</summary>
    private GraphSource Source => new(_tracing, _attribution, _clustering);

    /// <summary>
    /// The graph is read with each node's labels (a cluster's once, however many addresses carry it), traced by the
    /// asked method from the start's coins.
    /// </summary>
    [Fact]
    public async Task LoadsLabelsAndTraces()
    {
        var label = GraphSamples.Label(GraphSamples.K1, LabelKind.Exchange, "Börs A");
        _attribution.Labels[GraphSamples.K1] = [label];
        _attribution.Labels[GraphSamples.K2] = [label];
        _tracing.Tracer = (_, _) => GraphSamples.Trace();

        var scene = await Source.LoadAsync(_case, TracingMethodKind.Haircut, CancellationToken.None);

        Assert.Equal([label], scene.Snapshot.Labels[GraphSamples.KKey]);
        Assert.False(scene.Snapshot.Labels.ContainsKey(GraphSamples.UKey));
        var request = Assert.Single(_tracing.Traced);
        Assert.Equal(TracingMethodKind.Haircut, request.Method);
        Assert.Equal([new TraceSeed(new Outpoint(GraphSamples.Tx[0], 0))], request.Seeds);
        Assert.NotNull(scene.Snapshot.Trace);
        Assert.Null(scene.Snapshot.TraceProblem);
    }

    /// <summary>
    /// The scene is built the way the reading asks, with the outputs that can never be spent shown or hidden, so that
    /// the view never builds it again on the UI thread.
    /// </summary>
    [Fact]
    public async Task TheSceneIsBuiltWithTheHiddenOutputsAsTheReadingAsks()
    {
        _tracing.Tracer = (_, _) => GraphSamples.Trace();
        var shown = await Source.LoadAsync(
            _case, new GraphReading(TracingMethodKind.Fifo, ShowUnspendable: true), CancellationToken.None);
        var hidden = await Source.LoadAsync(_case, TracingMethodKind.Fifo, CancellationToken.None);

        Assert.True(shown.ShowsUnspendable);
        Assert.False(hidden.ShowsUnspendable);
    }

    /// <summary>A trace that cannot be counted leaves the graph shown without amounts, and says so.</summary>
    [Fact]
    public async Task FailedTraceIsSaid()
    {
        _tracing.Tracer = (_, _) => throw new ArgumentException("seed");

        var scene = await Source.LoadAsync(_case, TracingMethodKind.Fifo, CancellationToken.None);

        Assert.Null(scene.Snapshot.Trace);
        Assert.Equal(GraphSource.TraceFailedText, scene.Snapshot.TraceProblem);
        Assert.Equal(5, scene.Nodes.Count);
    }

    /// <summary>A start whose coins the graph does not show yet is not traced, and the graph says to expand it.</summary>
    [Fact]
    public async Task NothingToTraceIsSaid()
    {
        _tracing.Graph = GraphSamples.Graph() with
        {
            StartPoints = [TraceStartPoint.ForAddress(GraphSamples.Address(77))],
        };

        var scene = await Source.LoadAsync(_case, TracingMethodKind.Fifo, CancellationToken.None);

        Assert.Empty(_tracing.Traced);
        Assert.Equal(GraphSource.NothingToTraceText, scene.Snapshot.TraceProblem);
    }

    /// <summary>An expansion makes each lookup in turn and sums up how it went, with each problem once.</summary>
    [Fact]
    public async Task ExpansionMakesEveryLookup()
    {
        _tracing.Tracer = (_, _) => GraphSamples.Trace();
        ExpansionRequest[] requests =
        [
            ExpansionRequest.ForAddress(GraphSamples.K1),
            ExpansionRequest.ForAddress(GraphSamples.K2),
            ExpansionRequest.ForAddress(GraphSamples.E),
        ];

        var result = await Source.ExpandAsync(_case, requests, TracingMethodKind.Fifo, CancellationToken.None);

        Assert.Equal(requests, _tracing.Expanded);
        Assert.Equal(ExpansionOutcome.Partly, result.Outcome);
        Assert.Equal(["Ingen utgång är ansluten."], result.Problems);
        Assert.Equal(5, result.Scene.Nodes.Count);
    }

    /// <summary>Only a cluster is asked of Clustering, from its first address.</summary>
    [Fact]
    public async Task OnlyClustersAreExplained()
    {
        var graph = GraphSamples.Graph();

        var cluster = await Source.ExplainClusterAsync(_case, graph.Node(GraphSamples.KKey)!, CancellationToken.None);
        var address = await Source.ExplainClusterAsync(_case, graph.Node(GraphSamples.UKey)!, CancellationToken.None);

        Assert.Single(cluster);
        Assert.Empty(address);
        Assert.Equal([GraphSamples.K1], _clustering.Asked);
    }

    /// <summary>A trace that refuses its own result (InvalidOperationException) keeps the graph without amounts.</summary>
    [Fact]
    public async Task RefusedTraceKeepsTheGraph()
    {
        _tracing.Tracer = (_, _) => throw new InvalidOperationException("ring");

        var scene = await Source.LoadAsync(_case, TracingMethodKind.Fifo, CancellationToken.None);

        Assert.Equal(GraphSource.TraceFailedText, scene.Snapshot.TraceProblem);
        Assert.Equal(5, scene.Nodes.Count);
    }

    /// <summary>An expansion stops at its limit of new transactions and says so (principle 7).</summary>
    [Fact]
    public async Task ExpansionStopsAtItsLimit()
    {
        _tracing.Tracer = (_, _) => GraphSamples.Trace();
        _tracing.NewPerExpansion = [.. Enumerable.Range(0, 60).Select(GraphSamples.TxidOf)];
        ExpansionRequest[] requests =
        [
            ExpansionRequest.ForAddress(GraphSamples.K1),
            ExpansionRequest.ForAddress(GraphSamples.K2),
            ExpansionRequest.ForAddress(GraphSamples.E),
        ];

        var result = await Source.ExpandAsync(_case, requests, TracingMethodKind.Fifo, CancellationToken.None);

        Assert.Equal(2, _tracing.Expanded.Count);
        Assert.Equal(120, result.NewTransactions);
        Assert.Equal(ExpansionOutcome.Partly, result.Outcome);
        Assert.Equal([GraphSource.LimitReachedText], result.Problems);
    }

    /// <summary>An expansion makes at most ten lookups, however many addresses a cluster has.</summary>
    [Fact]
    public async Task ExpansionStopsAtTenLookups()
    {
        _tracing.Tracer = (_, _) => GraphSamples.Trace();
        _tracing.NewPerExpansion = [GraphSamples.TxidOf(0)];
        var requests = Enumerable.Range(10, 15).Select(n => ExpansionRequest.ForAddress(GraphSamples.Address(n))).ToList();

        var result = await Source.ExpandAsync(_case, requests, TracingMethodKind.Fifo, CancellationToken.None);

        Assert.Equal(GraphSource.MostLookups, _tracing.Expanded.Count);
        Assert.Equal(ExpansionOutcome.Partly, result.Outcome);
        Assert.Equal([GraphSource.LimitReachedText], result.Problems);
    }
}
