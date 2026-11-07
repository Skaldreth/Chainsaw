using Chainsaw.App.Graph;

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

    /// <summary>Gives the editor over the fakes.</summary>
    private GraphEditor Editor => new(new TestTracing(), _attribution, new TestClustering(), _library);

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
}
