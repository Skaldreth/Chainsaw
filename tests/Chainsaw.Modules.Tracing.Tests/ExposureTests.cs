using System.Globalization;
using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Tracing.Tests.Synthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// The analysis <em>exposure v4</em> (sketch EP1b) against a key of synthetic transfers: what reaches a service
/// directly and over hops by FIFO and haircut, what reaches none and why, read out and in, and that the services and the
/// rest always add up to what the subject paid or got.
/// </summary>
public sealed class ExposureTests
{
    /// <summary>The units of one whole coin in the synthetic amounts (ether is counted as if it had eight decimals).</summary>
    private const long Btc = 100_000_000;

    /// <summary>The asset of the wallet-by-wallet facit: an account chain's coin.</summary>
    private static readonly Asset Coin = Asset.Ether;

    private static readonly DateTimeOffset Start = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private static readonly ExposureAnalysis Analysis = new(
        [new AccountFifoMethod(), new AccountHaircutMethod(), new FifoMethod(), new HaircutMethod()]);

    private static readonly ExposureAnalysis UtxoOnly = new([new FifoMethod(), new HaircutMethod()]);

    private int _tx;

    [Fact]
    public void DirectPaymentsReachTheServiceAndTheRestIsNotReached()
    {
        var graph = Graph(Pay("S", "E", 0.5m, 1), Pay("S", "U", 0.3m, 2));

        var result = Out(graph, depth: 1, TracingMethodKind.Fifo, "E");

        Assert.Equal(80_000_000, (long)result.Total.Units);
        var exchange = Assert.Single(result.Reached);
        Assert.Equal(("E", 50_000_000L, 1), (exchange.NodeKey, (long)exchange.Amount.Units, exchange.Hops));
        Assert.Equal(0.625, result.ShareOf(exchange.Amount), 6);
        Assert.Equal(30_000_000, (long)result.Rest[ExposureRest.NotReached].Units);
    }

    [Fact]
    public void FifoSendsTheSubjectsMoneyOnAfterWhatCameBefore()
    {
        // K had 0.4 from O before 0.6 came from S; then 0.5 to the exchange and 0.5 to the mixer.
        var graph = TwoHops();

        var result = Out(graph, depth: 2, TracingMethodKind.Fifo, "E", "M");

        Assert.Equal([("M", 50_000_000L), ("E", 10_000_000L)], Amounts(result));
        Assert.All(result.Reached, reach => Assert.Equal(2, reach.Hops));
        Assert.False(result.Rest.ContainsKey(ExposureRest.Remains));
        Assert.Equal(new TracingMethodId(TracingMethodKind.Fifo, 1), result.Method);
    }

    [Fact]
    public void HaircutSpreadsTheSubjectsShareOverWhatLeaves()
    {
        var result = Out(TwoHops(), depth: 2, TracingMethodKind.Haircut, "E", "M");

        Assert.Equal([("E", 30_000_000L), ("M", 30_000_000L)], Amounts(result));
        Assert.Equal(new TracingMethodId(TracingMethodKind.Haircut, 1), result.Method);
    }

    [Fact]
    public void TheDepthStopsTheMoneyAtTheFirstRing()
    {
        var result = Out(TwoHops(), depth: 1, TracingMethodKind.Fifo, "E", "M");

        Assert.Empty(result.Reached);
        Assert.Equal(60_000_000, (long)result.Rest[ExposureRest.NotReached].Units);
    }

    [Fact]
    public void WhatStaysWithAnActorIsSaidAsRemaining()
    {
        var graph = Graph(Pay("S", "K", 1m, 1), Pay("K", "E", 0.4m, 2));

        var result = Out(graph, depth: 2, TracingMethodKind.Fifo, "E");

        Assert.Equal([("E", 40_000_000L)], Amounts(result));
        Assert.Equal(60_000_000, (long)result.Rest[ExposureRest.Remains].Units);
    }

    [Fact]
    public void MoneyThatComesBackToTheSubjectIsReturned()
    {
        var graph = Graph(Pay("S", "K", 1m, 1), Pay("K", "S", 0.4m, 2), Pay("K", "E", 0.6m, 3));

        var result = Out(graph, depth: 3, TracingMethodKind.Fifo, "E");

        Assert.Equal([("E", 60_000_000L)], Amounts(result));
        Assert.Equal(40_000_000, (long)result.Rest[ExposureRest.Returned].Units);
    }

    [Fact]
    public void ReadInFifoFindsWhereTheSubjectsMoneyCameFrom()
    {
        // The victim's 0.7 came to K before 0.3 from O; K sent 0.5 to S. The exchange X paid S 0.2 directly.
        var graph = ReadInCase();

        var result = In(graph, depth: 2, TracingMethodKind.Fifo, "V", "X");

        Assert.Equal(70_000_000, (long)result.Total.Units);
        Assert.Equal([("V", 50_000_000L), ("X", 20_000_000L)], Amounts(result));
        Assert.Equal(["S", "K", "V"], result.Reached[0].Path);
        Assert.Equal(2, result.Reached[0].Hops);
        Assert.Empty(result.Rest);
    }

