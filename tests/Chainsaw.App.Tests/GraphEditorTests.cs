using Chainsaw.App.Graph;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>The inspector's way into the modules when a label is taken back: the label library follows at once.</summary>
public sealed class GraphEditorTests
{
    /// <summary>The attribution.</summary>
    private readonly TestAttribution _attribution = new() { RetractAnswer = true };

    /// <summary>The library.</summary>
    private readonly TestLibrary _library = new();

    /// <summary>The case.</summary>
    private readonly TestCase _case = new("2026-0001");

    /// <summary>The clustering.</summary>
    private readonly TestClustering _clustering = new();

    /// <summary>The checks, which take a wallet's approval back with its merge.</summary>
    private readonly TestCheckService _checks = new();

    /// <summary>The tracing, which says which transactions are gone.</summary>
    private readonly TestTracing _tracing = new();

    /// <summary>Gives the editor over the fakes.</summary>
    private GraphEditor Editor => new(_tracing, _attribution, _clustering, _library, _checks);

    /// <summary>
    /// A decision about a held back conclusion is checked against the gone transactions the expansions leave out
    /// (sketch HB1a), so that one held back on its own gone transaction can be decided.
    /// </summary>
    [Fact]
    public async Task AHeldBackDecisionIsToldWhichTransactionsAreGone()
    {
        var gone = Txid.Parse(new string('a', 64));
        _tracing.SettledGone.Add(gone);

        var outcome = await Editor.DecideHeldBackAsync(
            _case, gone, Chainsaw.Modules.Clustering.Contracts.ConclusionChoice.Keep, null);

        Assert.True(outcome.Done);
        Assert.Equal([gone], Assert.Single(_clustering.GoneTold));
    }

    /// <summary>A label taken back is taken in by the library at once, so that its copies are withdrawn.</summary>
    [Fact]
    public async Task TheLibraryFollowsALabelTakenBack()
    {
        var outcome = await Editor.RetractLabelAsync(_case, 7, null);

        Assert.Equal(new UndoOutcome(true), outcome);
        Assert.Equal((7L, (string?)null), Assert.Single(_attribution.Retracted));
        Assert.Same(_case, Assert.Single(_library.Absorbed));
    }

    /// <summary>A library that cannot follow now never undoes the taking back; it is said instead.</summary>
    [Fact]
    public async Task ALibraryThatCannotFollowIsSaid()
    {
        _library.Failure = new InvalidOperationException("låst valv");

        var outcome = await Editor.RetractLabelAsync(_case, 7, "Fel");

        Assert.True(outcome.Done);
        Assert.Equal(GraphEditor.LibraryLaterText, outcome.LibraryProblem);
    }

    /// <summary>A label already taken back leaves the library alone.</summary>
    [Fact]
    public async Task NothingTakenBackLeavesTheLibraryAlone()
    {
        _attribution.RetractAnswer = false;

        var outcome = await Editor.RetractLabelAsync(_case, 7, null);

        Assert.False(outcome.Done);
        Assert.Empty(_library.Absorbed);
    }

    /// <summary>Sprid inte on a label already sealed lets the library follow at once (sketch ET1a).</summary>
    [Fact]
    public async Task TheLibraryFollowsALabelMarkedDoNotSpread()
    {
        var outcome = await Editor.SetDoNotSpreadAsync(_case, 7, doNotSpread: true);

        Assert.Equal(new UndoOutcome(true), outcome);
        Assert.Equal((7L, true), Assert.Single(_attribution.SpreadChanges));
        Assert.Same(_case, Assert.Single(_library.Absorbed));
    }

    /// <summary>A breaking out is stored as one; when the case refuses it, nothing is stored.</summary>
    [Fact]
    public async Task ABreakingOutIsStoredAsOne()
    {
        var failure = await Editor.SplitAsync(
            _case,
            [new Modules.Clustering.Contracts.SplitRequest(GraphSamples.K1, GraphSamples.K2, "change-address", "Två")]);

        Assert.Equal(0, failure.Stored);
        Assert.IsType<NotSupportedException>(failure.Failure);
        Assert.Equal(new SplitOutcome(0, null), await Editor.SplitAsync(_case, []));
    }

    /// <summary>
    /// A breaking out is read whole from any of its addresses, also when its splits share no address (A and B broken
    /// out of a cluster where each was joined to a different address that stayed).
    /// </summary>
    [Fact]
    public async Task ABreakingOutIsReadWholeFromAnyOfItsAddresses()
    {
        var at = DateTimeOffset.UnixEpoch;
        var a = GraphSamples.Address(40);
        var b = GraphSamples.Address(41);
        Modules.Clustering.Contracts.SplitHistoryEntry Entry(
            long sequence, BitcoinAddress from, BitcoinAddress to, long journal) =>
            new(new(sequence, from, to, "change-address", "Två", journal, at), null);
        _clustering.SplitHistory.AddRange(
            [Entry(1, a, GraphSamples.K1, 5), Entry(2, b, GraphSamples.K2, 5), Entry(3, b, GraphSamples.K2, 9)]);
        var node = GraphSamples.Node("address:a", Modules.Tracing.Contracts.GraphNodeKind.Address, [a], hop: 1);

        var data = await Editor.ReadHistoryAsync(_case, node, CancellationToken.None);

        Assert.Equal([1L, 2L], data.Splits.Select(entry => entry.Split.Sequence));
    }

