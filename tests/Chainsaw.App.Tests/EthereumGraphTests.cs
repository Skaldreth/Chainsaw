using System.Globalization;
using System.Numerics;
using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

/// <summary>
/// A made-up trace on Ethereum (never real addresses): the victim V pays the scammer S 1 000 USDT and 2 ETH; S sends
/// 500 USDT to the mule M and swaps 400 USDT for 0.1 ETH at the pool P in one transaction.
/// </summary>
internal static class EthSamples
{
    public static readonly EthereumAddress V = Address('1');
    public static readonly EthereumAddress S = Address('2');
    public static readonly EthereumAddress M = Address('3');
    public static readonly EthereumAddress P = Address('4');

    public static EthereumAddress Address(char digit) => EthereumAddress.Parse("0x" + new string(digit, 40));

    public static EthereumTxHash Hash(int value) =>
        EthereumTxHash.Parse("0x" + value.ToString("x64", CultureInfo.InvariantCulture));

    public static AssetAmount Usdt(decimal amount) =>
        new(KnownTokens.EthereumUsdt, new BigInteger(amount * 1_000_000m));

    public static AssetAmount Ether(decimal amount) =>
        new(Asset.Ether, new BigInteger(amount * 1_000_000_000_000_000_000m));

    public static EthereumBlockReference Block(long height) => new(
        height, "0x" + height.ToString("x64", CultureInfo.InvariantCulture),
        new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero).AddMinutes(height));

    public static string Key(EthereumAddress address) => GraphScene.AccountKey(address.ToChainAddress());

    public static EthereumTransfer Move(
        int tx, EthereumTransferKind kind, EthereumAddress from, EthereumAddress to, AssetAmount amount,
        long position = 0) =>
        new(Hash(tx), kind, from, to, amount, position, Block(100 + tx));

    /// <summary>The movements: 0 USDT V→S, 1 ETH V→S, 2 USDT S→M, 3 the swap (USDT S→P, ETH P→S).</summary>
    public static IReadOnlyList<EthereumTransfer> Transfers { get; } =
    [
        Move(0, EthereumTransferKind.Token, V, S, Usdt(1_000), 3),
        Move(1, EthereumTransferKind.Native, V, S, Ether(2)),
        Move(2, EthereumTransferKind.Token, S, M, Usdt(500), 5),
        Move(3, EthereumTransferKind.Token, S, P, Usdt(400), 7),
        Move(3, EthereumTransferKind.Internal, P, S, Ether(0.1m), 1),
    ];

    /// <summary>Gives the graph with the start at S, whose history is read when <paramref name="read"/>.</summary>
    public static TraceGraph Graph(bool read = true)
    {
        var nodes = new[] { V, S, M, P }.Select(address => new GraphNode(
                Key(address), GraphNodeKind.Address, [], 1, null, null, null, address == S,
                address == S ? 0 : 1, null,
                address == S && read ? NodeExpansion.Expanded : NodeExpansion.NotExpanded, [],
                new Dictionary<GraphLayout, NodePosition>())
            {
                Chain = Chain.Ethereum,
                Accounts = [address.ToChainAddress()],
            })
            .ToList();
        var edges = Transfers.GroupBy(transfer => (transfer.From, transfer.To, transfer.Amount.Asset))
            .Select(group => new GraphEdge(
                $"{group.Key.From.Value}-{group.Key.To.Value}-{group.Key.Asset.Symbol}",
                Key(group.Key.From), Key(group.Key.To), GraphEdgeKind.Transfer,
                group.Skip(1).Aggregate(group.First().Amount, (sum, transfer) => sum + transfer.Amount),
                [.. group.Select(transfer => AssetTransfer.OfEthereum(transfer, BlockOf(transfer.Block!)))],
                group.First().Block!.Time, EdgeDirection.Forward))
            .ToList();
        var fees = new List<EthereumFee>
        {
            new(Hash(2), S, Ether(0.001m), true, Block(102)),
            new(Hash(3), S, Ether(0.002m), true, Block(103)),
        };
        var swap = new TokenSwap(Hash(3), S.ToChainAddress(), Usdt(400), Ether(0.1m));
        return new TraceGraph(
            [TraceStartPoint.ForEthereumAddress(S)], nodes, edges,
            new UtxoGraph(new Dictionary<Txid, GraphTransaction>(), new Dictionary<Outpoint, CoinSpend>(),
                new Dictionary<Txid, IReadOnlyList<TrailEndHint>>()),
            [], [], [])
        {
            Account = new AccountGraph(
                Transfers, fees, new Dictionary<EthereumTxHash, EthereumTransaction>(),
                read ? new HashSet<ChainAddress> { S.ToChainAddress() } : new HashSet<ChainAddress>(), [swap]),
        };
    }

    /// <summary>Gives a block for the graph, with its hash.</summary>
    public static BlockReference BlockOf(EthereumBlockReference block) =>
        new(block.Height, BlockHash.TryParseEthereum(block.Hash, out var hash) ? hash : null!, block.Time);

    /// <summary>The edge key of a pair and asset in <see cref="Graph"/>.</summary>
    public static string EdgeKey(EthereumAddress from, EthereumAddress to, string symbol) =>
        $"{from.Value}-{to.Value}-{symbol}";

    /// <summary>A traced edge on Ethereum.</summary>
    public static TracedEdge Traced(string key, AssetAmount amount) => new(
        key, new TracedAmount(amount, new TracingMethodId(TracingMethodKind.Fifo, 1), [])
        {
            ThroughOther = [new ChainTransactionRef(Chain.Ethereum, Hash(2).Value)],
        }, false);

    /// <summary>The scene of the graph, with a trace of the USDT and a value on the line S→M.</summary>
    public static GraphScene Scene(bool read = true)
    {
        var graph = Graph(read);
        var trace = new AccountTraceResult(
            new TracingMethodId(TracingMethodKind.Fifo, 1),
            [AccountSeed.Of(Transfers[0])],
            [new AssetTrace(Usdt(1_000),
                [Traced(EdgeKey(V, S, "USDT"), Usdt(1_000)), Traced(EdgeKey(S, M, "USDT"), Usdt(500))],
                [])]);
        return GraphScene.Build(new GraphSnapshot(
            graph, new Dictionary<string, IReadOnlyList<Label>>(), TracingMethodKind.Fifo, [], null, null)
        {
            AccountSeeds = [AccountSeed.Of(Transfers[0])],
            AccountTrace = trace,
            Values = new Dictionary<string, EdgeValue>
            {
                [EdgeKey(S, M, "USDT")] = new(6_300m, "SEK", ["1 USDT = 10,5 SEK."]),
            },
        });
    }
}