    [Fact]
    public void ReadInHaircutSharesWhatWasSentByWhatCameIn()
    {
        var result = In(ReadInCase(), depth: 2, TracingMethodKind.Haircut, "V", "X");

        Assert.Equal([("V", 35_000_000L), ("X", 20_000_000L)], Amounts(result));
        Assert.Equal(15_000_000, (long)result.Rest[ExposureRest.NotReached].Units);
    }

    [Fact]
    public void ReadInWhatCameBeforeTheGraphsHistoryIsSaid()
    {
        var graph = Graph(Pay("V", "K", 0.4m, 1), Pay("K", "S", 1m, 2));

        var result = In(graph, depth: 2, TracingMethodKind.Fifo, "V");

        Assert.Equal([("V", 40_000_000L)], Amounts(result));
        Assert.Equal(60_000_000, (long)result.Rest[ExposureRest.BeforeHistory].Units);
    }

    [Fact]
    public void TheWayHasTheLinesAndTheTransactionsInOrder()
    {
        var graph = TwoHops();

        var mixer = Out(graph, depth: 2, TracingMethodKind.Fifo, "E", "M").Reached.Single(r => r.NodeKey == "M");

        Assert.Equal(["S", "K", "M"], mixer.Path);
        Assert.Equal(["S>K", "K>M"], mixer.PathEdges);
        Assert.Equal(["0x" + Tx(2), "0x" + Tx(4)], mixer.Transactions.Select(t => t.Id));
    }

    [Fact]
    public void ATransactionDrawnAsANodeAddsNoHop()
    {
        var graph = Graph(
            [Node("S"), Node("T", GraphNodeKind.Transaction), Node("E")],
            Pay("S", "T", 1m, 1), Pay("T", "E", 1m, 1));

        var exchange = Assert.Single(Out(graph, depth: 1, TracingMethodKind.Fifo, "E").Reached);

        Assert.Equal(1, exchange.Hops);
        Assert.Equal(["S", "T", "E"], exchange.Path);
    }

    [Fact]
    public void AnotherAssetIsNeverAdded()
    {
        var usdt = Asset.Token(Chain.Ethereum, "0xdac17f958d2ee523a2206206994597c13d831ec7", "USDT", 6);
        var ether = new GraphEdge(
            "S>E2", "S", "E2", GraphEdgeKind.Transfer, new AssetAmount(usdt, 5),
            [new AssetTransfer(new ChainTransactionRef(Chain.Ethereum, "0x" + Tx(99)), new AssetAmount(usdt, 5),
                null, null)],
            null, EdgeDirection.Forward);
        var graph = Graph(Pay("S", "E", 0.5m, 1));
        graph = graph with { Nodes = [.. graph.Nodes, Node("E2")], Edges = [.. graph.Edges, ether] };

        var result = Out(graph, depth: 1, TracingMethodKind.Fifo, "E", "E2");

        Assert.Equal(["E"], result.Reached.Select(reach => reach.NodeKey));
        Assert.Equal(50_000_000, (long)result.Total.Units);
    }

    [Fact]
    public void ASubjectWithoutTransfersHasNoMoney()
    {
        var result = Out(Graph(Pay("A", "E", 1m, 1)), depth: 3, TracingMethodKind.Fifo, "E");

        Assert.False(result.HasMoney);
        Assert.Empty(result.Reached);
        Assert.Empty(result.Rest);
    }

