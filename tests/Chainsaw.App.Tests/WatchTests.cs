using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>The watch form (sketches BV1a and BV2a) without WPF.</summary>
public sealed class WatchFormTests
{
    private readonly TestWatchSource _source = new();

    private WatchFormViewModel Form(out WatchBoard board)
    {
        (_, board) = WatchSamples.Board(_source);
        return new WatchFormViewModel(board);
    }

    [Fact]
    public async Task A_node_is_watched_out_to_two_receivers_picked_in_the_graph()
    {
        var form = Form(out _);
        Watch? saved = null;
        form.Saved += (_, watch) => saved = watch;

        form.OpenFor(WatchSamples.Subject(V), _ => "Nod");
        form.ChooseDirection(WatchDirection.Out);
        Assert.True(form.Add(WatchSamples.On(E), "Börs A"));
        Assert.True(form.Add(WatchSamples.On(U), null));
        Assert.True(form.Add(WatchSamples.On(E), "Börs A"));
        await form.SaveAsync();

        var filter = Assert.Single(_source.Added);
        Assert.Equal(WatchDirection.Out, filter.Direction);
        Assert.Equal([WatchSamples.On(E), WatchSamples.On(U)], filter.Receivers);
        Assert.Empty(filter.Senders);
        Assert.False(form.IsOpen);
        Assert.NotNull(saved);
        Assert.Equal("Bevaka V", form.Title);
    }

    [Fact]
    public void Only_the_direction_shows_and_a_sentence_says_what_the_watch_does()
    {
        var form = Form(out _);

        form.OpenFor(WatchSamples.Subject(V), _ => "Nod");

        Assert.False(form.ShowsParties);
        Assert.False(form.ShowsMinimum);
        Assert.False(form.ShowsPace);
        Assert.Equal("Säger till när pengar rör sig. Frågar vid varje nytt block.", form.Sentence);

        form.ChooseDirection(WatchDirection.Out);
        form.ToggleParties();
        Assert.True(form.Add(WatchSamples.On(E), "Börs A"));
        form.ToggleMinimum();
        form.MinimumInput = "0,05";

        Assert.Equal("Säger till när pengar går ut till Börs A över 0,05 BTC. Frågar vid varje nytt block.", form.Sentence);
    }

    [Fact]
    public void Folding_the_minimum_in_empties_it()
    {
        var form = Form(out _);
        form.OpenFor(WatchSamples.Subject(V));
        form.ToggleMinimum();
        form.MinimumInput = "1";

        form.ToggleMinimum();

        Assert.Equal(string.Empty, form.MinimumInput);
    }

    [Theory]
    [InlineData(WatchDirection.In, "kommer in från A eller B")]
    [InlineData(WatchDirection.Both, "rör sig till C från A eller B")]
    public void The_sentence_names_the_counterparties_by_direction(WatchDirection direction, string expected)
    {
        var text = WatchWords.Sentence(direction, ["C"], ["A", "B"], string.Empty, WatchPace.FiveMinutes);

        Assert.Equal($"Säger till när pengar {expected}. Frågar högst en gång per 5 min.", text);
    }

    [Fact]
    public void Coming_in_the_picks_go_to_the_senders_and_the_node_itself_is_refused()
    {
        var form = Form(out _);
        form.OpenFor(WatchSamples.Subject(V));
        form.ChooseDirection(WatchDirection.In);

        Assert.False(form.Add(WatchSamples.On(V), null));
        Assert.Equal(WatchFormViewModel.SelfText, form.Problem);
        Assert.False(form.Add(new ChainAddress(Chain.Ethereum, "0x" + new string('1', 40)), null));
        Assert.True(form.Add(WatchSamples.On(W), "W"));

        Assert.Empty(form.Receivers);
        Assert.Equal("W", Assert.Single(form.Senders).Text);
        Assert.False(form.ShowsReceivers);
    }

    [Fact]
    public void The_minimum_is_read_with_a_comma_and_must_be_positive()
    {
        var form = Form(out _);
        form.OpenFor(WatchSamples.Subject(V));

        form.MinimumInput = "0,5";
        Assert.Equal(Btc(0.5m), form.BuildFilter()!.Minimum);
        form.MinimumInput = "-1";
        Assert.Null(form.BuildFilter());
        Assert.Equal(WatchFormViewModel.MinimumProblemText, form.Problem);
        form.MinimumInput = "1 000";
        Assert.Equal(Btc(1000m), form.BuildFilter()!.Minimum);
    }

