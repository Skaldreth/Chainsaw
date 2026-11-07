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

    /// <summary>Gives the editor over the fakes.</summary>
    private GraphEditor Editor => new(new TestTracing(), _attribution, _clustering, _library);

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
}