    /// <summary>
    /// The history reads the user's merges that hold one of the node's addresses, also those undone, each with the
    /// other merges of the same approval (one journal entry), so that the approval is undone as one.
    /// </summary>
    [Fact]
    public async Task TheHistoryReadsTheUsersMergesOfTheNodeWholeApprovals()
    {
        var at = DateTimeOffset.UnixEpoch;
        var a = GraphSamples.Address(40);
        var b = GraphSamples.Address(41);
        var c = GraphSamples.Address(42);
        var d = GraphSamples.Address(43);
        Modules.Clustering.Contracts.MergeHistoryEntry Entry(
            long sequence, BitcoinAddress first, BitcoinAddress other, long journal) =>
            new(
                new(sequence, [first, other], GraphSamples.Tx[0], Modules.Clustering.Contracts.UserMerges.Id,
                    "Grund", null, null, journal, at),
                null);
        _clustering.MergeHistory.AddRange([Entry(1, a, b, 5), Entry(2, c, d, 5), Entry(3, c, d, 9)]);
        var node = GraphSamples.Node("address:a", Modules.Tracing.Contracts.GraphNodeKind.Address, [a], hop: 1);

        var data = await Editor.ReadHistoryAsync(_case, node, CancellationToken.None);

        Assert.Equal([1L, 2L], data.Merges.Select(entry => entry.Merge.Sequence));
    }

    /// <summary>Undoing a merge goes through the checks, which take the approval back with it.</summary>
    [Fact]
    public async Task UndoingAMergeTakesTheApprovalBackThroughTheChecks()
    {
        var outcome = await Editor.UndoMergesAsync(_case, [3, 4], "Fel");

        Assert.True(outcome.Done);
        var (merges, reason) = Assert.Single(_checks.TakenBack);
        Assert.Equal([3L, 4L], merges);
        Assert.Equal("Fel", reason);

        _checks.Answer = 0;
        Assert.False((await Editor.UndoMergesAsync(_case, [3], null)).Done);
    }

    /// <summary>
    /// A link an approved suggestion made is taken back through the checks, which take the approval back with it.
    /// </summary>
    [Fact]
    public async Task TakingBackAnApprovedLinkGoesThroughTheChecks()
    {
        var link = new Modules.Tracing.Contracts.CrossChainLink(
            9,
            new Modules.Tracing.Contracts.Outpoint(GraphSamples.Tx[0], 0),
            new Modules.Tracing.Contracts.ChainTransactionRef(SharedKernel.Chain.Tron, new string('a', 64)),
            string.Empty,
            1,
            DateTimeOffset.UnixEpoch)
        {
            Origin = new Modules.Tracing.Contracts.CrossChainOrigin("cross-chain v1", 4),
        };

        var outcome = await Editor.UnlinkAsync(_case, link, "Fel");

        Assert.True(outcome.Done);
        Assert.Equal((9L, (string?)"Fel"), Assert.Single(_checks.LinksTakenBack));
    }
}

/// <summary>A Checks module that only answers what undoing merges takes back.</summary>
internal sealed class TestCheckService : Modules.Checks.Contracts.ICheckService
{
    /// <summary>Gets the merges asked to be undone, with the reason.</summary>
    public List<(long[] Merges, string? Reason)> TakenBack { get; } = [];

    /// <summary>Gets or sets how many merges are said to be undone.</summary>
    public int Answer { get; set; } = 1;

    public IReadOnlyList<Modules.Clustering.Contracts.HeuristicId> Analyses => [];

    public Modules.Checks.Contracts.CheckRun Run(
        Modules.Cases.Contracts.ICase @case, Modules.Tracing.Contracts.TraceGraph graph) =>
        throw new NotSupportedException();

    public Modules.Checks.Contracts.CheckDecision Decide(
        Modules.Cases.Contracts.ICase @case, Modules.Checks.Contracts.DecisionRequest request) =>
        throw new NotSupportedException();

    public IReadOnlyList<Modules.Checks.Contracts.CheckDecision> DecideAll(
        Modules.Cases.Contracts.ICase @case, IReadOnlyList<Modules.Checks.Contracts.DecisionRequest> requests) =>
        throw new NotSupportedException();

    public int TakeBackMerges(
        Modules.Cases.Contracts.ICase @case, IReadOnlyCollection<long> mergeSequences, string? reason)
    {
        TakenBack.Add(([.. mergeSequences], reason));
        return Answer;
    }

    /// <summary>Gets the links asked to be taken back through their approval, with the reason.</summary>
    public List<(long Link, string? Reason)> LinksTakenBack { get; } = [];

    public bool TakeBackLink(
        Modules.Cases.Contracts.ICase @case, Modules.Tracing.Contracts.CrossChainLink link, string? reason)
    {
        LinksTakenBack.Add((link.Sequence, reason));
        return Answer > 0;
    }

    public IReadOnlyList<Modules.Checks.Contracts.OwnArea> GetAreas(Modules.Cases.Contracts.ICase @case) => [];

    public Modules.Checks.Contracts.OwnArea AddArea(
        Modules.Cases.Contracts.ICase @case, Modules.Checks.Contracts.OwnAreaDraft draft) =>
        throw new NotSupportedException();

    public Modules.Checks.Contracts.OwnArea? RenameArea(Modules.Cases.Contracts.ICase @case, string id, string name) =>
        throw new NotSupportedException();

    public bool RemoveArea(Modules.Cases.Contracts.ICase @case, string id, string? reason) =>
        throw new NotSupportedException();
}