/// <summary>A price service that values every asset it is given a rate for, and has none for the rest.</summary>
internal sealed class TestPrices : IPriceService
{
    public Dictionary<Asset, decimal> Rates { get; } = [];

    public List<(PriceRequest Request, PriceLookupMode Mode)> Asked { get; } = [];

    public Task<PriceLookup<Price>> GetPriceAsync(
        ICase @case, PriceRequest request, PriceLookupMode mode = PriceLookupMode.AsNeeded,
        CancellationToken cancellationToken = default)
    {
        Asked.Add((request, mode));
        var asset = request.Asset ?? request.Chain.NativeAsset();
        if (!Rates.TryGetValue(asset, out var rate))
        {
            return Task.FromResult(new PriceLookup<Price>(PriceLookupStatus.NotInCase, null, [], null));
        }

        var origin = new RateOrigin(1, "test", new Uri("https://rates.invalid/"), 200, request.At, request.At,
            new OperatorProfile("Test", "Analytiker", "Enheten"), new string('0', 64));
        var crypto = new CryptoRate(request.Chain, FiatCurrency.Sek, PriceResolution.Hour, request.At,
            request.At.AddHours(1), "test", rate, rate, rate, rate, 1m, origin) { Asset = asset };
        return Task.FromResult(new PriceLookup<Price>(
            PriceLookupStatus.FromCase, new Price(request.At, FiatCurrency.Sek, crypto, null), [origin], null));
    }
}

