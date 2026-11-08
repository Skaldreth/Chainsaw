using Chainsaw.App.Graph;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

/// <summary>
/// The inspector as rebuilt after IN2b, GR5 and TK1a (TODO 39.2): the line under the heading, the name in grey when it
/// is only the address, the facts with Tx and the latest block, the ids in their own box with the block under a txid,
/// and the balance per token with what is left worth at the case's latest rate.
/// </summary>
public sealed class InspectorContentTests
{
    /// <summary>The ordinal comparison every text check uses.</summary>
    private const StringComparison Ordinal = StringComparison.Ordinal;

    [Fact]
    public void An_unnamed_start_point_says_its_sort_chain_and_start_under_a_grey_heading()
    {
        var scene = GraphSamples.Scene();
        var start = scene.Node(GraphSamples.VKey)!;

        var content = InspectorContent.ForNode(scene, start);

        Assert.Equal("Okänd · Bitcoin · " + InspectorContent.StartPointWord, content.Subtitle);
        Assert.False(content.IsNamed);
        Assert.Equal(start.Name, content.Title);
        Assert.True(content.IsStartPoint);
    }

    [Fact]
    public void A_named_node_is_headed_by_its_name_and_a_cluster_counts_its_addresses()
    {
        var scene = GraphSamples.Scene();
        var cluster = scene.Node(GraphSamples.KKey)!;

        var content = InspectorContent.ForNode(scene, cluster);

        Assert.Contains("Bitcoin · 2 adresser", content.Subtitle, Ordinal);
        Assert.DoesNotContain(InspectorContent.StartPointWord, content.Subtitle, Ordinal);
        Assert.Equal(cluster.IsNamed, content.IsNamed);
    }

    [Fact]
    public void A_node_counts_its_transactions_and_says_its_latest_block()
    {
        var scene = GraphSamples.Scene();

        var content = InspectorContent.ForNode(scene, scene.Node(GraphSamples.VKey)!);

        Assert.Contains(new InspectorFact("Tx", "2"), content.Facts);
        Assert.Contains(new InspectorFact("Senast", "block 101"), content.Facts);
        Assert.DoesNotContain(content.Facts, fact => fact.Name == "Sort");
        Assert.Equal(["In", "Ut", "Spårat in"], content.Facts.Take(3).Select(fact => fact.Name));
    }

    [Fact]
    public void A_node_writes_its_address_in_full_once_without_a_note()
    {
        var scene = GraphSamples.Scene();

        var content = InspectorContent.ForNode(scene, scene.Node(GraphSamples.VKey)!);

        var id = Assert.Single(content.Ids);
        Assert.Equal(GraphSamples.V.Value, id.Value);
        Assert.False(id.HasNote);
        Assert.True(content.HasIds);
        Assert.False(content.HasMoreIds);
    }

    [Fact]
    public void A_line_puts_each_block_under_its_txid_and_says_its_chain()
    {
        var scene = GraphSamples.Scene();

        var content = InspectorContent.ForEdge(scene, scene.Edge(GraphSamples.VU)!);

        var id = Assert.Single(content.Ids);
        Assert.Equal(GraphSamples.Tx[1].Value, id.Value);
        Assert.StartsWith("Block 101 · ", id.Note, Ordinal);
        Assert.Equal([id.Value, id.Note!], content.Details);
        Assert.Equal(InspectorContent.LineWord + " · Bitcoin", content.Subtitle);
    }

    [Fact]
    public void An_account_says_ethereum_under_its_heading_and_no_chain_fact()
    {
        var scene = EthSamples.Scene();

        var content = InspectorContent.ForNode(scene, scene.Node(EthSamples.Key(EthSamples.S))!);

        Assert.StartsWith(content.Subtitle.Split(" · ")[0] + " · Ethereum", content.Subtitle, Ordinal);
        Assert.DoesNotContain(content.Facts, fact => fact.Name == "Kedja");
        Assert.Contains(content.Facts, fact => fact.Name == "Tx");
    }

