using Chainsaw.App.Graph;
using Chainsaw.App.Navigation;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>
/// The view Tidslinje (sketch TL3a) without WPF: the lanes and events read from the graph, the days and hours, the
/// bundles, the chips and the inspector.
/// </summary>
public sealed class TimelineViewTests
{
    /// <summary>The made-up graph with E an exchange, U sanctioned and W a victim, each label the user's own.</summary>
    private static GraphScene Labelled() => Scene(new Dictionary<string, IReadOnlyList<Label>>
    {
        [EKey] = [Label(E, LabelKind.Exchange, "Börs A")],
        [UKey] = [Label(U, LabelKind.Sanctioned, "Växlare Z")],
        [WKey] = [Label(W, LabelKind.Victim, "Offer 1")],
    });

    /// <summary>Gives a view shown on a graph, its reading done.</summary>
    /// <param name="scene">The graph.</param>
    /// <returns>The view.</returns>
    private static async Task<TimelineViewModel> Shown(GraphScene scene)
    {
        var model = new TimelineViewModel(new InlineUi(), new TestClock()) { TimeZone = TimeZoneInfo.Utc };
        model.Update(scene);
        model.IsShown = true;
        await model.Computed;
        return model;
    }

    [Fact]
    public void The_view_is_built_and_has_its_shortcut()
    {
        var entry = ViewTable.For(AppView.Timeline);

        Assert.True(entry.IsBuilt);
        Assert.True(entry.NeedsCase);
        Assert.Equal("Ctrl+2", entry.Gesture);
    }

    [Fact]
    public void A_lane_per_actor_by_hop_and_the_case_lane_last()
    {
        var data = TimelineReading.Read(Labelled(), TimelineReading.Labels(Labelled()));

        Assert.Equal([WKey, VKey, KKey, UKey, EKey, TimelineReading.CaseLane], data.Lanes.Select(lane => lane.Key));
        Assert.Equal(5, data.Events.Count(each => each.IsTransfer));
        Assert.Equal(3, data.Events.Count(each => each.Kind == TimelineKind.Label));
    }

    [Fact]
    public void A_transfer_takes_the_colour_of_what_the_money_became()
    {
        var events = TimelineReading.Read(Labelled(), []).Events.ToDictionary(each => each.EdgeKey!);

        Assert.Equal(TimelineKind.CashOut, events[KE].Kind);
        Assert.Equal(TimelineKind.Risk, events[VU].Kind);
        Assert.Equal(TimelineKind.Victim, events[VK].Kind);
        Assert.Equal(TimelineKind.Unknown, events[EK].Kind);
        Assert.Equal(Block(102).Height, events[KE].Block!.Height);
    }

    [Fact]
    public void The_case_lane_is_left_out_without_a_case_event()
    {
        var data = TimelineReading.Read(Scene(), []);

        Assert.DoesNotContain(data.Lanes, lane => lane.IsCase);
        Assert.All(data.Events, each => Assert.True(each.IsTransfer));
    }

    [Fact]
    public void The_days_cut_out_a_long_empty_stretch_and_say_how_long()
    {
        var start = new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset[] times = [start, start.AddDays(1), start.AddDays(20)];

        var scale = TimelineScale.Days(times, TimeZoneInfo.Utc, 800);

        Assert.Equal(2, scale.Pieces.Count);
        Assert.Equal(18, Assert.Single(scale.Cuts).Days);
        Assert.Null(scale.XOf(start.AddDays(10)));
        Assert.True(scale.XOf(start.AddDays(20)) > scale.XOf(start.AddDays(1)));
        Assert.Contains(scale.Ticks, tick => tick.Label == "tis 13 okt" && tick.Strong == "13");
    }

    [Fact]
    public void The_hours_are_a_window_around_a_moment()
    {
        var moment = new DateTimeOffset(2026, 10, 15, 22, 30, 0, TimeSpan.Zero);

        var scale = TimelineScale.Hours(moment, TimeZoneInfo.Utc, 700);

        Assert.Equal(TimelineScale.HoursShown, (scale.To - scale.From).TotalHours);
        Assert.Equal(350, scale.XOf(moment.AddMinutes(-30)));
        Assert.Contains(scale.Ticks, tick => tick.IsDayStart && tick.Label == "fre 16 okt");
    }

    [Fact]
    public async Task Transfers_closer_than_a_pixel_column_are_bundled_and_a_click_zooms_to_hours()
    {
        var model = new TimelineViewModel(new InlineUi(), new TestClock()) { TimeZone = TimeZoneInfo.Utc };
        var graph = GraphSamples.Graph() with { Edges = [.. GraphSamples.Graph().Edges.Where(edge => edge.Key != EK)] };
        model.Update(Scene(graph: graph));
        model.IsShown = true;
        await model.Computed;
        model.Resize(40);

        var bundle = Assert.Single(model.Picture.Bundles, b => b.Lane == 1);
        Assert.Equal(2, bundle.Items.Count);
        model.Open(bundle);

        Assert.True(model.IsHours);
        Assert.Empty(model.Picture.Bundles);
        Assert.Equal(bundle.Items[0].Id, model.SelectedId);
    }

