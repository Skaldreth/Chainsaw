using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>
/// The selection view of what was held back (sketches EX1 and EX2): the filters, the dots and their marks, the
/// choosing,
/// the geometry of the spiral and the time arc, and the graph opening it and bringing the chosen in; without WPF.
/// </summary>
public sealed class HeldBackTests
{
    /// <summary>The traced money's arrival at V in the made-up choices.</summary>
    private static readonly DateTimeOffset Arrival = new(2026, 2, 27, 14, 10, 0, TimeSpan.Zero);

    /// <summary>The clue the made-up choices come from.</summary>
    private static readonly HeldBackClue Clue = new(
        "held-back:" + VKey, VKey, 6, [], new Dictionary<GraphLayout, NodePosition>()) { Address = V };

    /// <summary>Makes a held-back transaction.</summary>
    private static HeldBackChoice Choice(
        int index, double hoursFromArrival, long satoshi, PileSide side, NodeSort sort = NodeSort.Unknown,
        bool inGraph = false, bool otherCase = false, int counterparty = -1)
    {
        var block = new BlockReference(
            900_000 + index,
            BlockHash.Parse((index + 1).ToString("x64", System.Globalization.CultureInfo.InvariantCulture)),
            Arrival.AddHours(hoursFromArrival));
        var held = new HeldBackTransaction(
            TxidOf(100 + index), block, side, new Satoshi(satoshi), Address(400 + PartyOf(index, counterparty)), 1);
        return new HeldBackChoice(held, sort, "p" + index, inGraph, otherCase);
    }

    /// <summary>Gives a transaction's counterparty: its own, or one it shares with another.</summary>
    private static int PartyOf(int index, int counterparty) => counterparty < 0 ? index : counterparty;

    /// <summary>
    /// Six transactions: two near the arrival out, one dust in, one a week away, one to a mixer, one in the graph from
    /// another case; 0,92 BTC traced in.
    /// </summary>
    private static HeldBackChoices Choices() => new(
        Clue,
        "bc1q…x0wl",
        [
            Choice(0, 2.5, 91_870_000, PileSide.Out),
            Choice(1, 21, 30_000_000, PileSide.Out, NodeSort.Mixer),
            Choice(2, -10, 5_000, PileSide.In),
            Choice(3, -200, 50_000_000, PileSide.In),
            Choice(4, 5, 1_000_000, PileSide.Out, NodeSort.Exchange, inGraph: true, otherCase: true),
            Choice(5, 6, 2_000_000, PileSide.Out, NodeSort.Exchange, counterparty: 4),
        ],
        new Satoshi(92_000_000),
        Arrival,
        null);

    [Fact]
    public void By_default_the_window_is_two_days_round_the_arrival_and_dust_is_hidden()
    {
        var view = new HeldBackViewModel(Choices(), TimeZoneInfo.Utc);

        Assert.True(view.IsTwoDays);
        Assert.Equal(Arrival, view.WindowAt);
        Assert.Equal(["p0", "p1", "p4", "p5"], view.Dots.Select(dot => dot.First.Name).Order());
        Assert.Equal("4 av 6", view.FilterCountText);
        Assert.Equal("bc1q…x0wl · 6 tx som hölls tillbaka · visas 4 av 6", view.HeaderText);
        Assert.Equal("Visas 4 av 6 · inga valda", view.CountText);
        Assert.Equal("25 feb 14:10 – 1 mar 14:10", view.TimeText);
        Assert.Equal("Alla belopp", view.AmountText);
    }

    [Fact]
    public void The_whole_time_dust_and_the_direction_filter_as_they_say()
    {
        var view = new HeldBackViewModel(Choices(), TimeZoneInfo.Utc);

        view.WholeTimeCommand.Execute(null);
        view.HideDust = false;
        Assert.Equal(6, view.Dots.Count);
        view.ShowInCommand.Execute(null);
        Assert.Equal(["p2", "p3"], view.Dots.Select(dot => dot.First.Name).Order());
        view.OnlyNotInGraph = true;
        view.ShowAllCommand.Execute(null);
        Assert.DoesNotContain(view.Dots, dot => dot.First.Name == "p4");
        view.ResetCommand.Execute(null);
        Assert.True(view.IsTwoDays);
        Assert.False(view.OnlyNotInGraph);
    }

