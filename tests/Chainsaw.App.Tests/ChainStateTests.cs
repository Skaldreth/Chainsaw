using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>
/// The graph's view of the chains now (sketches IN4a, GR9a and TL2a), without WPF: the card's back with its balance and
/// rate, the block marks of transactions that left their block, <em>Väntar på källan</em> and the strip's lens.
/// </summary>
public sealed class ChainStateTests
{
    /// <summary>The time zone the tests write times in.</summary>
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    /// <summary>Now, in the tests.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 14, 32, 0, TimeSpan.Zero);

    private readonly CaseSession _session = new();

    private readonly TestClock _clock = new() { UtcNow = Now };

    private readonly FakeChainState _state = new();

    private readonly FakeSources _sources = new();

    private readonly TestExitConnection _exit = new();

    private readonly TestGraphSource _source = new();

    // ===== The card's back (IN4a) =====

    [Fact]
    public void A_back_shows_a_row_per_asset_with_its_last_decimals_dimmed_and_todays_value()
    {
        var holdings = new NodeHoldings([Btc(5.85m)], new ChainTip(921_344, "00ab", Now), Now, false, null);
        var rates = new Dictionary<Asset, Price> { [Asset.Bitcoin] = Rate(Asset.Bitcoin, 1_012_400m, Now.AddHours(-1)) };

        var card = HoldingsCards.Build("n", holdings, rates, CardRateKind.Today, null, Now, Utc);

        var row = Assert.Single(card.Rows);
        Assert.Equal("₿", row.Glyph);
        Assert.Equal("5,8500", row.Head);
        Assert.Equal("0000", row.Tail);
        Assert.Equal("5 922 540 kr", row.Value);
        Assert.Equal(CardRateKind.Today, card.Rate);
        Assert.Equal("test · i dag 13:32", card.RateText);
        Assert.Equal("921 344", card.Block);
        Assert.Equal(HoldingsCard.HeadHeight + HoldingsCard.RowHeight + HoldingsCard.FootHeight + 2, card.Height);
    }

    [Fact]
    public void Offline_the_back_uses_the_cases_newest_rate_with_its_date()
    {
        var holdings = new NodeHoldings([Btc(1m)], null, Now, true, null);
        var rates = new Dictionary<Asset, Price> { [Asset.Bitcoin] = Rate(Asset.Bitcoin, 998_300m, Now.AddDays(-3)) };

        var card = HoldingsCards.Build("n", holdings, rates, CardRateKind.FromCase, null, Now, Utc);

        Assert.Equal(CardRateKind.FromCase, card.Rate);
        Assert.StartsWith("Offline · kurs 5 nov", card.RateText, StringComparison.Ordinal);
        Assert.Equal("998 300 kr", card.Rows[0].Value);
        Assert.Contains(HoldingsCards.OutdatedText.Trim(), card.BlockTip, StringComparison.Ordinal);
    }

    [Fact]
    public void Offline_without_a_rate_in_the_case_the_back_says_so_and_values_nothing()
    {
        var holdings = new NodeHoldings([Btc(1m)], null, Now, false, null);

        var card = HoldingsCards.Build(
            "n", holdings, new Dictionary<Asset, Price>(), CardRateKind.FromCase, null, Now, Utc);

        Assert.Equal(CardRateKind.None, card.Rate);
        Assert.Equal(HoldingsCards.NoRateText, card.RateText);
        Assert.Null(card.Rows[0].Value);
    }

    [Fact]
    public void An_empty_address_says_since_when_and_needs_no_rate()
    {
        var holdings = new NodeHoldings([], null, Now, false, null);
        var left = new DateTimeOffset(2026, 9, 29, 10, 20, 0, TimeSpan.Zero);

        var card = HoldingsCards.Build("n", holdings, new Dictionary<Asset, Price>(), CardRateKind.Today, left, Now, Utc);

        Assert.True(card.IsEmpty);
        Assert.False(card.ShowsRate);
        Assert.Equal("29 sep 10:20", card.EmptySince);
        Assert.Equal(HoldingsCard.HeadHeight + HoldingsCard.EmptyHeight + HoldingsCard.FootHeight + 2, card.Height);
    }

    [Fact]
    public void A_balance_that_could_not_be_had_says_why()
    {
        var holdings = new NodeHoldings([], null, null, false, "Källan bad Chainsaw vänta.");

        var card = HoldingsCards.Build("n", holdings, new Dictionary<Asset, Price>(), CardRateKind.None, null, Now, Utc);

        Assert.Equal(CardState.Failed, card.State);
        Assert.Equal("Källan bad Chainsaw vänta.", card.Problem);
    }