/// <summary>The graph on Ethereum after TK1a (stage 23b): marks, values, swaps, balances, seeds and the forms.</summary>
public sealed class EthereumGraphTests
{
    [Fact]
    public void A_line_carries_the_tokens_mark_first_and_its_value_small_under()
    {
        var scene = EthSamples.Scene();

        var line = scene.Edge(EthSamples.EdgeKey(EthSamples.S, EthSamples.M, "USDT"))!;

        Assert.Equal("₮", line.Glyph);
        Assert.Equal("≈ 6 300 kr", line.Value!.Text.Replace(' ', ' '));
        Assert.Equal(GraphInk.Accent, line.Ink);
        Assert.True(line.IsTraced);
        Assert.Null(scene.Edge(EthSamples.EdgeKey(EthSamples.V, EthSamples.S, "USDT"))!.Value);
        Assert.Equal("Ξ", scene.Edge(EthSamples.EdgeKey(EthSamples.V, EthSamples.S, "ETH"))!.Glyph);
    }

    [Fact]
    public void Lines_are_as_thick_as_their_amount_among_lines_of_the_same_asset()
    {
        var scene = EthSamples.Scene();

        var whole = scene.Edge(EthSamples.EdgeKey(EthSamples.V, EthSamples.S, "USDT"))!;
        var part = scene.Edge(EthSamples.EdgeKey(EthSamples.S, EthSamples.M, "USDT"))!;

        Assert.True(whole.Width > part.Width);
    }

    [Fact]
    public void The_swap_pill_sits_only_on_the_line_the_swapper_sold_on()
    {
        var scene = EthSamples.Scene();

        var sold = scene.Edge(EthSamples.EdgeKey(EthSamples.S, EthSamples.P, "USDT"))!;

        Assert.Equal("₮ → Ξ", sold.Swap);
        Assert.Single(sold.Swaps);
        Assert.All(scene.Edges.Where(edge => edge.Key != sold.Key), edge => Assert.Null(edge.Swap));
    }

    [Fact]
    public void An_expanded_account_shows_its_balance_per_token_and_the_snowflake()
    {
        var scene = EthSamples.Scene();

        var scammer = scene.Node(EthSamples.Key(EthSamples.S))!;

        Assert.Contains(NodeBadge.CanBeFrozen, scammer.Badges);
        var usdt = Assert.Single(scammer.Balances, row => row.Name == "USDT");
        Assert.True(usdt.CanBeFrozen);
        Assert.Contains("Tether", usdt.Note, StringComparison.Ordinal);
        var ether = Assert.Single(scammer.Balances, row => row.Name == "ETH");
        Assert.False(ether.CanBeFrozen);
        Assert.Equal(TokenMarks.NativeNote, ether.Note);
        Assert.Equal(AmountText.Number(EthSamples.Ether(2.097m)), ether.Left);
    }

    [Fact]
    public void An_account_whose_history_is_not_read_says_its_balance_is_unknown_and_gets_no_snowflake()
    {
        var scene = EthSamples.Scene(read: false);

        var mule = scene.Node(EthSamples.Key(EthSamples.M))!;
        var scammer = scene.Node(EthSamples.Key(EthSamples.S))!;

        Assert.DoesNotContain(NodeBadge.CanBeFrozen, mule.Badges);
        Assert.DoesNotContain(NodeBadge.CanBeFrozen, scammer.Badges);
        Assert.All(mule.Balances, row => Assert.Equal(GraphScene.NotReadNote, row.Note));
        Assert.All(mule.Balances, row => Assert.Equal("–", row.Left));
    }

    [Fact]
    public void An_ethereum_node_is_named_by_its_checksummed_address_and_its_traced_money_per_asset()
    {
        var scene = EthSamples.Scene();

        var mule = scene.Node(EthSamples.Key(EthSamples.M))!;

        Assert.StartsWith("0x3333", mule.Name, StringComparison.Ordinal);
        Assert.Equal(EthSamples.M.Checksummed, mule.Subject);
        Assert.Equal([EthSamples.Usdt(500)], mule.TracedIns);
    }