    [Fact]
    public async Task Nothing_is_read_while_the_view_is_hidden()
    {
        var model = new TimelineViewModel(new InlineUi(), new TestClock());
        var asked = 0;
        model.Moments = _ =>
        {
            asked++;
            return [];
        };

        model.Update(Labelled());
        await model.Computed;

        Assert.Equal(0, asked);
        Assert.False(model.HasEvents);
        model.IsShown = true;
        await model.Computed;
        Assert.Equal(1, asked);
        Assert.True(model.HasEvents);
    }

    [Fact]
    public async Task The_first_cash_out_is_chosen_with_the_facts_from_the_chain_and_the_judgement_apart()
    {
        var model = await Shown(Labelled());

        var detail = model.Detail!;
        Assert.Equal("Börs A", detail.Title.Split(" → ")[1]);
        Assert.Equal("Uttag", detail.Line);
        Assert.Contains(detail.Facts, fact => fact.Label == "Block" && fact.Value == "102");
        Assert.Contains(detail.Facts, fact => fact is { Label: "txid", IsMono: true, CanCopy: true });
        Assert.Contains(detail.Facts, fact => fact.Label == "Blockhash");
        Assert.Contains("FIFO", detail.Judgement, StringComparison.Ordinal);
        Assert.StartsWith("PÅ BANAN · BÖRS A", model.SequenceTitle, StringComparison.Ordinal);
        Assert.Contains(model.Sequence, row => row.IsSelected && row.Amount.StartsWith('+'));
    }

    [Fact]
    public async Task Money_the_trace_does_not_follow_is_a_fact_without_a_judgement()
    {
        var model = await Shown(Labelled());
        var back = TimelineReading.Read(Labelled(), []).Events.Single(each => each.EdgeKey == EK);

        model.Pick(back.Id);

        Assert.False(model.Detail!.IsJudged);
    }

    [Fact]
    public async Task A_chip_turned_off_dims_its_kind_and_the_count_says_so()
    {
        var model = await Shown(Labelled());
        var before = model.CountText;

        model.Toggle(TimelineFilter.CashOut);

        Assert.NotEqual(before, model.CountText);
        Assert.False(model.Chips.Single(chip => chip.Filter == TimelineFilter.CashOut).IsOn);
        Assert.All(model.Picture.Events.Where(each => each.Event.Kind == TimelineKind.CashOut),
            each => Assert.True(each.IsHidden));
    }

    [Fact]
    public async Task Show_in_graph_asks_for_the_transfer_s_actors_and_line()
    {
        var model = await Shown(Labelled());
        ExposurePath? asked = null;
        model.PathWanted += (_, path) => asked = path;

        model.ShowInGraphCommand.Execute(null);

        Assert.Equal([KKey, EKey], asked!.Nodes);
        Assert.Equal([KE], asked.Edges);
    }

    [Fact]
    public async Task A_label_shows_who_set_it_and_why_and_its_journal_entry()
    {
        var model = await Shown(Labelled());
        var label = TimelineReading.Read(Labelled(), TimelineReading.Labels(Labelled())).Events
            .First(each => each.Kind == TimelineKind.Label && each.On == EKey);

        model.Pick(label.Id);

        Assert.False(model.Detail!.IsTransfer);
        Assert.Contains(model.Detail.Facts, fact => fact.Label == "Av" && fact.Value == "Utredare");
        Assert.Contains(model.Detail.Facts, fact => fact.Label == "Förseglad");
        Assert.False(model.Detail.IsJudged);
    }

    [Fact]
    public async Task A_case_event_added_on_another_computer_carries_the_quiet_mark()
    {
        var model = await Shown(Labelled());
        var label = TimelineReading.Read(Labelled(), TimelineReading.Labels(Labelled())).Events
            .First(each => each.Kind == TimelineKind.Label && each.On == EKey);
        model.IsElsewhere = journal => journal == label.Journal;

        model.Pick(label.Id);

        Assert.True(model.Detail!.Facts.Single(fact => fact.Label == "Förseglad").AddedElsewhere);
        Assert.True(model.Sequence.Single(row => row.Id == label.Id).AddedElsewhere);
        Assert.All(
            model.Detail.Facts.Where(fact => fact.Label != "Förseglad"), fact => Assert.False(fact.AddedElsewhere));
    }

    [Fact]
    public async Task Why_opens_the_guide_on_the_view()
    {
        var model = await Shown(Labelled());
        Guide.GuideRequest? asked = null;
        model.GuideWanted += (_, request) => asked = request;

        model.AskWhyCommand.Execute(null);

        Assert.Equal(GuideTopic.TimelineView, asked!.Topic);
        Assert.Equal("vyn-tidslinje", GuideTopic.TimelineView.FileName());
        Assert.Contains(asked.Context!.Lines, line => line.Topic == GuideTopic.Methods);
    }

    [Fact]
    public void The_strip_shows_the_whole_case_and_the_window_shown()
    {
        var data = TimelineReading.Read(Labelled(), []);
        var scale = TimelineScale.Days(data.Events.Select(each => each.Time!.Value), TimeZoneInfo.Utc, 600);

        var strip = TimelineLayout.Strip(data, scale, new HashSet<TimelineFilter>(), 600);

        Assert.Equal(data.Events.Count, strip.Marks.Count);
        Assert.Single(strip.Windows);
        Assert.All(strip.Marks, mark => Assert.InRange(mark.X, 0, 600));
    }
}