    [Fact]
    public void On_Ethereum_a_minimum_needs_an_asset_and_a_typed_address_finds_its_chain()
    {
        var form = Form(out _);
        form.OpenNew();
        form.AddressText = "0x" + new string('a', 40);

        Assert.True(form.IsEthereum);
        Assert.False(form.CanHaveMinimum);
        Assert.Equal(WatchFormViewModel.MinimumNeedsAssetText, form.MinimumHint);
        form.ChooseAsset(KnownTokens.EthereumUsdt);
        form.MinimumInput = "1000";
        var filter = form.BuildFilter()!;
        Assert.Equal(KnownTokens.EthereumUsdt, filter.Asset);
        Assert.Equal(1_000_000_000, (long)filter.Minimum!.Value.Units);
    }

    [Fact]
    public void A_typed_Ethereum_address_shows_the_assets_to_choose_from()
    {
        var form = Form(out _);
        form.OpenNew();
        var changed = new List<string?>();
        form.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        form.AddressText = "0x" + new string('a', 40);

        Assert.True(form.ShowsAssets);
        Assert.Contains(nameof(WatchFormViewModel.ShowsAssets), changed);
        Assert.Contains(nameof(WatchFormViewModel.CanHaveMinimum), changed);
        Assert.Contains(nameof(WatchFormViewModel.Chain), changed);
    }

    [Fact]
    public async Task A_typed_address_that_is_none_is_refused_and_nothing_is_saved()
    {
        var form = Form(out _);
        form.OpenNew();
        form.AddressText = "inte en adress";

        await form.SaveAsync();

        Assert.True(form.IsOpen);
        Assert.Equal(WatchFormViewModel.NotAnAddressText, form.Problem);
        Assert.Empty(_source.Added);
    }

    [Fact]
    public async Task A_change_starts_from_the_watch_and_saves_the_new_filter()
    {
        var form = Form(out var board);
        var watch = _source.Add(WatchSamples.Subject(V), WatchSamples.Filter(WatchDirection.Out, [WatchSamples.On(E)]));
        await board.ReloadAsync();

        form.OpenChange(watch, _ => "Börs A");
        Assert.Equal("Börs A", Assert.Single(form.Receivers).Text);
        Assert.Equal("Spara", form.SaveText);
        form.ChooseDirection(WatchDirection.Both);
        await form.SaveAsync();

        Assert.Equal(WatchDirection.Both, _source.Watches.Single().Filter.Direction);
        Assert.Equal(WatchAction.Changed, _source.Records[^1].Action);
    }
}

/// <summary>The view Bevakning (sketch BV1a) without WPF.</summary>
public sealed class MonitoringViewModelTests
{
    private readonly TestWatchSource _source = new();

    private MonitoringViewModel View(out WatchBoard board)
    {
        (_, board) = WatchSamples.Board(_source);
        var form = new WatchFormViewModel(board);
        return new MonitoringViewModel(board, form, new TestClock());
    }

    [Fact]
    public async Task The_list_shows_each_watch_and_the_chosen_ones_hits_and_seeing_them_clears_the_dot()
    {
        var watch = _source.Add(WatchSamples.Subject(V), WatchSamples.Filter(WatchDirection.Out, [WatchSamples.On(E)]));
        _source.Add(WatchSamples.Subject(U, "U"), WatchSamples.Filter(), state: WatchState.Paused);
        _source.Hit(watch, WatchSamples.Out(WatchSamples.On(E), 0.42m));
        var view = View(out var board);
        await board.ReloadAsync();
        view.Names = address => address == WatchSamples.On(E) ? "Börs A" : null;
        await board.ReloadAsync();

        Assert.Equal(2, view.Rows.Count);
        var row = view.Rows[0];
        Assert.True(row.HasNew);
        Assert.Equal(WatchLight.Active, row.Light);
        Assert.Equal(WatchLight.Paused, view.Rows[1].Light);
        Assert.Equal("Börs A", Assert.Single(row.Parties).Text);
        Assert.Contains("1 ny träff", view.Summary, StringComparison.Ordinal);
        Assert.Equal(WatchWords.RunningText, view.BackgroundText);

        view.Choose(row);
        await board.ReloadAsync();

        var hit = Assert.Single(view.Hits);
        Assert.Equal("Utgående 0,4200 BTC till Börs A", hit.Text);
        Assert.Equal(1, _source.Seen[watch.Id]);
        Assert.False(view.Rows[0].HasNew);
        Assert.Contains(view.Facts, fact => fact.Key == "Räknas från" && fact.Value == "Efter block 899 000");
    }