    [Fact]
    public void The_inspector_shows_the_chain_the_balances_and_expands_through_ethereum()
    {
        var scene = EthSamples.Scene();
        var scammer = scene.Node(EthSamples.Key(EthSamples.S))!;

        var content = InspectorContent.ForNode(scene, scammer);

        Assert.Contains(content.Facts, fact => fact.Name == "Kedja" && fact.Value == "Ethereum");
        Assert.DoesNotContain(content.Facts, fact => fact.Name == "In");
        Assert.True(content.HasBalances);
        Assert.True(content.HasFreezeText);
        Assert.Contains(content.Why, line => line.Topic == GuideTopic.CanBeFrozen);
        var request = Assert.Single(content.Expansion);
        Assert.Equal(EthereumAddress.Parse(EthSamples.S.Value), request.EthereumAddress);
        Assert.Contains(EthSamples.S.Checksummed, content.Details);
    }

    [Fact]
    public void The_inspector_of_a_line_on_ethereum_has_the_value_the_swap_and_the_blocks()
    {
        var scene = EthSamples.Scene();

        var swapKey = EthSamples.EdgeKey(EthSamples.S, EthSamples.P, "USDT");
        var swapLine = InspectorContent.ForEdge(scene, scene.Edge(swapKey)!);
        var valued = InspectorContent.ForEdge(
            scene, scene.Edge(EthSamples.EdgeKey(EthSamples.S, EthSamples.M, "USDT"))!);

        Assert.Contains(swapLine.Facts, fact => fact.Name == "Swap" && fact.Value == "₮ → Ξ");
        Assert.Contains(swapLine.Facts, fact => fact.Name == "Värde" && fact.Value == InspectorContent.NoRateText);
        Assert.DoesNotContain(swapLine.Facts, fact => fact.Name == "Kontroll");
        Assert.Contains(swapLine.Why, line => line.Topic == GuideTopic.Tokens && line.Text.Contains("Swap"));
        Assert.Contains(swapLine.Details, line => line.StartsWith("Block 103 · 0x", StringComparison.Ordinal));
        Assert.Contains(
            valued.Facts, fact => fact.Name == "Belopp" && fact.Value.StartsWith("₮ ", StringComparison.Ordinal));
        Assert.Contains(valued.Why, line => line.Topic == GuideTopic.Prices && line.Text.Contains("10,5"));
        Assert.Contains(valued.Why, line => line.Text.Contains("räknat genom 1 tx", StringComparison.Ordinal));
    }

    [Fact]
    public void The_seeds_are_what_came_to_the_start_address_and_never_what_its_money_reached()
    {
        var graph = EthSamples.Graph() with
        {
            StartPoints =
                [TraceStartPoint.ForEthereumAddress(EthSamples.S), TraceStartPoint.ForEthereumAddress(EthSamples.M)],
        };

        var seeds = AccountSeedPicker.Pick(graph);

        // The mule's USDT and the swap's ether back to S both come from accounts the seeds' money reaches.
        Assert.Equal([AccountSeed.Of(EthSamples.Transfers[0]), AccountSeed.Of(EthSamples.Transfers[1])], seeds);
    }

    [Fact]
    public void An_ethereum_account_can_carry_a_label_but_not_be_split()
    {
        var scene = EthSamples.Scene();
        var mule = scene.Node(EthSamples.Key(EthSamples.M))!;

        var subject = InspectorForms.LabelSubjectOf(mule);

        Assert.Equal(LabelSubject.ForAccount(EthSamples.M.ToChainAddress()), subject);
        Assert.False(InspectorForms.CanSplit(mule));
    }

    [Fact]
    public void The_movements_that_came_to_an_account_can_be_linked_to_another_chain()
    {
        var scene = EthSamples.Scene();
        var pool = scene.Node(EthSamples.Key(EthSamples.P))!;

        var incoming = Assert.Single(InspectorForms.IncomingCoins(scene, pool));

        Assert.Null(incoming.Coin);
        Assert.Equal(AccountSeed.Of(EthSamples.Transfers[3]), incoming.Movement);
        Assert.Equal(Chain.Ethereum, incoming.Chain);
        Assert.Equal(EthSamples.Usdt(400), incoming.Amount);
    }