    [Fact]
    public void The_amount_slider_and_the_groups_narrow_what_is_shown_and_the_last_group_stays_on()
    {
        var view = new HeldBackViewModel(Choices(), TimeZoneInfo.Utc);

        view.Low = HeldBackViewModel.AmountShare(0.25);
        Assert.Equal(["p0", "p1"], view.Dots.Select(dot => dot.First.Name).Order());
        Assert.StartsWith("0,", view.AmountText, StringComparison.Ordinal);
        view.Low = 0;
        foreach (var chip in view.Groups.Where(chip => chip.Group != CounterpartyGroup.Risk))
        {
            chip.ToggleCommand.Execute(null);
        }

        Assert.Equal(["p1"], view.Dots.Select(dot => dot.First.Name));
        view.Groups.Single(chip => chip.Group == CounterpartyGroup.Risk).ToggleCommand.Execute(null);
        Assert.True(view.Groups.Single(chip => chip.Group == CounterpartyGroup.Risk).IsOn);
    }

    [Fact]
    public void A_negative_label_and_an_amount_near_the_traced_one_pulse_and_the_marks_are_said()
    {
        var view = new HeldBackViewModel(Choices(), TimeZoneInfo.Utc);

        var near = view.Dots.Single(dot => dot.First.Name == "p0");
        var mixer = view.Dots.Single(dot => dot.First.Name == "p1");
        var marked = view.Dots.Single(dot => dot.First.Name == "p4");
        Assert.True(near.IsNear && near.Pulses);
        Assert.True(mixer.First.IsNegative && mixer.Pulses);
        Assert.False(marked.Pulses);
        Assert.Contains(
            view.TipOf(near).Lines, line => line.Text.StartsWith("Nära det spårade", StringComparison.Ordinal));
        Assert.Contains(view.TipOf(mixer).Lines, line => line.Text == "Mixer: negativ etikett");
        Assert.Equal(2, view.TipOf(marked).Lines.Count);
        view.ChoosePulsingCommand.Execute(null);
        Assert.Equal(2, view.Selected.Count);
    }

    [Fact]
    public void A_click_chooses_shift_runs_the_lasso_rings_and_take_brings_the_chosen()
    {
        var view = new HeldBackViewModel(Choices(), TimeZoneInfo.Utc);

        view.Click(0, run: false);
        view.Click(2, run: true);
        Assert.Equal(3, view.Selected.Count);
        Assert.StartsWith("3 valda · ", view.CountText, StringComparison.Ordinal);
        Assert.Equal("Plocka ut 3 · Enter", view.TakeText);
        view.Click(0, run: false);
        Assert.Equal(2, view.Selected.Count);
        view.ClearChoiceCommand.Execute(null);
        var first = view.Dots[0].At;
        view.Lasso([
            new GraphPoint(first.X - 5, first.Y - 5), new GraphPoint(first.X + 5, first.Y - 5),
            new GraphPoint(first.X + 5, first.Y + 5), new GraphPoint(first.X - 5, first.Y + 5),
        ]);
        Assert.Equal([view.Dots[0].Key], view.Selected);

        IReadOnlyList<string>? taken = null;
        view.TakeWanted += (_, keys) => taken = keys;
        view.Take();
        Assert.Equal([view.Dots[0].Key], taken);
        Assert.Equal(0, view.DotAt(first));
    }

    [Fact]
    public void Grouping_makes_one_dot_per_counterparty_and_side_with_its_count()
    {
        var view = new HeldBackViewModel(Choices(), TimeZoneInfo.Utc) { GroupByCounterparty = true };

        var grouped = view.Dots.Single(dot => dot.Count == 2);
        Assert.Equal(0.03, grouped.Btc, 6);
        Assert.True(grouped.Size >= HeldBackLayout.GroupedDot);
        Assert.EndsWith("· 3 motparter", view.HeaderText, StringComparison.Ordinal);
        view.ChooseLargestCommand.Execute(null);
        Assert.Equal(4, view.Selected.Count);
    }

    [Fact]
    public void The_spiral_starts_at_the_top_and_the_time_arc_lies_over_the_address()
    {
        var top = HeldBackLayout.SpiralAt(0);
        Assert.Equal(0, top.X, 6);
        Assert.Equal(-HeldBackLayout.SpiralStart, top.Y, 6);
        Assert.True(HeldBackLayout.SpiralAt(40).DistanceTo(new GraphPoint(0, 0)) > HeldBackLayout.SpiralStart + 30);

        var view = new HeldBackViewModel(Choices(), TimeZoneInfo.Utc);
        view.ShowTimeArcCommand.Execute(null);
        Assert.All(view.Dots, dot => Assert.True(dot.At.Y < 0));
        Assert.NotNull(view.Guides.Arrival);
        Assert.Contains(view.Guides.Labels, label => label.IsAccent && label.Text == "spårad ankomst · 0,92 BTC");
        view.ShowSpiralCommand.Execute(null);
        view.ByAmountCommand.Execute(null);
        Assert.Equal("p0", view.Dots[0].First.Name);
        Assert.Contains(view.Guides.Labels, label => label.Text == "störst");
    }