    [Fact]
    public async Task Alt_click_turns_a_card_offline_from_the_case_and_again_turns_it_back()
    {
        var graph = await Open();
        _state.Holdings = new NodeHoldings([Btc(0.05m)], null, Now, true, null);

        graph.ToggleTurn(VKey);
        graph.ToggleTurn(UKey);

        Assert.True(graph.IsTurned(VKey));
        Assert.True(graph.IsTurned(UKey));
        Assert.Equal(CardState.Shown, graph.Cards[VKey].State);
        Assert.Equal(CardRateKind.None, graph.Cards[VKey].Rate);
        Assert.Equal(0, _state.RateCalls);

        graph.ToggleTurn(VKey);
        Assert.False(graph.IsTurned(VKey));
        Assert.True(graph.IsTurned(UKey));
    }

    [Fact]
    public async Task Online_the_card_asks_for_todays_rate_through_the_exit()
    {
        var graph = await Open(live: true);
        _state.Holdings = new NodeHoldings([Btc(2m)], new ChainTip(900_000, "00", Now), Now, false, null);
        _state.Today[Asset.Bitcoin] = Rate(Asset.Bitcoin, 1_000_000m, Now.AddHours(-1));

        graph.ToggleTurn(VKey);

        var card = graph.Cards[VKey];
        Assert.Equal(1, _state.RateCalls);
        Assert.Equal(CardRateKind.Today, card.Rate);
        Assert.Equal("2 000 000 kr", card.Rows[0].Value);
    }

    [Fact]
    public async Task A_pile_or_a_transaction_has_no_back_and_a_new_case_forgets_the_cards()
    {
        var graph = await Open();
        graph.ToggleTurn("nowhere");
        Assert.Empty(graph.Cards);

        graph.ToggleTurn(VKey);
        _session.Forget();

        Assert.Empty(graph.Cards);
    }

    // ===== Block marks (GR9a) =====

    [Fact]
    public void A_line_whose_transaction_moved_gets_its_mark_with_before_and_now()
    {
        var change = Moved(Tx[1]);

        var marks = BlockMarks.ForLines(Scene(), [change], Now, Utc);

        Assert.Equal([VK, VU], marks.Keys.Order(StringComparer.Ordinal));
        var mark = marks[VK];
        Assert.Equal(BlockMarks.MovedTitle, mark.Title);
        Assert.Equal("Icon.BlockMoved", mark.Icon);
        Assert.False(mark.IsGone);
        Assert.Equal("921 340", mark.Rows[0].Height);
        Assert.Equal("000011…11113fa1", mark.Rows[0].Hash);
        Assert.True(mark.Rows[0].IsBefore);
        Assert.Equal("921 341", mark.Rows[1].Height);
        Assert.Equal("sett 13:51", mark.Rows[0].When);
    }

    [Fact]
    public void The_gravest_change_on_a_line_wins_and_a_gone_transaction_has_no_block()
    {
        var gone = new BlockChange(Tx[1], BlockChangeKind.Gone, Sighting(921_340, Hash('a'), 1),
            new BlockSighting(null, null, Now, 2));
        var unconfirmed = new BlockChange(Tx[1], BlockChangeKind.Unconfirmed, Sighting(921_340, Hash('a'), 1),
            new BlockSighting(null, null, Now, 3));

        Assert.True(BlockMarks.Of(gone, Now, Utc).IsGone);
        Assert.Equal("okänd för källan", BlockMarks.Of(gone, Now, Utc).Rows[1].Hash);
        Assert.Equal("mempool", BlockMarks.Of(unconfirmed, Now, Utc).Rows[1].Height);
        Assert.Equal(BlockMarks.GoneTitle, BlockMarks.ForLines(Scene(), [gone], Now, Utc)[VK].Title);
    }

    [Fact]
    public async Task The_graph_reads_its_marks_and_the_newest_block_from_the_case()
    {
        _state.Marks = new ChainMarksReading([Moved(Tx[2])], new ChainTip(921_344, "00", Now));

        var graph = await Open();

        Assert.True(graph.BlockMarksByLine.ContainsKey(KE));
        Assert.Equal(921_344, graph.Timeline.NewestBlock!.Height);
        graph.ToggleBlockMark(KE);
        Assert.Equal(KE, graph.OpenBlockMark);
        graph.ToggleBlockMark(KE);
        Assert.Null(graph.OpenBlockMark);
    }

    // ===== Väntar på källan (GR9a) =====