    [Fact]
    public async Task The_background_s_tips_are_shown_as_unchecked_never_as_hits_and_a_rejected_one_stays_faint()
    {
        var watch = _source.Add(WatchSamples.Subject(V), WatchSamples.Filter(WatchDirection.Out, [WatchSamples.On(E)]));
        var match = WatchSamples.Out(WatchSamples.On(E), 0.42m);
        _source.Tips.Add(new WatchTip(1, watch.Id, Chain.Bitcoin, match, Guid.NewGuid(), [], 1, TestWatchSource.At));
        _source.Tips.Add(new WatchTip(2, watch.Id, Chain.Bitcoin, match, Guid.NewGuid(), [], 2, TestWatchSource.At)
        {
            State = WatchTipState.Rejected,
        });
        _source.Tips.Add(new WatchTip(3, watch.Id, Chain.Bitcoin, match, Guid.NewGuid(), [], 3, TestWatchSource.At)
        {
            State = WatchTipState.Confirmed,
            Hit = 7,
        });
        var view = View(out var board);
        await board.ReloadAsync();

        view.Choose(view.Rows[0]);

        Assert.Empty(view.Hits);
        Assert.False(view.Rows[0].HasNew);
        Assert.Equal([2L, 1L], view.Tips.Select(tip => tip.Tip.Sequence));
        Assert.Equal([false, true], view.Tips.Select(tip => tip.IsPending));
        Assert.Equal(WatchWords.TipPendingText, view.Tips[1].State);
        Assert.Equal(WatchWords.TipRejectedText, view.Tips[0].State);
        Assert.Contains("väntar på kontroll", view.Tips[1].State, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_an_exit_the_head_says_so_and_the_rows_wait()
    {
        _source.Add(WatchSamples.Subject(V), WatchSamples.Filter());
        _source.GateState = WatchGate.NoExit;
        var view = View(out var board);
        await board.ReloadAsync();

        Assert.True(view.IsWaiting);
        Assert.Equal(WatchWords.NoExitText, view.BackgroundText);
        Assert.Equal(WatchLight.Waiting, view.Rows[0].Light);
        Assert.Equal("Väntar på utgången", view.Rows[0].StateText);
    }

    [Fact]
    public async Task Remove_asks_first_takes_an_optional_reason_and_pause_toggles()
    {
        var watch = _source.Add(WatchSamples.Subject(V), WatchSamples.Filter());
        var view = View(out var board);
        await board.ReloadAsync();

        await view.TogglePauseAsync();
        Assert.Equal("Fortsätt", view.PauseText);
        await view.RemoveAsync();
        Assert.Empty(_source.Removed);
        view.AskRemove();
        view.RemoveReason = "Fel adress";
        await view.RemoveAsync();

        Assert.Equal((watch.Id, "Fel adress"), Assert.Single(_source.Removed));
        Assert.Empty(view.Rows);
        Assert.False(view.HasSelection);
    }

    [Fact]
    public async Task A_removal_asked_for_one_watch_never_falls_to_the_next()
    {
        var first = _source.Add(WatchSamples.Subject(V), WatchSamples.Filter());
        _source.Add(WatchSamples.Subject(U, "U"), WatchSamples.Filter());
        var view = View(out var board);
        await board.ReloadAsync();
        view.AskRemove();

        await _source.RemoveAsync(new TestCase("x"), first.Id, null);
        await board.ReloadAsync();

        Assert.False(view.IsRemoving);
        Assert.Equal("U", view.Selected!.Name);
    }

    [Fact]
    public async Task Open_in_graph_and_new_are_asked_for()
    {
        var watch = _source.Add(WatchSamples.Subject(V), WatchSamples.Filter());
        var view = View(out var board);
        await board.ReloadAsync();
        Watch? wanted = null;
        view.OpenInGraphWanted += (_, each) => wanted = each;

        view.OpenInGraph();
        view.New();

        Assert.Equal(watch.Id, wanted?.Id);
        Assert.True(view.Form.IsOpen);
        Assert.True(view.Form.IsManual);
    }

    [Fact]
    public async Task Without_watches_the_empty_state_offers_the_one_button_and_the_head_and_panel_step_aside()
    {
        var view = View(out var board);
        await board.ReloadAsync();

        Assert.False(view.HasRows);
        Assert.False(view.ShowsHead);
        Assert.False(view.HasSelection);
        Assert.True(view.CanAddFirst);
        Assert.Equal(WatchWords.EmptyTitle, view.EmptyTitle);
        Assert.Equal(WatchWords.EmptyText, view.EmptyText);

        _source.Add(WatchSamples.Subject(V), WatchSamples.Filter());
        await board.ReloadAsync();

        Assert.True(view.ShowsHead);
        Assert.False(view.CanAddFirst);
        Assert.True(view.HasSelection);
    }

    [Fact]
    public async Task Without_a_case_the_empty_state_says_so_and_offers_nothing()
    {
        var (_, board) = WatchSamples.Board(_source, open: false);
        var view = new MonitoringViewModel(board, new WatchFormViewModel(board), new TestClock());
        await board.ReloadAsync();

        Assert.False(view.ShowsHead);
        Assert.False(view.CanAddFirst);
        Assert.Equal(WatchWords.NoCaseTitle, view.EmptyTitle);
        Assert.Equal(WatchWords.NoCaseText, view.EmptyText);
    }
}

/// <summary>The checks while Chainsaw is open (stage 26), without a loop or the network.</summary>
public sealed class WatchRunnerTests
{
    private readonly TestWatchSource _source = new();

    private readonly TestClock _clock = new();

    private WatchRunner Runner(out WatchBoard board, bool open = true)
    {
        (_, board) = WatchSamples.Board(_source, open);
        return new WatchRunner(board, _clock, (_, _) => Task.CompletedTask);
    }

    [Fact]
    public async Task A_new_watch_starts_at_once_and_then_the_height_is_asked_once_a_minute()
    {
        _source.Add(WatchSamples.Subject(V), WatchSamples.Filter(), start: null);
        var runner = Runner(out var board);

        await runner.RunOnceAsync();
        await board.ReloadAsync();
        _clock.UtcNow += TimeSpan.FromMinutes(2);
        await runner.RunOnceAsync();
        _source.Height = 900_001;
        _clock.UtcNow += TimeSpan.FromSeconds(10);
        await runner.RunOnceAsync();
        _clock.UtcNow += TimeSpan.FromMinutes(1);
        await runner.RunOnceAsync();

        // The module decides which watches are due at each height (WatchDueTests); the runner only paces the height.
        Assert.Equal([900_000L, 900_000L, 900_001L], _source.Checks.Select(check => check.Height));
        Assert.Equal(3, _source.HeightsAsked);
    }

    [Fact]
    public async Task Nothing_is_asked_without_a_case_or_with_the_gate_closed_or_only_paused_watches()
    {
        _source.Add(WatchSamples.Subject(V), WatchSamples.Filter());
        var closed = Runner(out _, open: false);
        await closed.RunOnceAsync();

        _source.GateState = WatchGate.Locked;
        var locked = Runner(out var board);
        await board.ReloadAsync();
        await locked.RunOnceAsync();

        Assert.Equal(0, _source.HeightsAsked);
        Assert.Empty(_source.Checks);
    }

    [Fact]
    public async Task Hits_are_told_and_read_back_and_a_closed_case_is_left_alone()
    {
        var watch = _source.Add(WatchSamples.Subject(V), WatchSamples.Filter());
        var runner = Runner(out var board);
        await board.ReloadAsync();
        IReadOnlyList<WatchHit>? told = null;
        board.HitsFound += (_, hits) => told = hits;
        _source.NextHits.Add(WatchSamples.Out(WatchSamples.On(E), 0.3m));

        await runner.RunOnceAsync();

        Assert.Equal(watch.Id, Assert.Single(told!).WatchId);
        Assert.Single(board.Snapshot.Hits);
        _source.CaseClosed = true;
        _source.Height = 900_002;
        _clock.UtcNow += TimeSpan.FromMinutes(2);
        await runner.RunOnceAsync();
    }
}

/// <summary>What the graph draws for the watches (sketch BV2a), without WPF.</summary>
public sealed class GraphWatchesTests
{
    private readonly TestWatchSource _source = new();

    private GraphWatchOverlay Build(WatchGate gate = WatchGate.Open)
    {
        var snapshot = new WatchSnapshot(
            [.. _source.Watches.Where(watch => watch.State != WatchState.Removed)], _source.Records, _source.Hits,
            _source.Checked, _source.Seen);
        return GraphWatches.Build(Scene(), snapshot, _ => gate);
    }

    [Fact]
    public void A_receiver_without_money_gets_a_watch_line_and_one_with_money_a_glow()
    {
        _source.Add(WatchSamples.Subject(V), WatchSamples.Filter(
            WatchDirection.Out, [WatchSamples.On(K1), WatchSamples.On(E)]));

        var overlay = Build();

        Assert.Equal(VKey, Assert.Single(overlay.Marks).NodeKey);
        Assert.Contains(overlay.Lines, line => line is { From: VKey, To: KKey, Kind: WatchLineKind.Glow });
        Assert.Contains(overlay.Lines, line => line is { From: VKey, To: EKey, Kind: WatchLineKind.Guard });
        Assert.Empty(overlay.Rays);
        Assert.False(overlay.Moves);
    }

    [Fact]
    public void Senders_draw_into_the_node_and_no_counterparties_draw_rays()
    {
        _source.Add(
            WatchSamples.Subject(E, "E"), WatchSamples.Filter(WatchDirection.In, senders: [WatchSamples.On(W)]));
        _source.Add(WatchSamples.Subject(U, "U"), WatchSamples.Filter(WatchDirection.Both));

        var overlay = Build();

        Assert.Contains(overlay.Lines, line => line is { From: WKey, To: EKey, Kind: WatchLineKind.Guard });
        Assert.Equal([true, false], overlay.Rays.Where(ray => ray.NodeKey == UKey).Select(ray => ray.IsOut));
        Assert.Contains("från", overlay.MarkOf(EKey)!.Tip, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hit_becomes_a_line_with_its_amount_that_moves_until_seen()
    {
        var watch = _source.Add(WatchSamples.Subject(V), WatchSamples.Filter(WatchDirection.Out, [WatchSamples.On(E)]));
        _source.Hit(watch, WatchSamples.Out(WatchSamples.On(E), 0.3m));

        var unseen = Build();
        _source.Seen[watch.Id] = 1;
        var seen = Build();

        var line = Assert.Single(unseen.Lines);
        Assert.Equal(WatchLineKind.Hit, line.Kind);
        Assert.Equal("0,3000 BTC", line.Label);
        Assert.True(line.IsNew);
        Assert.True(unseen.Moves);
        Assert.False(seen.Moves);
    }

    [Fact]
    public void A_tip_from_the_background_is_a_grey_dashed_line_that_waits_and_never_a_hit()
    {
        var watch = _source.Add(WatchSamples.Subject(V), WatchSamples.Filter(WatchDirection.Out, [WatchSamples.On(E)]));
        var tip = new WatchTip(
            1, watch.Id, Chain.Bitcoin, WatchSamples.Out(WatchSamples.On(E), 0.3m), Guid.NewGuid(), [], 1,
            TestWatchSource.At);
        var snapshot = new WatchSnapshot(_source.Watches, _source.Records, [], _source.Checked, _source.Seen)
        {
            Tips = [tip],
        };

        var overlay = GraphWatches.Build(Scene(), snapshot, _ => WatchGate.Open);
        var closed = GraphWatches.Build(
            Scene(), snapshot with { Tips = [tip with { State = WatchTipState.Rejected }] }, _ => WatchGate.Open);

        var line = Assert.Single(overlay.Lines);
        Assert.Equal((WatchLineKind.Pending, VKey, EKey), (line.Kind, line.From, line.To));
        Assert.True(line.Idle);
        Assert.Null(line.Label);
        Assert.False(overlay.Moves);
        Assert.Contains("väntar på kontroll", line.Tip, StringComparison.Ordinal);
        Assert.Equal(WatchLineKind.Guard, Assert.Single(closed.Lines).Kind);
    }

    [Fact]
    public void Grey_when_lookups_cannot_be_made_and_a_paused_watch_keeps_only_its_eye()
    {
        _source.Add(WatchSamples.Subject(V), WatchSamples.Filter(WatchDirection.Out, [WatchSamples.On(E)]));
        _source.Add(WatchSamples.Subject(U, "U"), WatchSamples.Filter(), state: WatchState.Paused);
        _source.Add(WatchSamples.Subject(Address(77), "Utanför"), WatchSamples.Filter());

        var overlay = Build(WatchGate.NoExit);

        Assert.All(overlay.Marks, mark => Assert.True(mark.Idle));
        Assert.All(overlay.Lines, line => Assert.True(line.Idle));
        Assert.Equal([VKey, UKey], overlay.Marks.Select(mark => mark.NodeKey));
        Assert.DoesNotContain(overlay.Rays, ray => ray.NodeKey == UKey);
    }
}