    [Fact]
    public void The_histograms_light_what_is_let_through_and_the_window_follows_a_drag()
    {
        var view = new HeldBackViewModel(Choices(), TimeZoneInfo.Utc);

        Assert.Equal(26, view.AmountBars.Count);
        Assert.Equal(44, view.TimeBars.Count);
        Assert.Contains(view.TimeBars, bar => bar.Height > 0 && !bar.Inside);
        Assert.InRange(view.ArrivalShare, view.WindowStart, view.WindowEnd);
        view.WholeTimeCommand.Execute(null);
        view.MoveWindowTo(0);
        Assert.True(view.IsTwoDays);
        Assert.True(view.WindowAt < Arrival);
    }

    [Fact]
    public void The_motion_eases_in_order_and_ends_in_place()
    {
        Assert.Equal(0, HeldBackMotion.Out(0, 0).Travel, 6);
        Assert.Equal(1, HeldBackMotion.Out(0, 1).Travel, 6);
        Assert.True(HeldBackMotion.Out(800, 0.5).Travel < HeldBackMotion.Out(0, 0.5).Travel);
        Assert.Equal(0, HeldBackMotion.Back(100, 1).Travel, 6);
        Assert.Equal(1, HeldBackMotion.EaseBack(1), 6);
        Assert.Equal(1, HeldBackMotion.DotArrival(5, 10, 1));
        Assert.Equal(1, HeldBackMotion.Tile(30, 31, 1).Travel, 6);
    }

    [Fact]
    public void The_choices_know_the_counterparty_in_the_graph_its_label_and_the_traced_arrival()
    {
        var scene = Scene(graph: GraphSamples.Graph());
        var list = new HeldBackList(
            V,
            [
                new HeldBackTransaction(TxidOf(50), Block(150), PileSide.Out, new Satoshi(1_000), W, 1),
                new HeldBackTransaction(TxidOf(51), Block(151), PileSide.In, new Satoshi(2_000), Address(900), 1),
            ],
            true,
            null);
        var labels = new Dictionary<BitcoinAddress, IReadOnlyList<Label>>
        {
            [Address(900)] = [Label(Address(900), LabelKind.Mixer, "Mixer X")],
        };

        var choices = HeldBackChoices.Build(Clue, list, labels, scene);

        Assert.True(choices.Choices[0].InGraph);
        Assert.Equal("Mixer X", choices.Choices[1].Name);
        Assert.True(choices.Choices[1].IsNegative);
        Assert.False(choices.Choices[1].InGraph);
    }

    [Fact]
    public async Task A_clue_with_an_address_history_opens_the_view_and_the_chosen_are_brought_in()
    {
        var source = new TestGraphSource { HeldBack = _ => Choices() };
        var session = new CaseSession();
        var workflow = new CaseWorkflow(
            new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), session);
        var graph = new GraphViewModel(
            session, workflow, source, TestWays.For(new TestExitConnection()), new InlineUi(), new TestGraphEditor(),
            new TestClock());
        session.Open(new TestCase("2026-0001"));
        await graph.ReloadAsync(fit: true);

        await graph.OpenHeldBackAsync(Clue);

        var view = Assert.IsType<HeldBackViewModel>(graph.HeldBack);
        Assert.False(graph.ShowsCorner);
        view.Click(0, run: false);
        view.Take();
        Assert.Null(graph.HeldBack);
        Assert.Single(source.Taken);
    }

    [Fact]
    public async Task A_history_that_cannot_be_read_says_so_and_opens_nothing()
    {
        var source = new TestGraphSource();
        var session = new CaseSession();
        var workflow = new CaseWorkflow(
            new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), session);
        var graph = new GraphViewModel(
            session, workflow, source, TestWays.For(new TestExitConnection()), new InlineUi(), new TestGraphEditor(),
            new TestClock());
        var toasts = new List<ToastRequest>();
        graph.ToastWanted += (_, toast) => toasts.Add(toast);
        session.Open(new TestCase("2026-0001"));
        await graph.ReloadAsync(fit: true);

        await graph.OpenHeldBackAsync(Clue);

        Assert.Null(graph.HeldBack);
        Assert.Null(graph.OpeningClue);
        Assert.Contains(toasts, toast => toast.Title == GraphViewModel.HeldBackFailedText);
    }
}