    [Fact]
    public void A_start_point_on_ethereum_becomes_a_trace_start_point()
    {
        var address = ChainStartPoint.Recognise(EthSamples.M.Checksummed);
        var transaction = ChainStartPoint.Recognise(EthSamples.Hash(9).Value);

        Assert.True(address.IsSupported);
        Assert.Equal(TraceStartPoint.ForEthereumAddress(EthSamples.M), CaseWorkflow.ToTrace(address));
        Assert.Equal(TraceStartPoint.ForEthereumTransaction(EthSamples.Hash(9)), CaseWorkflow.ToTrace(transaction));
    }

    [Fact]
    public void A_chainsaw_link_to_ethereum_names_a_start_point_that_can_be_traced()
    {
        Assert.True(Links.ChainsawLink.TryParse("chainsaw:ethereum/" + EthSamples.M.Checksummed, out var startPoint));

        Assert.True(startPoint.IsSupported);
        Assert.Equal(TraceStartPoint.ForEthereumAddress(EthSamples.M), CaseWorkflow.ToTrace(startPoint));
    }

    [Fact]
    public void The_checks_say_honestly_that_they_do_not_run_on_ethereum()
    {
        Assert.Contains(
            "körs inte på Ethereum", ChecksViewModel.AccountNote(EthSamples.Scene()), StringComparison.Ordinal);
        Assert.Equal(string.Empty, ChecksViewModel.AccountNote(GraphSamples.Scene()));
    }

    [Fact]
    public async Task The_source_traces_ethereum_per_asset_and_values_lines_with_a_rate()
    {
        var tracing = new TestTracing { Graph = EthSamples.Graph() };
        var prices = new TestPrices();
        prices.Rates[KnownTokens.EthereumUsdt] = 10.5m;
        var source = new GraphSource(tracing, new TestAttribution(), new TestClustering(), null, prices);

        var scene = await source.LoadAsync(new TestCase("2026-0001"), TracingMethodKind.Haircut, CancellationToken.None);

        var request = Assert.Single(tracing.AccountTraced);
        Assert.Equal(TracingMethodKind.Haircut, request.Method);
        Assert.Equal([AccountSeed.Of(EthSamples.Transfers[0]), AccountSeed.Of(EthSamples.Transfers[1])],
            request.Seeds);
        Assert.All(prices.Asked, asked => Assert.Equal(PriceLookupMode.CaseOnly, asked.Mode));
        Assert.Contains(prices.Asked, asked => asked.Request.Asset == KnownTokens.EthereumUsdt);
        var valued = scene.Edge(EthSamples.EdgeKey(EthSamples.S, EthSamples.M, "USDT"))!;
        Assert.Equal(5_250m, valued.Value!.Rounded);
        Assert.Null(scene.Edge(EthSamples.EdgeKey(EthSamples.V, EthSamples.S, "ETH"))!.Value);
        Assert.Null(scene.Snapshot.TraceProblem);
    }

    [Fact]
    public async Task An_expansion_may_ask_for_rates_and_a_failed_account_trace_is_said()
    {
        var tracing = new TestTracing
        {
            Graph = EthSamples.Graph(),
            AccountTracer = (_, _) => throw new InvalidOperationException("seeds in each other's flow"),
        };
        var prices = new TestPrices();
        var source = new GraphSource(tracing, new TestAttribution(), new TestClustering(), null, prices);

        var expansion = await source.ExpandAsync(
            new TestCase("2026-0001"), [ExpansionRequest.ForEthereumAddress(EthSamples.M)], TracingMethodKind.Fifo,
            CancellationToken.None);

        Assert.Contains(prices.Asked, asked => asked.Mode == PriceLookupMode.AsNeeded);
        Assert.Equal(GraphSource.TraceFailedText, expansion.Scene.Snapshot.TraceProblem);
        Assert.Null(expansion.Scene.Snapshot.AccountTrace);
    }
}