    [Fact]
    public void A_wait_is_shown_only_when_every_source_waits_or_does_not_answer()
    {
        var waits = View((Now.AddSeconds(34), false), (Now.AddSeconds(48), false), (null, true));
        var oneAnswers = View((Now.AddSeconds(34), false), (null, false));
        var noneWait = View((null, true), (null, true));

        var wait = SourceWait.Read(waits, Now);

        Assert.NotNull(wait);
        Assert.Equal("34 s", wait!.LeftText(Now));
        Assert.Equal("₿", wait.Glyph);
        Assert.Equal([SourceWait.WaitsText, SourceWait.WaitsText, SourceWait.SilentText], wait.Rows.Select(r => r.Why));
        Assert.Equal("–", wait.Rows[2].LeftText(Now));
        Assert.Equal(1, wait.LeftShare(Now));
        Assert.Equal(0.5, wait.LeftShare(Now.AddSeconds(17)), 3);
        Assert.Null(SourceWait.Read(oneAnswers, Now));
        Assert.Null(SourceWait.Read(noneWait, Now));
        Assert.Null(SourceWait.Read(waits with { Mode = SourceMode.Off }, Now));
    }

    [Fact]
    public async Task The_graph_counts_the_wait_down_and_gives_the_expanding_node_its_hourglass()
    {
        var graph = await Open();
        _sources.View = View((Now.AddSeconds(30), false));
        _sources.Raise();

        Assert.True(graph.HasWait);
        Assert.Equal("30 s", graph.WaitLeftText);
        _clock.UtcNow = Now.AddSeconds(10);
        graph.TickWait();
        Assert.Equal("20 s", graph.WaitLeftText);
        Assert.False(graph.IsWaitingNode(VKey));

        _clock.UtcNow = Now.AddSeconds(31);
        graph.TickWait();
        Assert.False(graph.HasWait);
    }

    [Fact]
    public async Task The_node_under_expansion_gets_the_hourglass_while_its_sources_wait()
    {
        var graph = await Open();
        _source.ExpandGate = new TaskCompletionSource();
        _sources.View = View((Now.AddSeconds(30), false));
        _sources.Raise();
        graph.Choose(UKey, null);

        var expansion = graph.ExpandAsync();

        Assert.True(graph.IsExpanding);
        Assert.True(graph.IsWaitingNode(UKey));
        Assert.False(graph.IsWaitingNode(VKey));
        _source.ExpandGate.SetResult();
        await expansion;
        Assert.False(graph.IsWaitingNode(UKey));
    }

    [Fact]
    public async Task A_new_pause_that_ends_later_starts_the_ring_full_again()
    {
        var graph = await Open();
        _sources.View = View((Now.AddSeconds(30), false));
        _sources.Raise();
        _clock.UtcNow = Now.AddSeconds(20);
        _sources.View = View((Now.AddSeconds(60), false));
        _sources.Raise();

        Assert.Equal(1, graph.WaitLeftShare, 3);
    }

    [Fact]
    public async Task Online_without_a_new_rate_the_card_says_so_and_not_offline()
    {
        var graph = await Open(live: true);
        _state.Holdings = new NodeHoldings([Btc(1m)], null, Now, false, null);

        graph.ToggleTurn(VKey);

        var card = graph.Cards[VKey];
        Assert.Equal(CardRateKind.None, card.Rate);
        Assert.Equal(HoldingsCards.NoRateOnlineText, card.RateText);
    }

    [Fact]
    public async Task An_answer_for_an_earlier_turn_of_the_same_card_is_dropped()
    {
        var graph = await Open();
        var first = new TaskCompletionSource<NodeHoldings>();
        _state.Pending = first;
        graph.ToggleTurn(VKey);
        graph.ToggleTurn(VKey);
        _state.Pending = null;
        _state.Holdings = new NodeHoldings([Btc(2m)], null, Now, true, null);
        graph.ToggleTurn(VKey);

        first.SetResult(new NodeHoldings([Btc(9m)], null, Now, true, null));
        await Task.Yield();

        Assert.Equal("2,0000", graph.Cards[VKey].Rows[0].Head);
    }

    [Fact]
    public async Task An_older_reading_of_the_marks_is_dropped()
    {
        var gate = new TaskCompletionSource<ChainMarksReading>();
        _state.MarksGate = gate;
        var graph = await Open();
        _state.MarksGate = null;
        _state.Marks = new ChainMarksReading([], new ChainTip(5, "00", Now));
        await graph.ReloadAsync(fit: false);

        gate.SetResult(new ChainMarksReading([Moved(Tx[2])], new ChainTip(1, "00", Now)));
        await Task.Yield();

        Assert.Equal(5, graph.Timeline.NewestBlock!.Height);
        Assert.Empty(graph.BlockMarksByLine);
    }