    [Fact]
    public void TheDepthIsOneToThree()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Request(Graph(), ExposureDirection.Out, 0,
            TracingMethodKind.Fifo));
        Assert.Throws<ArgumentOutOfRangeException>(() => Request(Graph(), ExposureDirection.Out, 4,
            TracingMethodKind.Fifo));
    }

    [Fact]
    public void TheExplanationNamesTheAnalysisTheMethodAndTheTransactions()
    {
        var result = Out(TwoHops(), depth: 2, TracingMethodKind.Fifo, "E", "M");

        var text = result.Explain(result.Reached[0]);

        Assert.Contains("exposure v4", text, StringComparison.Ordinal);
        Assert.Contains("FIFO v1", text, StringComparison.Ordinal);
        Assert.Contains("0x" + Tx(4), text, StringComparison.Ordinal);
        Assert.Contains("bedömning", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void TheServicesAndTheRestAlwaysAddUpToTheTotal(int seed)
    {
        var random = new Random(seed);
        var keys = Enumerable.Range(0, 9).Select(n => "N" + n.ToString(CultureInfo.InvariantCulture)).ToList();
        var pays = Enumerable.Range(0, 40).Select(minute =>
        {
            var from = keys[random.Next(keys.Count)];
            var to = keys[random.Next(keys.Count)];
            return Pay(from, to, random.Next(1, 100) / 100m, minute);
        }).ToArray();
        var graph = Graph(pays);
        foreach (var direction in Enum.GetValues<ExposureDirection>())
        {
            foreach (var method in Enum.GetValues<TracingMethodKind>())
            {
                for (var depth = 1; depth <= ExposureRequest.MaxDepth; depth++)
                {
                    var result = Analysis.Analyse(Request(graph, direction, depth, method, "N7", "N8") with
                    {
                        Subject = "N0",
                    });
                    var sum = result.Reached.Aggregate(BigInteger.Zero, (s, r) => s + r.Amount.Units)
                        + result.Unknown.Units;
                    Assert.Equal(result.Total.Units, sum);
                }
            }
        }
    }

    [Fact]
    public void AnActorReadAgainCarriesTheMoneyThatCameBackToIt()
    {
        // K pays L before the money comes back from L; read again, K sends it on to the exchange.
        var graph = Graph(Pay("S", "K", 1m, 1), Pay("K", "L", 1m, 2), Pay("L", "K", 1m, 3), Pay("K", "E", 1m, 4));

        var result = Out(graph, depth: 3, TracingMethodKind.Fifo, "E");

        Assert.Equal([("E", 100_000_000L)], Amounts(result));
        Assert.Equal(2, result.Reached[0].Hops);
        Assert.Empty(result.Rest);
    }

    [Fact]
    public void MoneyGoingRoundACircleLongerThanTheReadingsIsStillMoving()
    {
        var pays = new List<Payment> { Pay("S", "K", 1m, 0) };
        for (var round = 0; round < 3 * AccountExposureRun.MaxRuns; round++)
        {
            pays.Add(Pay("K", "L", 1m, (2 * round) + 1));
            pays.Add(Pay("L", "K", 1m, (2 * round) + 2));
        }

        var result = Out(Graph([.. pays]), depth: 3, TracingMethodKind.Fifo);

        Assert.True(result.Rest[ExposureRest.StillMoving].Units.Sign > 0);
        Assert.Equal(result.Total.Units, result.Unknown.Units);
    }

    [Fact]
    public void AnActorNotExpandedIsSaidAsSuchAndNotAsRemaining()
    {
        var graph = Graph(Pay("S", "K", 1m, 1), Pay("K", "E", 0.4m, 2));
        graph = graph with
        {
            Nodes = [.. graph.Nodes.Select(n => n.Key == "K" ? n with { Expansion = NodeExpansion.NotExpanded } : n)],
        };

        var result = Out(graph, depth: 2, TracingMethodKind.Fifo, "E");

        Assert.Equal(60_000_000, (long)result.Rest[ExposureRest.NotExpanded].Units);
        Assert.False(result.Rest.ContainsKey(ExposureRest.Remains));
    }

    [Fact]
    public void OnlyTheTransactionsThatLedToTheServiceAreListed()
    {
        // S paid K twice; K paid the exchange before the second payment came, which went to U.
        var graph = Graph(Pay("S", "K", 0.3m, 1), Pay("K", "E", 0.3m, 2), Pay("S", "K", 0.5m, 3), Pay("K", "U", 0.5m, 4));

        var exchange = Out(graph, depth: 2, TracingMethodKind.Fifo, "E").Reached.Single();

        Assert.Equal(["0x" + Tx(1), "0x" + Tx(2)], exchange.Transactions.Select(t => t.Id));
    }

    [Fact]
    public void AMethodIsTakenOnlyFromThoseRegisteredForTheChain()
    {
        Assert.Throws<InvalidOperationException>(() =>
            UtxoOnly.Analyse(Request(TwoHops(), ExposureDirection.Out, 2, TracingMethodKind.Fifo, "E")));
    }

    [Fact]
    public void OnBitcoinTheMoneyFollowsTheCoinAsTheGraphDoes()
    {
        // K got O's 0.6 before S's 0.4, then spent S's own coin to the exchange and O's to the mixer. A wallet's FIFO
        // would give the exchange O's money; the coin says it is S's.
        var (graph, keys) = CoinCase();

        var result = UtxoOnly.Analyse(Coins(graph, keys, ExposureDirection.Out, TracingMethodKind.Fifo));

        Assert.Equal(40_000_000, (long)result.Total.Units);
        var exchange = Assert.Single(result.Reached);
        Assert.Equal((keys["E"], 39_990_000L, 2), (exchange.NodeKey, (long)exchange.Amount.Units, exchange.Hops));
        Assert.Equal(10_000, (long)result.Rest[ExposureRest.Fees].Units);
        Assert.Equal([keys["S"], keys["K"], keys["E"]], exchange.Path);
        Assert.Equal(new TracingMethodId(TracingMethodKind.Fifo, 1), result.Method);
        var traced = TraceCalculator.Trace(graph, new FifoMethod(), [new TraceSeed(new Outpoint(_coinPay.Txid, 0))]);
        Assert.Equal(
            traced.Coins.Where(c => c.NodeKey == keys["E"]).Sum(c => (long)c.Amount.Amount.Units),
            (long)exchange.Amount.Units);
    }

    [Fact]
    public void OnBitcoinChangeBackToTheSameActorGoesOn()
    {
        var pay = Synthetic.Tx("pay", 1).In(Wpkh("s"), 100_010_000).Out(Wpkh("k"), 100_000_000).Build();
        var split = Synthetic.Tx("split", 2).Spend(pay, 0).Out(Wpkh("e"), 30_000_000).Out(Wpkh("k"), 69_990_000).Build();
        var rest = Synthetic.Tx("rest", 3).Spend(split, 1).Out(Wpkh("m"), 69_980_000).Build();
        var (graph, keys) = Expanded([pay, split, rest], "s", "k", "e", "m");

        var result = UtxoOnly.Analyse(Coins(graph, keys, ExposureDirection.Out, TracingMethodKind.Fifo, "e", "m"));

        Assert.Equal(
            [(keys["m"], 69_980_000L), (keys["e"], 30_000_000L)],
            result.Reached.Select(r => (r.NodeKey, (long)r.Amount.Units)));
        Assert.All(result.Reached, reach => Assert.Equal(2, reach.Hops));
        Assert.Equal(20_000, (long)result.Rest[ExposureRest.Fees].Units);
    }

    [Fact]
    public void OnBitcoinAnUnspentCoinRemainsOnlyWithAnActorReadInFull()
    {
        var pay = Synthetic.Tx("pay", 1).In(Wpkh("s"), 100_010_000).Out(Wpkh("k"), 100_000_000).Build();
        var (read, keys) = Expanded([pay], "s", "k");
        var notRead = read with
        {
            Nodes = [.. read.Nodes.Select(n => n.Key == keys["k"] ? n with { Expansion = NodeExpansion.Partly } : n)],
        };

        var full = UtxoOnly.Analyse(Coins(read, keys, ExposureDirection.Out, TracingMethodKind.Fifo));
        var partly = UtxoOnly.Analyse(Coins(notRead, keys, ExposureDirection.Out, TracingMethodKind.Fifo));

        Assert.Equal(100_000_000, (long)full.Rest[ExposureRest.Remains].Units);
        Assert.Equal(100_000_000, (long)partly.Rest[ExposureRest.NotExpanded].Units);
    }

    [Fact]
    public void OnBitcoinReadInTheCoinsInputsSayWhereTheMoneyCameFrom()
    {
        var (graph, keys) = InCoins();

        var fifo = UtxoOnly.Analyse(Coins(graph, keys, ExposureDirection.In, TracingMethodKind.Fifo, "v"));
        var haircut = UtxoOnly.Analyse(Coins(graph, keys, ExposureDirection.In, TracingMethodKind.Haircut, "v"));

        Assert.Equal(50_000_000, (long)fifo.Total.Units);
        Assert.Equal([(keys["v"], 50_000_000L)], fifo.Reached.Select(r => (r.NodeKey, (long)r.Amount.Units)));
        Assert.Equal(2, fifo.Reached[0].Hops);
        Assert.Equal(keys["v"], Assert.Single(haircut.Reached).NodeKey);
        Assert.Equal(50_000_000, (long)(haircut.Reached[0].Amount.Units + haircut.Unknown.Units));
        Assert.Equal(15_001_500, (long)haircut.Rest[ExposureRest.NotExpanded].Units);
        Assert.Equal(34_998_500, (long)haircut.Reached[0].Amount.Units);
    }

    [Fact]
    public void OnBitcoinEveryDirectionMethodAndDepthAddsUp()
    {
        foreach (var (graph, keys) in new[] { CoinCase(), InCoins() })
        {
            foreach (var direction in Enum.GetValues<ExposureDirection>())
            {
                foreach (var method in Enum.GetValues<TracingMethodKind>())
                {
                    for (var depth = 1; depth <= ExposureRequest.MaxDepth; depth++)
                    {
                        var request = Coins(graph, keys, direction, method, depth);
                        var result = UtxoOnly.Analyse(request);
                        var sum = result.Reached.Aggregate(BigInteger.Zero, (s, r) => s + r.Amount.Units)
                            + result.Unknown.Units;
                        Assert.Equal(result.Total.Units, sum);
                    }
                }
            }
        }
    }

    [Fact]
    public void OnBitcoinTheMoneyStopsAtACoinJoinThatIsAService()
    {
        var pay = Synthetic.Tx("pay", 1).In(Wpkh("s"), 50_010_000).Out(Wpkh("k"), 50_000_000).Build();
        var mix = Synthetic.Tx("mix", 2).Spend(pay, 0).In(Wpkh("x"), 50_000_000)
            .Out(Wpkh("a"), 49_990_000).Out(Wpkh("b"), 49_990_000).Build();
        var (graph, keys) = Expanded([pay, mix], "s", "k", "x", "a", "b");
        var node = "tx:" + mix.Txid.Value;
        Assert.Equal(GraphNodeKind.Transaction, graph.Node(node)?.Kind);
        var request = Coins(graph, keys, ExposureDirection.Out, TracingMethodKind.Fifo) with
        {
            Services = new HashSet<string>(StringComparer.Ordinal) { node },
        };

        var result = UtxoOnly.Analyse(request);

        var coinJoin = Assert.Single(result.Reached);
        Assert.Equal((node, 50_000_000L, 1), (coinJoin.NodeKey, (long)coinJoin.Amount.Units, coinJoin.Hops));
    }

    [Fact]
    public void OnBitcoinReadInMoneyThatCameFromTheSubjectItselfIsReturned()
    {
        // V paid S 1.0; S paid K with that coin, and K paid it back to S. Read in, the first coin came from V and the
        // second from S itself.
        var fromV = Synthetic.Tx("v-s", 1).In(Wpkh("v"), 100_010_000).Out(Wpkh("s"), 100_000_000).Build();
        var toK = Synthetic.Tx("s-k", 2).Spend(fromV, 0).Out(Wpkh("k"), 99_990_000).Build();
        var back = Synthetic.Tx("k-s", 3).Spend(toK, 0).Out(Wpkh("s"), 99_980_000).Build();
        var (graph, keys) = Expanded([fromV, toK, back], "v", "s", "k");

        foreach (var method in Enum.GetValues<TracingMethodKind>())
        {
            var result = UtxoOnly.Analyse(Coins(graph, keys, ExposureDirection.In, method, "v"));

            Assert.Equal(199_980_000, (long)result.Total.Units);
            Assert.Equal([(keys["v"], 100_000_000L)], result.Reached.Select(r => (r.NodeKey, (long)r.Amount.Units)));
            Assert.Equal(99_980_000, (long)result.Rest[ExposureRest.Returned].Units);
        }
    }

    [Fact]
    public void OnBitcoinTheMoneyStopsWhereTheTrailEndsAsInTheGraphsTrace()
    {
        var pay = Synthetic.Tx("pay", 1).In(Wpkh("s"), 50_010_000).Out(Wpkh("k"), 50_000_000).Build();
        var mix = Synthetic.Tx("mix", 2).Spend(pay, 0).In(Wpkh("x"), 50_000_000)
            .Out(Wpkh("a"), 49_990_000).Out(Wpkh("b"), 49_990_000).Build();
        var (expanded, keys) = Expanded([pay, mix], "s", "k", "x", "a", "b");
        var hint = new TrailEndHint(TrailEndReason.CoinJoin, "coinjoin v1", "Lika utgångar.");
        var graph = expanded with
        {
            TransactionEnds = new Dictionary<Txid, IReadOnlyList<TrailEndHint>> { [mix.Txid] = [hint] },
        };

        var paid = UtxoOnly.Analyse(Coins(graph, keys, ExposureDirection.Out, TracingMethodKind.Fifo, "b"));
        var got = UtxoOnly.Analyse(new ExposureRequest(
            graph, keys["a"], Asset.Bitcoin, ExposureDirection.In, 3, TracingMethodKind.Fifo,
            new HashSet<string>(StringComparer.Ordinal) { keys["s"] }));

        Assert.Empty(paid.Reached);
        Assert.Equal(50_000_000, (long)paid.Rest[ExposureRest.TrailEnds].Units);
        Assert.Empty(got.Reached);
        Assert.Equal(49_990_000, (long)got.Rest[ExposureRest.TrailEnds].Units);
        var traced = TraceCalculator.Trace(graph, new FifoMethod(), [new TraceSeed(new Outpoint(pay.Txid, 0))]);
        Assert.Contains(traced.Ends, end => end.Reason == TrailEndReason.CoinJoin);
    }

    [Fact]
    public void OnBitcoinASpendThatDoesNotAgreeStopsTheMoneyBothWays()
    {
        var pay = Synthetic.Tx("pay", 1).In(Wpkh("s"), 100_010_000).Out(Wpkh("k"), 100_000_000).Build();
        var on = Synthetic.Tx("on", 2).Spend(pay, 0).Out(Wpkh("e"), 99_990_000).Build();
        var (expanded, keys) = Expanded([pay, on], "s", "k", "e");
        var coin = new Outpoint(pay.Txid, 0);
        var graph = expanded with
        {
            Spends = new Dictionary<Outpoint, CoinSpend>(expanded.Spends)
            {
                [coin] = new CoinSpend(SpendState.Spent, new BitcoinSpend(on.Txid, 5, null), false),
            },
        };

        var paid = UtxoOnly.Analyse(Coins(graph, keys, ExposureDirection.Out, TracingMethodKind.Fifo, "e"));
        var got = UtxoOnly.Analyse(new ExposureRequest(
            graph, keys["e"], Asset.Bitcoin, ExposureDirection.In, 3, TracingMethodKind.Fifo,
            new HashSet<string>(StringComparer.Ordinal) { keys["s"] }));

        Assert.Empty(paid.Reached);
        Assert.Equal(100_000_000, (long)paid.Rest[ExposureRest.Conflicting].Units);
        Assert.Empty(got.Reached);
        Assert.True(got.Rest.ContainsKey(ExposureRest.Conflicting));
        var traced = TraceCalculator.Trace(graph, new FifoMethod(), [new TraceSeed(coin)]);
        Assert.Contains(traced.Ends, end => end.Reason == TrailEndReason.Conflicting);
    }

    [Fact]
    public void OnBitcoinACoinLinkedToAnotherChainStopsThereAndNotInItsSpend()
    {
        var pay = Synthetic.Tx("pay", 1).In(Wpkh("s"), 100_010_000).Out(Wpkh("k"), 100_000_000).Build();
        var on = Synthetic.Tx("on", 2).Spend(pay, 0).Out(Wpkh("e"), 99_990_000).Build();
        var to = new ChainTransactionRef(Chain.Tron, new string('a', 64));
        var link = new CrossChainLink(1, new Outpoint(pay.Txid, 0), to, "Bryggan", 1, Start);
        var graph = PureGraph.Of([pay, on], [link]);
        graph = graph with { Nodes = [.. graph.Nodes.Select(n => n with { Expansion = NodeExpansion.Expanded })] };
        var keys = new[] { "s", "k", "e" }.ToDictionary(label => label, label => graph.NodeOf(Wpkh(label))!.Key);
        var other = NodeKeys.OtherChain(to);
        var request = Coins(graph, keys, ExposureDirection.Out, TracingMethodKind.Fifo, "e") with
        {
            Services = new HashSet<string>(StringComparer.Ordinal) { keys["e"], other },
        };

        var result = UtxoOnly.Analyse(request);

        var reached = Assert.Single(result.Reached);
        Assert.Equal((other, 100_000_000L, 2), (reached.NodeKey, (long)reached.Amount.Units, reached.Hops));
        Assert.Contains(result.Flows, flow => flow.Far == other);
        Assert.False(result.Rest.ContainsKey(ExposureRest.Fees));
    }

    [Fact]
    public void OnBitcoinALinkedCoinGoesToTheOtherChainAlsoWhenItsActorIsAServiceOrBeyondTheDepth()
    {
        var (graph, keys, other) = LinkedCase();
        var bridge = Coins(graph, keys, ExposureDirection.Out, TracingMethodKind.Fifo) with
        {
            Services = new HashSet<string>(StringComparer.Ordinal) { keys["k"], keys["e"], other },
        };
        var shallow = Coins(graph, keys, ExposureDirection.Out, TracingMethodKind.Fifo, 1, "e") with
        {
            Services = new HashSet<string>(StringComparer.Ordinal) { keys["e"], other },
        };

        foreach (var request in new[] { bridge, shallow })
        {
            var reached = Assert.Single(UtxoOnly.Analyse(request).Reached);
            Assert.Equal((other, 100_000_000L), (reached.NodeKey, (long)reached.Amount.Units));
        }
    }

    [Fact]
    public void OnBitcoinReadInALinkedInputsShareContinuesOnTheOtherChainAndNoOtherInputTakesIt()
    {
        var pay = Synthetic.Tx("pay", 1).In(Wpkh("s"), 100_010_000).Out(Wpkh("k"), 100_000_000).Build();
        var both = Synthetic.Tx("both", 2).Spend(pay, 0).In(Wpkh("x"), 50_000_000).Out(Wpkh("e"), 149_990_000)
            .Build();
        var (graph, subject, sources) = LinkedReadIn(pay, both, "x", "k");
        var x = graph.NodeOf(Wpkh("x"))!.Key;

        var result = UtxoOnly.Analyse(new ExposureRequest(
            graph, subject, Asset.Bitcoin, ExposureDirection.In, 3, TracingMethodKind.Fifo, sources));

        // FIFO: the linked coin's 1 BTC comes first, so x's 0.5 pays the rest and the fee.
        var reached = Assert.Single(result.Reached);
        Assert.Equal((x, 49_990_000L), (reached.NodeKey, (long)reached.Amount.Units));
        Assert.Equal(100_000_000, (long)result.Rest[ExposureRest.OtherChain].Units);
        Assert.Equal(149_990_000, (long)result.Total.Units);
    }

    [Fact]
    public void OnBitcoinReadInWhatOnlyALinkedInputPaidContinuesOnTheOtherChain()
    {
        var pay = Synthetic.Tx("pay", 1).In(Wpkh("s"), 100_010_000).Out(Wpkh("k"), 100_000_000).Build();
        var only = Synthetic.Tx("only", 2).Spend(pay, 0).Out(Wpkh("e"), 99_990_000).Build();
        var (graph, subject, sources) = LinkedReadIn(pay, only, "k");

        var result = UtxoOnly.Analyse(new ExposureRequest(
            graph, subject, Asset.Bitcoin, ExposureDirection.In, 3, TracingMethodKind.Fifo, sources));

        Assert.Empty(result.Reached);
        Assert.Equal(99_990_000, (long)result.Rest[ExposureRest.OtherChain].Units);
    }

    [Fact]
    public void OnAnAccountChainATransferLinkedToAnotherChainGoesThereAndNotIntoTheWallet()
    {
        // S's 0.6 to K is linked to another chain; K's own payments to E and M carry only O's 0.4.
        var hops = TwoHops();
        var linked = hops.Edges.Single(edge => edge.Key == "S>K");
        var onward = new GraphEdge(
            "K>X", "K", "X", GraphEdgeKind.ContinuesOnOtherChain, linked.Amount, linked.Transfers, linked.FirstTime,
            EdgeDirection.Forward);
        var graph = hops with
        {
            Nodes = [.. hops.Nodes, Node("X", GraphNodeKind.OtherChain)],
            Edges = [.. hops.Edges, onward],
        };

        var result = Out(graph, depth: 2, TracingMethodKind.Fifo, "E", "M", "X");

        var other = Assert.Single(result.Reached);
        Assert.Equal(("X", 60_000_000L, 2), (other.NodeKey, (long)other.Amount.Units, other.Hops));
        Assert.Contains(result.Flows, flow => flow.EdgeKey == "K>X");
    }

    [Fact]
    public void OnAnAccountChainReadInTheShareOfADepositLinkedToAnotherChainContinuesThere()
    {
        // V's 0.7 to K is linked to another chain; by FIFO K's 0.5 to S came from it, and goes on there.
        var cases = ReadInCase();
        var linked = cases.Edges.Single(edge => edge.Key == "V>K");
        var onward = new GraphEdge(
            "K>Y", "K", "Y", GraphEdgeKind.ContinuesOnOtherChain, linked.Amount, linked.Transfers, linked.FirstTime,
            EdgeDirection.Forward);
        var graph = cases with
        {
            Nodes = [.. cases.Nodes, Node("Y", GraphNodeKind.OtherChain)],
            Edges = [.. cases.Edges, onward],
        };

        var result = In(graph, depth: 2, TracingMethodKind.Fifo, "V", "X");

        Assert.DoesNotContain(result.Reached, reach => reach.NodeKey == "V");
        Assert.Equal(50_000_000, (long)result.Rest[ExposureRest.OtherChain].Units);
        Assert.False(result.Rest.ContainsKey(ExposureRest.BeforeHistory));
    }

    [Fact]
    public void ATallyThatCountsMoreThanTheTotalIsRefused()
    {
        var request = Request(TwoHops(), ExposureDirection.Out, 2, TracingMethodKind.Fifo, "E");
        var tally = new ExposureTally(request);
        tally.Reach("E", "S");
        tally.Arrive("E", 10, []);

        Assert.Throws<InvalidOperationException>(() => tally.Result(5, new TracingMethodId(TracingMethodKind.Fifo, 1)));
    }

    /// <summary>
    /// The graph of S's payment to K, whose coin is linked to a TRON tx, and a spend of it to E; read in from E with
    /// the given actors as services.
    /// </summary>
    private static (TraceGraph Graph, string Subject, IReadOnlySet<string> Services) LinkedReadIn(
        BitcoinTransaction pay, BitcoinTransaction spend, params string[] services)
    {
        var to = new ChainTransactionRef(Chain.Tron, new string('a', 64));
        var link = new CrossChainLink(1, new Outpoint(pay.Txid, 0), to, "Bryggan", 1, Start);
        var graph = PureGraph.Of([pay, spend], [link]);
        graph = graph with { Nodes = [.. graph.Nodes.Select(n => n with { Expansion = NodeExpansion.Expanded })] };
        var keys = services.Select(label => graph.NodeOf(Wpkh(label))!.Key).ToHashSet(StringComparer.Ordinal);
        return (graph, graph.NodeOf(Wpkh("e"))!.Key, keys);
    }

    /// <summary>S paid K 1 BTC, a coin linked to a TRON tx, which K then spent to E.</summary>
    private static (TraceGraph Graph, Dictionary<string, string> Keys, string Other) LinkedCase()
    {
        var pay = Synthetic.Tx("pay", 1).In(Wpkh("s"), 100_010_000).Out(Wpkh("k"), 100_000_000).Build();
        var on = Synthetic.Tx("on", 2).Spend(pay, 0).Out(Wpkh("e"), 99_990_000).Build();
        var to = new ChainTransactionRef(Chain.Tron, new string('a', 64));
        var link = new CrossChainLink(1, new Outpoint(pay.Txid, 0), to, "Bryggan", 1, Start);
        var graph = PureGraph.Of([pay, on], [link]);
        graph = graph with { Nodes = [.. graph.Nodes.Select(n => n with { Expansion = NodeExpansion.Expanded })] };
        var keys = new[] { "s", "k", "e" }.ToDictionary(label => label, label => graph.NodeOf(Wpkh(label))!.Key);
        return (graph, keys, NodeKeys.OtherChain(to));
    }

    private BitcoinTransaction _coinPay = null!;

    /// <summary>O paid K 0.6, then S paid K 0.4; K spent S's coin to E and O's to M.</summary>
    private (TraceGraph Graph, Dictionary<string, string> Keys) CoinCase()
    {
        var fromO = Synthetic.Tx("from-o", 1).In(Wpkh("o"), 60_010_000).Out(Wpkh("k"), 60_000_000).Build();
        _coinPay = Synthetic.Tx("from-s", 2).In(Wpkh("s"), 40_010_000).Out(Wpkh("k"), 40_000_000).Build();
        var toE = Synthetic.Tx("to-e", 3).Spend(_coinPay, 0).Out(Wpkh("e"), 39_990_000).Build();
        var toM = Synthetic.Tx("to-m", 4).Spend(fromO, 0).Out(Wpkh("m"), 59_990_000).Build();
        var (graph, keys) = Expanded([fromO, _coinPay, toE, toM], "o", "s", "k", "e", "m");
        keys["S"] = keys["s"];
        keys["K"] = keys["k"];
        keys["E"] = keys["e"];
        keys["M"] = keys["m"];
        return (graph, keys);
    }

    /// <summary>V paid K 0.7, then O paid K 0.3; K spent both, 0.5 to S and the change back.</summary>
    private static (TraceGraph Graph, Dictionary<string, string> Keys) InCoins()
    {
        var fromV = Synthetic.Tx("from-v", 1).In(Wpkh("v0"), 70_010_000).Out(Wpkh("v"), 70_000_000).Build();
        var vToK = Synthetic.Tx("v-k", 2).Spend(fromV, 0).Out(Wpkh("k"), 69_990_000).Build();
        var oToK = Synthetic.Tx("o-k", 3).In(Wpkh("o"), 30_010_000).Out(Wpkh("k"), 30_000_000).Build();
        var toS = Synthetic.Tx("k-s", 4).Spend(vToK, 0).Spend(oToK, 0).Out(Wpkh("s"), 50_000_000).Out(Wpkh("k2"), 49_980_000)
            .Build();
        return Expanded([fromV, vToK, oToK, toS], "v", "o", "k", "s", "k2");
    }

    /// <summary>The graph of synthetic transactions with every actor expanded, and the actors' keys by label.</summary>
    private static (TraceGraph Graph, Dictionary<string, string> Keys) Expanded(
        IEnumerable<BitcoinTransaction> transactions, params string[] labels)
    {
        var graph = PureGraph.Of(transactions);
        graph = graph with { Nodes = [.. graph.Nodes.Select(n => n with { Expansion = NodeExpansion.Expanded })] };
        var keys = labels.ToDictionary(label => label, label => graph.NodeOf(Wpkh(label))!.Key);
        return (graph, keys);
    }

    /// <summary>A request on the coin graph with S as the subject (or s), and services by label.</summary>
    private static ExposureRequest Coins(
        TraceGraph graph,
        Dictionary<string, string> keys,
        ExposureDirection direction,
        TracingMethodKind method,
        params string[] services) => Coins(graph, keys, direction, method, ExposureRequest.MaxDepth, services);

    /// <summary>A request on the coin graph at a depth.</summary>
    private static ExposureRequest Coins(
        TraceGraph graph,
        Dictionary<string, string> keys,
        ExposureDirection direction,
        TracingMethodKind method,
        int depth,
        params string[] services) =>
        new(
            graph,
            keys["s"],
            Asset.Bitcoin,
            direction,
            depth,
            method,
            (services.Length > 0 ? services : ["e", "m", "v"])
                .Where(keys.ContainsKey)
                .Select(label => keys[label])
                .ToHashSet(StringComparer.Ordinal));

    /// <summary>S paid K 0.6 after O paid K 0.4; K paid the exchange E 0.5 and the mixer M 0.5.</summary>
    private TraceGraph TwoHops() => Graph(
        Pay("O", "K", 0.4m, 1), Pay("S", "K", 0.6m, 2), Pay("K", "E", 0.5m, 3), Pay("K", "M", 0.5m, 4));

    /// <summary>The victim V paid K 0.7 before O paid 0.3; K paid S 0.5, and the exchange X paid S 0.2.</summary>
    private TraceGraph ReadInCase() => Graph(
        Pay("V", "K", 0.7m, 1), Pay("O", "K", 0.3m, 2), Pay("K", "S", 0.5m, 3), Pay("X", "S", 0.2m, 4));

    private static ExposureResult Out(TraceGraph graph, int depth, TracingMethodKind method, params string[] services) =>
        Analysis.Analyse(Request(graph, ExposureDirection.Out, depth, method, services));

    private static ExposureResult In(TraceGraph graph, int depth, TracingMethodKind method, params string[] services) =>
        Analysis.Analyse(Request(graph, ExposureDirection.In, depth, method, services));

    private static ExposureRequest Request(
        TraceGraph graph, ExposureDirection direction, int depth, TracingMethodKind method, params string[] services) =>
        new(graph, "S", Coin, direction, depth, method, services.ToHashSet(StringComparer.Ordinal));

    private static List<(string, long)> Amounts(ExposureResult result) =>
        [.. result.Reached.Select(reach => (reach.NodeKey, (long)reach.Amount.Units))];

    private Payment Pay(string from, string to, decimal btc, int minute) =>
        new(from, to, (long)(btc * Btc), minute, ++_tx);

    private static string Tx(int n) => n.ToString("x64", CultureInfo.InvariantCulture);

    private static TraceGraph Graph(params Payment[] pays)
    {
        var keys = pays.SelectMany(pay => new[] { pay.From, pay.To }).Append("S").Distinct().ToList();
        return Graph([.. keys.Select(key => Node(key))], pays);
    }

    private static TraceGraph Graph(IReadOnlyList<GraphNode> nodes, params Payment[] pays)
    {
        var edges = pays.GroupBy(pay => (pay.From, pay.To)).Select(group =>
        {
            var transfers = group.Select(Transfer).ToList();
            return new GraphEdge(
                group.Key.From + ">" + group.Key.To,
                group.Key.From,
                group.Key.To,
                GraphEdgeKind.Transfer,
                transfers.Aggregate(AssetAmount.Zero(Coin), (sum, t) => sum + t.Amount),
                transfers,
                transfers[0].Time,
                EdgeDirection.Forward);
        }).ToList();
        return new TraceGraph(
            [], nodes, edges,
            new UtxoGraph(
                new Dictionary<Txid, GraphTransaction>(),
                new Dictionary<Outpoint, CoinSpend>(),
                new Dictionary<Txid, IReadOnlyList<TrailEndHint>>()),
            [], [], []);
    }

    private static AssetTransfer Transfer(Payment pay) => new(
        new ChainTransactionRef(Chain.Ethereum, "0x" + Tx(pay.Tx)),
        new AssetAmount(Coin, pay.Satoshi),
        new BlockReference(900_000 + pay.Minute, BlockHash.Parse(Tx(1000 + pay.Minute)), Start.AddMinutes(pay.Minute)),
        null);

    private static GraphNode Node(string key, GraphNodeKind kind = GraphNodeKind.Cluster) => new(
        key, kind, [], 1, null, null, null, false, null, null, NodeExpansion.Expanded, [],
        new Dictionary<GraphLayout, NodePosition>());

    private sealed record Payment(string From, string To, long Satoshi, int Minute, int Tx);
}