    [Fact]
    public void What_is_left_is_valued_at_the_rate_and_the_tip_says_how()
    {
        var usdt = KnownTokens.EthereumUsdt;
        var rate = Rate(usdt, 10.5m);

        var row = TokenMarks.Balance(new AssetAmount(usdt, 20_000_000), AssetAmount.Zero(usdt), rate);

        Assert.True(row.HasValue);
        Assert.Equal("≈ 210 kr", row.Value!.Replace(' ', ' '));
        Assert.Contains("senaste kursen i ärendet", row.ValueTip, Ordinal);
        Assert.Contains("10,5", row.ValueTip, Ordinal);
        Assert.Equal("20,0000 USDT", row.LeftWithUnit.Replace(' ', ' '));
        Assert.False(row.IsEmpty);
        Assert.StartsWith("In ", row.ToolTip, Ordinal);
    }

    [Fact]
    public void Nothing_left_is_dimmed_and_a_rate_of_another_asset_gives_no_value()
    {
        var usdt = KnownTokens.EthereumUsdt;
        var spent = new AssetAmount(usdt, 5);

        var empty = TokenMarks.Balance(spent, spent, Rate(usdt, 10m));
        var other = TokenMarks.Balance(spent, AssetAmount.Zero(usdt), Rate(Asset.Ether, 30_000m));
        var none = TokenMarks.Balance(spent, AssetAmount.Zero(usdt));

        Assert.True(empty.IsEmpty);
        Assert.False(other.HasValue);
        Assert.False(none.HasValue);
        Assert.Null(none.ValueTip);
    }

    [Fact]
    public void An_unknown_balance_is_written_alone_without_a_unit()
    {
        var usdt = KnownTokens.EthereumUsdt;

        var row = TokenMarks.Balance(new AssetAmount(usdt, 1), new AssetAmount(usdt, 5), Rate(usdt, 10m));

        Assert.Equal(TokenMarks.UnknownLeft, row.LeftWithUnit);
        Assert.False(row.HasValue);
    }

    [Fact]
    public async Task The_source_keeps_the_latest_rate_and_the_inspector_values_what_is_left()
    {
        var tracing = new TestTracing { Graph = EthSamples.Graph() };
        var prices = new TestPrices();
        prices.Rates[KnownTokens.EthereumUsdt] = 10.5m;
        var source = new GraphSource(tracing, new TestAttribution(), new TestClustering(), null, prices);

        var scene = await source.LoadAsync(new TestCase("2026-0001"), TracingMethodKind.Fifo, CancellationToken.None);

        var rate = scene.Snapshot.Rates[KnownTokens.EthereumUsdt];
        var latest = EthSamples.Graph().Edges.SelectMany(edge => edge.Transfers)
            .Where(transfer => transfer.Amount.Asset == KnownTokens.EthereumUsdt)
            .Max(transfer => transfer.Time);
        Assert.Equal(latest, rate.At);
        Assert.False(scene.Snapshot.Rates.ContainsKey(Asset.Ether));
        var scammer = scene.Node(EthSamples.Key(EthSamples.S))!;
        var row = Assert.Single(scammer.Balances, balance => balance.Name == "USDT");
        Assert.True(row.CanBeFrozen);
        Assert.StartsWith("≈ ", row.Value, Ordinal);
        Assert.All(
            scammer.Balances.Where(balance => balance.Name != "USDT"), balance => Assert.False(balance.HasValue));
    }

    /// <summary>A made-up rate for an asset, from a made-up source.</summary>
    /// <param name="asset">The asset.</param>
    /// <param name="sek">One unit in SEK.</param>
    /// <returns>The price.</returns>
    private static Price Rate(Asset asset, decimal sek)
    {
        var at = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        var origin = new RateOrigin(1, "test", new Uri("https://rates.invalid/"), 200, at, at,
            new OperatorProfile("Test", "Analytiker", "Enheten"), new string('0', 64));
        var crypto = new CryptoRate(asset.Chain, FiatCurrency.Sek, PriceResolution.Hour, at, at.AddHours(1), "test",
            sek, sek, sek, sek, 1m, origin) { Asset = asset };
        return new Price(at, FiatCurrency.Sek, crypto, null);
    }
}