    [Fact]
    public void Pending_money_stands_as_a_clock_on_its_row()
    {
        var holdings = new NodeHoldings([], null, Now, false, null)
        {
            Pending = Btc(0.01m),
        };

        var card = HoldingsCards.Build("n", holdings, new Dictionary<Asset, Price>(), CardRateKind.FromCase, null, Now, Utc);

        var row = Assert.Single(card.Rows);
        Assert.StartsWith(HoldingsCards.PendingPrefix + "+0,0100 BTC", row.Pending, StringComparison.Ordinal);
    }

    // ===== The strip's lens (TL2a) =====    // ===== The strip's lens (TL2a) =====

    [Fact]
    public void The_lens_gives_its_window_more_than_half_the_strip_and_squeezes_the_rest()
    {
        var axis = new TimeAxis(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), Utc);
        var ticks = new[] { axis.Time(10), axis.Time(400), axis.Time(900) };
        var window = new StripWindow(352, 448);

        var strip = new TimeStripLayout(axis, ticks, 0, 1000, window, newestBlock: 1200);

        Assert.Equal(1200, strip.To);
        Assert.Equal(1000, strip.EndX, 6);
        Assert.Equal(TimeStripLayout.LensShare * 1000, strip.X(448) - strip.X(352), 6);
        Assert.Equal(0, strip.X(0), 6);
        foreach (var hours in new[] { 100d, 380, 700 })
        {
            Assert.Equal(hours, strip.HoursAt(strip.X(hours)), 6);
        }

