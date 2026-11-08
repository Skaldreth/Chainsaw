using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

/// <summary>
/// The rates in the graph after KU1a: kronor under the amount on Bitcoin's lines, <c>– kr</c> with the reason when a
/// rate is missing, <em>Varför?</em> with the steps and their hashes, and the count on the settings' page Kurser.
/// </summary>
public sealed class RatesTests
{
    /// <summary>A made-up SHA-256 of a sealed answer.</summary>
    private const string Hash = "9c1e07b2aa00bb11cc22dd33ee44ff5566778899aabbccddeeff00112233440b";

    [Fact]
    public async Task Bitcoin_lines_are_valued_in_kronor_transfer_by_transfer()
    {
        var prices = new TestPrices();
        prices.Rates[Chain.Bitcoin.NativeAsset()] = 700_000m;
        var source = Source(prices);

        var scene = await source.LoadAsync(new TestCase("2026-0001"), TracingMethodKind.Fifo, CancellationToken.None);

        var line = scene.Edge(GraphSamples.VK)!;
        Assert.Equal(420_000m, line.Value!.Rounded);
        Assert.Equal("≈ 420\u00A0000 kr", line.ValueLine);
        var part = Assert.Single(line.Value.Parts);
        Assert.Equal(line.Edge.Transfers[0], part.Transfer);
        Assert.Null(line.ValueGap);
        Assert.All(prices.Asked, asked => Assert.Equal(PriceLookupMode.CaseOnly, asked.Mode));
        Assert.Contains("Värde", InspectorContent.ForEdge(scene, line).Facts.Select(fact => fact.Name));
    }

    [Fact]
    public async Task A_missing_rate_is_said_offline_and_never_made_up()
    {
        var prices = new TestPrices { Missing = PriceLookupStatus.Offline };
        var source = Source(prices);

        var scene = await source.LoadAsync(new TestCase("2026-0001"), TracingMethodKind.Fifo, CancellationToken.None);

        var line = scene.Edge(GraphSamples.VK)!;
        Assert.Null(line.Value);
        Assert.Equal("– kr", line.ValueLine);
        Assert.True(line.ValueGap!.IsOffline);
        Assert.Equal("Icon.Offline", line.ValueGap.Icon);
        Assert.Contains("2026-11-01 13:00 UTC", line.ValueGap.Tip, StringComparison.Ordinal);
        Assert.Contains("Offline", line.ValueGap.Tip, StringComparison.Ordinal);
        Assert.Contains(
            InspectorContent.ForEdge(scene, line).Why,
            why => why.Text.StartsWith("Ingen kurs: ", StringComparison.Ordinal) && why.Topic == GuideTopic.Prices);
    }

    [Fact]
    public void The_gaps_say_why_and_an_asset_without_a_source_shows_nothing()
    {
        var at = new DateTimeOffset(2026, 11, 8, 9, 58, 0, TimeSpan.Zero);
        var now = at.AddHours(1);

        var notFetched = EdgeValues.GapOf(Lookup(PriceLookupStatus.NotInCase), at, "SEK", now)!;
        var notSet = EdgeValues.GapOf(Lookup(PriceLookupStatus.NotAvailable, "Inte satt."), at, "SEK", now)!;
        var failed = EdgeValues.GapOf(Lookup(PriceLookupStatus.Failed), at, "EUR", now)!;

        Assert.False(notFetched.IsOffline);
        Assert.True(notFetched.IsPending);
        Assert.Equal("Icon.Clock", notFetched.Icon);
        Assert.Contains("hämtas genom utgången", notFetched.Tip, StringComparison.Ordinal);
        Assert.False(notSet.IsPending);
        Assert.EndsWith("Inte satt.", notSet.Tip, StringComparison.Ordinal);
        Assert.Equal("– EUR", failed.Text);
        Assert.Null(EdgeValues.GapOf(Lookup(PriceLookupStatus.Unsupported), at, "SEK", now));
        Assert.Equal(EdgeValues.UnconfirmedText, EdgeValues.Unconfirmed("SEK").Tip);
        Assert.Throws<ArgumentException>(() => EdgeValues.Sum([]));
    }

