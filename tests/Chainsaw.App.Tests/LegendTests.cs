using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Tests;

/// <summary>
/// The legend behind the graph's <em>?</em> (sketch GR7a): ordered by what the colour means, drawn from the graph's
/// own tables, and with a sentence for everything that can be hovered.
/// </summary>
public sealed class LegendTests
{
    /// <summary>The rows are the six meanings of colour, in the sketch's order.</summary>
    [Fact]
    public void RowsAreTheMeaningsOfColour()
    {
        Assert.Equal(
            ["Offrens pengar", "Uttag", "Spenderas", "Risk, slut", "Annan kedja", "Okänt"],
            Legend.Groups.Select(group => group.Name));
        Assert.Equal(
            [GraphInk.Accent, GraphInk.CashOut, GraphInk.Spent, GraphInk.Risk, GraphInk.OtherChain, GraphInk.Unknown],
            Legend.Groups.Select(group => group.Ink));
    }

    /// <summary>
    /// Every sort of an actor stands in exactly one row, drawn as the graph draws it; the pile and the held-back clue
    /// are the graph's own structure and are left out.
    /// </summary>
    [Fact]
    public void EverySortOfAnActorStandsOnceAsTheGraphDrawsIt()
    {
        var shown = Legend.Groups.SelectMany(group => group.Sorts).ToList();
        var expected = VisualLanguage.Sorts.Where(look => look.Sort is not (NodeSort.Pile or NodeSort.HeldBack));

        Assert.Equal(expected.Select(look => look.Sort).Order(), shown.Select(sort => sort.Look.Sort).Order());
        Assert.All(shown, sort => Assert.Same(VisualLanguage.Of(sort.Look.Sort), sort.Look));
        Assert.All(shown, sort => Assert.False(string.IsNullOrWhiteSpace(sort.Hint)));
    }

    /// <summary>Each row holds the sorts of its colour: the sanctioned in the risks, the cluster in the unknown.</summary>
    [Fact]
    public void EachRowHoldsTheSortsOfItsColour()
    {
        Assert.Equal(
            [NodeSort.Exchange, NodeSort.NoKycExchange, NodeSort.Nested, NodeSort.OtcP2P, NodeSort.Atm, NodeSort.Card],
            Row("Uttag"));
        Assert.Contains(NodeSort.Sanctioned, Row("Risk, slut"));
        Assert.Equal([NodeSort.Unknown, NodeSort.Cluster, NodeSort.SharedTransaction, NodeSort.Coin], Row("Okänt"));
        Assert.Equal([NodeSort.Victim], Row("Offrens pengar"));
        Assert.Equal([NodeSort.SwapBridge, NodeSort.OtherChainTransaction], Row("Annan kedja"));
    }

    /// <summary>A dashed sort has the graph's frame dashes, a whole one none; a CoinJoin is round.</summary>
    [Fact]
    public void FramesFollowTheGraph()
    {
        var sorts = Legend.Groups.SelectMany(group => group.Sorts).ToDictionary(sort => sort.Look.Sort);

        Assert.Equal(GraphDashes.Frame, sorts[NodeSort.Mixer].FrameDashes);
        Assert.Null(sorts[NodeSort.Exchange].FrameDashes);
        Assert.True(sorts[NodeSort.CoinJoin].IsRound);
        Assert.False(sorts[NodeSort.Exchange].IsRound);
        Assert.Equal([false, false, true], Legend.Forms.Select(form => form.IsDashed));
        Assert.True(Legend.Forms[0].IsRound);
    }

    /// <summary>The marks are the graph's, in its order, each with a sentence.</summary>
    [Fact]
    public void MarksAreTheGraphs()
    {
        Assert.Equal(VisualLanguage.Badges, Legend.Badges.Select(badge => badge.Look));
        Assert.All(Legend.Badges, badge => Assert.False(string.IsNullOrWhiteSpace(badge.Hint)));
    }

    /// <summary>
    /// The lines are drawn with the graph's widths and dashes: the traced line as thick as the most traced, the
    /// untraced as thin as on the surface, the dashes in multiples of the width as the surface makes them.
    /// </summary>
    [Fact]
    public void LinesFollowTheGraph()
    {
        var lines = Legend.Lines.ToDictionary(line => line.Name);

        Assert.Equal(EdgeWidth.ThinnestTraced + EdgeWidth.TracedSpan, lines["Spårat"].Width);
        Assert.Equal(EdgeWidth.Untraced, lines["Inte spårat"].Width);
        Assert.Null(lines["Spårat"].Dashes);
        Assert.Equal(GraphDashes.ForLine(EdgeDash.Uncertain, lines["Osäkert"].Width), lines["Osäkert"].Dashes);
        Assert.Equal(GraphInk.OtherChain, lines["Annan kedja"].Ink);
        Assert.True(lines["Vänder"].IsArc);
    }

    /// <summary>A line's dashes are the sketch's pixels divided by its width; a whole line has none.</summary>
    [Fact]
    public void DashesAreMeasuredInTheLinesWidth()
    {
        Assert.Equal([1d, 2.5d], GraphDashes.ForLine(EdgeDash.Uncertain, 2));
        Assert.Equal([3.5d, 2.5d], GraphDashes.ForLine(EdgeDash.OtherChain, 2));
        Assert.Null(GraphDashes.ForLine(EdgeDash.None, 2));
        Assert.Null(GraphDashes.ForLine(EdgeDash.Uncertain, 0));
    }

    /// <summary>
    /// The bottom row says the rule, then the hovered thing's name and sentence; leaving something else keeps it,
    /// leaving it brings the rule back.
    /// </summary>
    [Fact]
    public void BottomRowFollowsThePointer()
    {
        var legend = new LegendViewModel();
        var cashOut = legend.Groups[1];
        var line = legend.Lines[0];

        Assert.Equal(Legend.Rule, legend.Hint);
        Assert.False(legend.IsHinting);

        legend.Hover(cashOut);
        Assert.Equal("Uttag: " + cashOut.Hint, legend.Hint);
        legend.Hover(line);
        legend.Leave(cashOut);
        Assert.Equal("Spårat: " + line.Hint, legend.Hint);

        legend.Leave(line);
        Assert.Equal(Legend.Rule, legend.Hint);
    }

    /// <summary>Gives the sorts of a row, in order.</summary>
    /// <param name="name">The row's name.</param>
    /// <returns>The sorts.</returns>
    private static NodeSort[] Row(string name) =>
        [.. Legend.Groups.Single(group => group.Name == name).Sorts.Select(sort => sort.Look.Sort)];
}