        var marks = strip.LensMarks();
        Assert.Contains(marks, mark => mark.IsDay && mark.Name == "16 september");
        Assert.Contains(marks, mark => mark is { IsDay: false, Name: "06" });
        Assert.DoesNotContain(strip.Days(46), day => day.X > strip.X(352) && day.X < strip.X(448));
    }

    [Fact]
    public void Without_a_newer_block_the_strip_ends_a_little_after_the_last_transaction()
    {
        var axis = new TimeAxis(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), Utc);
        var strip = new TimeStripLayout(axis, [axis.Time(10), axis.Time(40)], 0, 100, newestBlock: 20);

        Assert.Null(strip.NewestBlock);
        Assert.Equal(40 + TimeStripLayout.LeastTail, strip.To, 6);
        Assert.Empty(strip.LensMarks());
    }

    [Fact]
    public void The_lens_steps_through_its_windows_and_then_closes()
    {
        var timeline = new GraphTimelineViewModel(Utc);
        timeline.ShowLens(Now, 48, Now, [Now.AddHours(-3)]);

        Assert.Equal("Valvyn · ±48 h", timeline.LensText);
        timeline.StepLens();
        Assert.Equal("Valvyn · ±7 d", timeline.LensText);
        timeline.StepLens();
        Assert.Equal("Valvyn · ±6 h", timeline.LensText);
        timeline.StepLens();
        Assert.False(timeline.HasLens);
        Assert.Null(timeline.LensWindow);
    }

    // ===== Helpers =====

    /// <summary>Opens a case in a graph wired to the fakes.</summary>
    /// <param name="live">Whether the exit lets lookups go now (Windows with a network).</param>
    /// <returns>The view model.</returns>
    private async Task<GraphViewModel> Open(bool live = false)
    {
        var route = new TestExitRoute();
        if (live)
        {
            route.Choose(ExitRoute.Windows);
        }

        var source = _source;
        var workflow = new CaseWorkflow(
            new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), _session);
        var graph = new GraphViewModel(
            _session, workflow, source, TestWays.For(_exit, route), new InlineUi(), new TestGraphEditor(), _clock,
            chainState: _state, chainSources: _sources)
        {
            Zone = Utc,
        };
        _session.Open(new TestCase("2026-0117"));
        await graph.ReloadAsync(fit: false);
        return graph;
    }

    /// <summary>A transaction moved from one block to the next.</summary>
    /// <param name="txid">The transaction.</param>
    /// <returns>The change.</returns>
    private static BlockChange Moved(Txid txid) => new(
        txid, BlockChangeKind.Moved,
        new BlockSighting(921_340, "0000" + new string('1', 56) + "3fa1", new DateTimeOffset(2026, 11, 8, 13, 51, 0, TimeSpan.Zero), 1),
        Sighting(921_341, Hash('2'), 2));

    /// <summary>A sighting at a height.</summary>
    /// <param name="height">The height.</param>
    /// <param name="hash">The hash.</param>
    /// <param name="sequence">The answer's number.</param>
    /// <returns>The sighting.</returns>
    private static BlockSighting Sighting(long height, string hash, long sequence) => new(height, hash, Now, sequence);

    /// <summary>A made-up hash.</summary>
    /// <param name="digit">Its digit.</param>
    /// <returns>The hash.</returns>
    private static string Hash(char digit) => new(digit, 64);

    /// <summary>Bitcoin's sources with endpoints that wait until a time or failed.</summary>
    /// <param name="endpoints">Each endpoint's wait and whether it failed.</param>
    /// <returns>The view.</returns>
    private static ChainSourcesView View(params (DateTimeOffset? Until, bool Failed)[] endpoints) => new(
        Chain.Bitcoin,
        SourceMode.Fastest,
        [.. endpoints.Select((endpoint, index) => new EndpointView(
            new SourceEndpoint(Guid.NewGuid(), Chain.Bitcoin, $"e{index}", new Uri($"https://e{index}.invalid/"),
                "esplora", null, true),
            null, endpoint.Failed, index == 0) { WaitUntil = endpoint.Until })],
        null);

    /// <summary>A rate of an asset, in kronor, for an hour that ended at a time.</summary>
    /// <param name="asset">The asset.</param>
    /// <param name="sek">One unit in kronor.</param>
    /// <param name="end">When the hour ended.</param>
    /// <returns>The price.</returns>
    private static Price Rate(Asset asset, decimal sek, DateTimeOffset end)
    {
        var at = end.AddHours(-1);
        var origin = new RateOrigin(1, "test", new Uri("https://rates.invalid/"), 200, at, at,
            new OperatorProfile("Test", "Analytiker", "Enheten"), new string('0', 64));
        var crypto = new CryptoRate(asset.Chain, FiatCurrency.Sek, PriceResolution.Hour, at, end, "test",
            sek, sek, sek, sek, 1m, origin) { Asset = asset };
        return new Price(at, FiatCurrency.Sek, crypto, null);
    }

    /// <summary>The graph's way to the chains, answering what the test set.</summary>
    private sealed class FakeChainState : IGraphChainState
    {
        public NodeHoldings Holdings { get; set; } = new([], null, Now, false, null);

        public Dictionary<Asset, Price> Today { get; } = [];

        public ChainMarksReading Marks { get; set; } = ChainMarksReading.Empty;

        public int RateCalls { get; private set; }

        public TaskCompletionSource<NodeHoldings>? Pending { get; set; }

        public TaskCompletionSource<ChainMarksReading>? MarksGate { get; set; }

        public Task<NodeHoldings> HoldingsAsync(ICase @case, GraphNode node, CancellationToken cancellationToken) =>
            Pending?.Task ?? Task.FromResult(Holdings);

        public Task<IReadOnlyDictionary<Asset, Price>> TodayRatesAsync(
            ICase @case, IReadOnlyCollection<Asset> assets, DateTimeOffset now, CancellationToken cancellationToken)
        {
            RateCalls++;
            return Task.FromResult<IReadOnlyDictionary<Asset, Price>>(Today);
        }

        public Task<ChainMarksReading> ReadMarksAsync(ICase @case, CancellationToken cancellationToken) =>
            MarksGate?.Task ?? Task.FromResult(Marks);
    }

    /// <summary>Bitcoin's sources as the test sets them.</summary>
    private sealed class FakeSources : IChainSources
    {
        public ChainSourcesView View { get; set; } = ChainStateTests.View((null, false));

        public IReadOnlyList<Chain> Chains { get; } = [Chain.Bitcoin];

        public event EventHandler<Chain>? Changed;

        public void Raise() => Changed?.Invoke(this, Chain.Bitcoin);

        public IReadOnlyList<SourceApiInfo> Apis(Chain chain) => [];

        public ChainSourcesView Get(Chain chain) => View;

        public Task SetModeAsync(Chain chain, SourceMode mode) => throw new NotSupportedException();

        public (SourceEndpoint Endpoint, Task Saved) Add(
            Chain chain, Uri address, string api, string? apiKey, bool local = false) =>
            throw new NotSupportedException();

        public Task RemoveAsync(Chain chain, Guid id) => throw new NotSupportedException();

        public Task SetEnabledAsync(Chain chain, Guid id, bool enabled) => throw new NotSupportedException();

        public Task MoveAsync(Chain chain, Guid id, int steps) => throw new NotSupportedException();

        public Task<ProbeRun> ProbeAsync(Chain chain, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EndpointProbe> TryAsync(
            Chain chain, Uri address, string api, string? apiKey, bool local = false,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