    [Fact]
    public void A_block_time_ahead_of_the_clock_is_told_apart_from_an_asset_without_a_source()
    {
        var at = new DateTimeOffset(2026, 11, 8, 11, 0, 0, TimeSpan.Zero);

        var ahead = EdgeValues.GapOf(Lookup(PriceLookupStatus.Unsupported), at, "SEK", at.AddMinutes(-30))!;

        Assert.False(ahead.IsPending);
        Assert.Equal("Icon.Clock", ahead.Icon);
        Assert.EndsWith(EdgeValues.AheadText, ahead.Tip, StringComparison.Ordinal);
    }

    [Fact]
    public void Offline_a_waiting_rate_says_offline_and_waits_for_the_exit()
    {
        var at = new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);
        var waiting = EdgeValues.GapOf(Lookup(PriceLookupStatus.NotInCase), at, "SEK", at.AddHours(2))!;
        var failed = EdgeValues.GapOf(Lookup(PriceLookupStatus.Failed), at, "SEK", at.AddHours(2))!;

        var offline = EdgeValues.Offline(new Dictionary<string, EdgeValueGap> { ["a"] = waiting, ["b"] = failed });

        Assert.True(offline["a"].IsOffline);
        Assert.True(offline["a"].IsPending);
        Assert.Equal("Icon.Offline", offline["a"].Icon);
        Assert.EndsWith("är inte hämtad · Offline.", offline["a"].Tip, StringComparison.Ordinal);
        Assert.Same(failed, offline["b"]);
    }

    [Fact]
    public async Task Showing_and_expanding_the_graph_never_wait_for_a_source()
    {
        var prices = new TestPrices { AtSourceOnly = true };
        prices.Rates[Chain.Bitcoin.NativeAsset()] = 700_000m;
        var source = Source(prices);

        var scene = await source.LoadAsync(new TestCase("2026-0001"), TracingMethodKind.Fifo, CancellationToken.None);
        var expansion = await source.ExpandAsync(
            new TestCase("2026-0001"), [ExpansionRequest.ForAddress(GraphSamples.V)], TracingMethodKind.Fifo,
            CancellationToken.None);

        Assert.All(prices.Asked, asked => Assert.Equal(PriceLookupMode.CaseOnly, asked.Mode));
        Assert.True(scene.Edge(GraphSamples.VK)!.ValueGap!.IsPending);
        Assert.Equal(scene.PendingRates, expansion.Scene.PendingRates);
        Assert.True(scene.PendingRates > 0);
    }

    [Fact]
    public async Task Rates_in_the_case_never_count_against_the_lookups_of_one_fetch()
    {
        var prices = new TestPrices { AtSourceOnly = true };
        prices.Rates[Chain.Bitcoin.NativeAsset()] = 700_000m;
        var graph = ManyTimes(GraphSource.MostRateLookups + 10);
        var times = graph.Edges.Select(edge => edge.Transfers[0].Time!.Value).ToList();
        foreach (var time in times.Take(GraphSource.MostRateLookups + 5))
        {
            prices.InCase.Add(time);
        }

        var source = Source(prices, graph);
        var scene = await source.LoadAsync(new TestCase("2026-0001"), TracingMethodKind.Fifo, CancellationToken.None);
        Assert.Equal(5, scene.PendingRates);

        var rates = await source.FillRatesAsync(new TestCase("2026-0001"), scene, CancellationToken.None);

        Assert.Equal(0, rates.Pending);
        Assert.Equal(graph.Edges.Count, rates.Values.Count);
        Assert.Equal(5, prices.Asked.Count(asked => asked.Mode == PriceLookupMode.AsNeeded));
    }

    [Fact]
    public async Task One_fetch_asks_the_source_for_at_most_the_limit_and_the_next_takes_the_rest()
    {
        var prices = new TestPrices { AtSourceOnly = true };
        prices.Rates[Chain.Bitcoin.NativeAsset()] = 700_000m;
        var graph = ManyTimes(GraphSource.MostRateLookups + 10);
        var source = Source(prices, graph);
        var scene = await source.LoadAsync(new TestCase("2026-0001"), TracingMethodKind.Fifo, CancellationToken.None);

        var first = await source.FillRatesAsync(new TestCase("2026-0001"), scene, CancellationToken.None);
        var second = await source.FillRatesAsync(
            new TestCase("2026-0001"), scene.WithRates(first), CancellationToken.None);

        Assert.Equal(10, first.Pending);
        Assert.Equal(0, second.Pending);
        Assert.Equal(
            GraphSource.MostRateLookups + 10, prices.Asked.Count(asked => asked.Mode == PriceLookupMode.AsNeeded));
    }

    [Fact]
    public async Task The_graph_shows_at_once_and_fills_in_the_rates_in_the_background()
    {
        var (graph, source, exit) = Rig();
        var pending = PendingScene();
        source.Scene = () => pending;
        source.Fill = scene => scene.Rates with
        {
            Values = new Dictionary<string, EdgeValue> { [GraphSamples.VK] = EdgeValues.Sum([Part()]) },
            Gaps = new Dictionary<string, EdgeValueGap>(),
        };
        exit.Set(new ExitStatus(ExitState.Connected, Guid.NewGuid(), null));

        await graph.ReloadAsync(fit: false);
        await graph.FillRatesAsync();

        Assert.True(source.Fills > 0);
        Assert.NotNull(graph.Scene!.Edge(GraphSamples.VK)!.Value);
        Assert.Equal(0, graph.Scene.PendingRates);
    }

    [Fact]
    public async Task Offline_the_graph_asks_nothing_and_says_offline_at_the_lines()
    {
        var (graph, source, _) = Rig();
        source.Scene = PendingScene;

        await graph.ReloadAsync(fit: false);

        Assert.True(graph.IsOffline);
        Assert.Equal(0, source.Fills);
        var gap = graph.Scene!.Edge(GraphSamples.VK)!.ValueGap!;
        Assert.True(gap.IsOffline);
        Assert.Equal("Icon.Offline", gap.Icon);
    }

    [Fact]
    public void A_missing_value_takes_room_under_the_amount()
    {
        var plain = GraphSamples.Scene().Edge(GraphSamples.VK)!;
        var gap = plain with { ValueGap = EdgeValues.Unconfirmed("SEK") };

        var without = AmountLayout.BoxAt(plain, new GraphPoint(0, 0));
        var with = AmountLayout.BoxAt(gap, new GraphPoint(0, 0));

        Assert.False(plain.HasValueLine);
        Assert.Equal(without.Height + AmountLayout.ValueHeight, with.Height);
    }

    [Fact]
    public void Why_shows_the_amount_the_two_rates_with_their_hashes_and_the_calculation()
    {
        var part = Part();

        var valuation = GuideValuation.Of(EdgeValues.Sum([part]), TimeZoneInfo.Utc)!;

        Assert.Equal("164\u00A0223 kr", valuation.Total);
        Assert.False(valuation.HasSeveralParts);
        var step = Assert.Single(valuation.Parts);
        Assert.True(step.Amount.IsFact);
        Assert.Equal("fakta", step.Amount.Source);
        Assert.Equal("Icon.Check", step.Amount.Icon);
        var tx = step.Amount.Rows[0];
        Assert.Equal("Tx", tx.Name);
        Assert.Equal(part.Transfer.Transaction.Id, tx.Copy);
        Assert.EndsWith("· utgång 0", tx.Value, StringComparison.Ordinal);
        Assert.Contains(step.Amount.Rows, row => row.Name == "Block" && row.CanCopy);
        Assert.Equal("Kursen BTC → EUR", step.CryptoRate.Title);
        Assert.Equal("Bitstamp", step.CryptoRate.Source);
        Assert.Equal("28\u00A0010,5 EUR", step.CryptoRate.Value);
        Assert.Contains(
            step.CryptoRate.Rows, row => row.Name == "Timme" && row.Value == "2026-11-01 · 13:00–14:00 UTC");
        Assert.Contains(step.CryptoRate.Rows, row => row.Value == "Timmens stängning");
        var answer = step.CryptoRate.Rows.Single(row => row.Name == "Svaret");
        Assert.Equal("9C1E 07B2 … 440B", answer.Value);
        Assert.Equal(Hash, answer.Copy);
        Assert.Contains(step.CryptoRate.Rows, row => row.Name == "Hämtad" && row.Value.EndsWith("genom Windows",
            StringComparison.Ordinal));
        Assert.Equal("Växelkursen EUR → SEK", step.FiatRate!.Title);
        Assert.Equal("Riksbanken", step.FiatRate.Source);
        Assert.Equal("Icon.Kind.Bank", step.FiatRate.Icon);
        Assert.Contains(step.FiatRate.Rows, row => row.Name == "Dag" && row.Value == "2026-11-01");
        Assert.Equal("0,512 × 28\u00A0010,5 × 11,451", step.Formula);
        Assert.Equal("= 164\u00A0223 kr", step.Value);
        Assert.Equal("164\u00A0223,10 kr, avrundat", step.Exact);
    }

    [Fact]
    public void A_weekend_says_which_banking_day_the_exchange_rate_is_from()
    {
        var step = GuideValuationStep.OfFiatRate(
            Fiat() with { AskedDate = new DateOnly(2026, 11, 2) }, TimeZoneInfo.Utc);

        Assert.Equal("2026-11-01, 2026-11-02 saknar kurs", step.Rows[0].Value);
    }

    [Fact]
    public void Why_for_a_value_opens_the_section_on_rates_at_the_steps()
    {
        var valuation = GuideValuation.Of(EdgeValues.Sum([Part()]), TimeZoneInfo.Utc)!;

        var request = GuideRequest.ForValue("v-k", valuation);
        var page = GuidePage.Of(GuideLibrary.Load(), new GuidePlace(
            request.Topic.FileName(), null, request.Context, request.Subject))!;

        Assert.Equal(GuideTopic.Prices, request.Topic);
        Assert.Equal(GuideRequest.ValueHeading, request.Heading);
        Assert.True(request.IsChoice);
        Assert.Equal("Varför 164\u00A0223 kr?", request.Context!.Label);
        Assert.Same(valuation, page.Valuation);
        Assert.True(page.HasValuation);
        Assert.False(page.HasPlainAnswers);
        Assert.NotNull(page.Section.FindAnchor(GuideRequest.ValueHeading));
    }

    [Fact]
    public void A_value_without_parts_has_no_steps_to_show()
    {
        Assert.Null(GuideValuation.Of(new EdgeValue(1m, "SEK", []), TimeZoneInfo.Utc));
        Assert.Equal("9C1E", GuideValuationStep.ShortHash("9c1e"));
    }

    [Fact]
    public void The_guide_panel_copies_a_full_id()
    {
        var panel = new GuidePanelViewModel(GuideLibrary.Load());
        var copied = new List<string>();
        panel.CopyWanted += (_, text) => copied.Add(text);

        panel.Copy(Hash);
        panel.Copy(" ");

        Assert.Equal([Hash], copied);
    }

    [Fact]
    public async Task The_settings_count_the_rates_in_the_open_case()
    {
        var session = new CaseSession();
        var prices = new TestPrices { Summary = new RateSummary(23, 2) };
        var rates = new RatesSummaryViewModel(session, prices);

        await rates.RefreshAsync();
        Assert.Equal(RatesSummaryViewModel.NoCaseText, rates.StoredText);

        session.Open(new TestCase("2026-0001"));
        await rates.RefreshAsync();

        Assert.Equal("23 kurser", rates.StoredText);
        Assert.Equal("23 svar från Bitstamp och 2 från Riksbanken, förseglade.", rates.StoredTip);
        Assert.Equal("1 kurs", RatesSummaryViewModel.Describe(new RateSummary(1, 1)));
    }

    /// <summary>Gives the graph's source over the made-up graph and its trace, with the rates given.</summary>
    /// <param name="prices">The rates.</param>
    /// <param name="graph">Another graph, or <see langword="null"/> for the made-up one.</param>
    /// <returns>The source.</returns>
    private static GraphSource Source(TestPrices prices, TraceGraph? graph = null) => new(
        new TestTracing { Graph = graph ?? GraphSamples.Graph(), Tracer = (_, _) => GraphSamples.Trace() },
        new TestAttribution(),
        new TestClustering(),
        null,
        prices);

    /// <summary>
    /// Gives the made-up graph with many lines from W to V, each one transfer in a block of its own hour, so that
    /// there are more times to price than one fetch may ask for.
    /// </summary>
    /// <param name="count">How many lines.</param>
    /// <returns>The graph.</returns>
    private static TraceGraph ManyTimes(int count)
    {
        var graph = GraphSamples.Graph();
        var edges = Enumerable.Range(0, count).Select(index =>
        {
            var block = GraphSamples.Block(200 + index);
            var txid = GraphSamples.TxidOf(500 + index);
            return new GraphEdge(
                $"many-{index}", GraphSamples.WKey, GraphSamples.VKey, GraphEdgeKind.Transfer,
                Satoshi.FromBitcoin(0.1m).ToAmount(),
                [AssetTransfer.OfCoin(txid, new Outpoint(txid, 0), Satoshi.FromBitcoin(0.1m), block)],
                block.Time,
                EdgeDirection.Forward);
        });
        return graph with { Edges = [.. edges] };
    }

    /// <summary>Gives the made-up scene with the line V → K waiting for its rate.</summary>
    /// <returns>The scene.</returns>
    private static GraphScene PendingScene()
    {
        var at = GraphSamples.Block(101).Time;
        var gap = EdgeValues.GapOf(Lookup(PriceLookupStatus.NotInCase), at, "SEK", at.AddHours(1))!;
        var scene = GraphSamples.Scene();
        var gaps = new Dictionary<string, EdgeValueGap> { [GraphSamples.VK] = gap };
        return scene.WithRates(scene.Rates with { Gaps = gaps });
    }

    /// <summary>Gives the graph's view model over a test source, with its exit, and a case open.</summary>
    /// <returns>The view model, the source and the exit.</returns>
    private static (GraphViewModel Graph, TestGraphSource Source, TestExitConnection Exit) Rig()
    {
        var session = new CaseSession();
        var workflow = new CaseWorkflow(
            new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), session);
        var source = new TestGraphSource();
        var exit = new TestExitConnection();
        var graph = new GraphViewModel(
            session, workflow, source, TestWays.For(exit), new InlineUi(), new TestGraphEditor(), new TestClock());
        session.Open(new TestCase("2026-0001"));
        return (graph, source, exit);
    }

    /// <summary>Gives an answer without a value.</summary>
    /// <param name="status">How it was answered.</param>
    /// <param name="problem">Why, or <see langword="null"/>.</param>
    /// <returns>The answer.</returns>
    private static PriceLookup<Price> Lookup(PriceLookupStatus status, string? problem = null) =>
        new(status, null, [], problem);

    /// <summary>Gives the line V → K's transfer valued through euro and the Riksbank.</summary>
    /// <returns>The part.</returns>
    private static EdgeValuePart Part()
    {
        var transfer = GraphSamples.Edge(GraphSamples.VK, GraphSamples.VKey, GraphSamples.KKey, 1, 0, 0.512m)
            .Transfers[0];
        var at = transfer.Time!.Value;
        var crypto = new CryptoRate(
            Chain.Bitcoin, FiatCurrency.Eur, PriceResolution.Hour, at, at.AddHours(1), "BTC/EUR",
            28_000m, 28_100m, 27_900m, 28_010.5m, 3m, Origin(1)) { SourceName = "Bitstamp" };
        var price = new Price(at, FiatCurrency.Sek, crypto, Fiat());
        return new EdgeValuePart(transfer, price.ValueOf(transfer.Amount));
    }

    /// <summary>Gives the Riksbank's made-up rate for 2026-11-01.</summary>
    /// <returns>The rate.</returns>
    private static FiatRate Fiat() => new(
        FiatCurrency.Eur, FiatCurrency.Sek, 11.451m, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1),
        "SEKEURPMI", Origin(2)) { SourceName = "Riksbanken" };

    /// <summary>Gives a sealed answer's origin, fetched through Windows.</summary>
    /// <param name="sequence">Its number.</param>
    /// <returns>The origin.</returns>
    private static RateOrigin Origin(long sequence) => new(
        sequence, "test", new Uri("https://rates.invalid/"), 200,
        new DateTimeOffset(2026, 11, 8, 10, 14, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 8, 11, 14, 0,
        TimeSpan.FromHours(1)), new OperatorProfile("Test", "Analytiker", "Enheten"), Hash) { Route = "windows" };
}
