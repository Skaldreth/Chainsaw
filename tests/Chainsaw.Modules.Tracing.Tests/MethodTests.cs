using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Tracing.Tests.Synthetic;

namespace Chainsaw.Modules.Tracing.Tests;

public sealed class TaintSetTests
{
    [Fact]
    public void Of_sorts_and_joins_ranges_that_overlap_or_touch()
    {
        var set = TaintSet.Of(
            [new TaintRange(10, 20), new TaintRange(0, 5), new TaintRange(5, 8), new TaintRange(15, 30)]);

        Assert.Equal([new TaintRange(0, 8), new TaintRange(10, 30)], set.Ranges);
        Assert.Equal(28, set.Units);
        Assert.Equal(30, set.End);
    }

    [Fact]
    public void Cut_takes_the_part_between_two_positions_and_moves_it_to_zero()
    {
        var set = TaintSet.Of([new TaintRange(0, 8), new TaintRange(10, 30)]);

        Assert.Equal([new TaintRange(0, 3), new TaintRange(5, 10)], set.Cut(5, 15).Ranges);
        Assert.True(set.Cut(8, 10).IsEmpty);
    }

    [Fact]
    public void Shift_moves_every_range()
    {
        Assert.Equal([new TaintRange(100, 105)], TaintSet.FirstPart(5).Shift(100).Ranges);
    }

    [Fact]
    public void A_range_refuses_a_negative_start_or_an_end_before_its_start()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TaintRange(-1, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TaintRange(5, 3));
    }
}

public sealed class FifoMethodTests
{
    private readonly FifoMethod _fifo = new();

    [Fact]
    public void Consolidation_lays_the_inputs_end_to_end()
    {
        var tx = Tx("consolidate").In(Wpkh("a"), 300).In(Wpkh("b"), 700).Out(Wpkh("c"), 990).Build();

        var taint = _fifo.Propagate(MethodChecks.Flow(tx), [TaintSet.None, TaintSet.FirstPart(700)]);

        // The traced satoshi are b's, at 300–1 000 on the line; the output is 0–990 and the fee 990–1 000.
        Assert.Equal([new TaintRange(300, 990)], taint.Outputs[0].Ranges);
        Assert.Equal(10, taint.Fee);
    }

    [Fact]
    public void A_split_gives_the_first_outputs_the_first_satoshi()
    {
        var tx = Tx("split").In(Wpkh("a"), 1_000).Out(Wpkh("x"), 400).Out(Wpkh("y"), 590).Build();

        var taint = _fifo.Propagate(MethodChecks.Flow(tx), [TaintSet.Of([new TaintRange(300, 500)])]);

        Assert.Equal(100, taint.Outputs[0].Units);
        Assert.Equal([new TaintRange(0, 100)], taint.Outputs[1].Ranges);
        Assert.Equal(0L, taint.Fee);
    }

    [Fact]
    public void The_fee_is_the_end_of_the_line()
    {
        var tx = Tx("fee").In(Wpkh("a"), 1_000).Out(Wpkh("x"), 900).Build();

        var taint = _fifo.Propagate(MethodChecks.Flow(tx), [TaintSet.FirstPart(1_000)]);

        Assert.Equal(900, taint.Outputs[0].Units);
        Assert.Equal(100, taint.Fee);
    }

    [Fact]
    public void A_traced_set_that_reaches_past_its_input_is_refused()
    {
        var tx = Tx("past").In(Wpkh("a"), 100).Out(Wpkh("x"), 90).Build();

        Assert.Throws<ArgumentException>(() => _fifo.Propagate(MethodChecks.Flow(tx), [TaintSet.FirstPart(101)]));
        Assert.Throws<ArgumentException>(() => _fifo.Propagate(MethodChecks.Flow(tx), []));
    }
}

public sealed class HaircutMethodTests
{
    private readonly HaircutMethod _haircut = new();

    [Fact]
    public void The_traced_share_is_spread_in_proportion_to_the_amounts()
    {
        var tx = Tx("haircut").In(Wpkh("a"), 600).In(Wpkh("b"), 400).Out(Wpkh("x"), 500).Out(Wpkh("y"), 400).Build();

        var taint = _haircut.Propagate(MethodChecks.Flow(tx), [TaintSet.FirstPart(600), TaintSet.None]);

        // 60 % of the money is traced, so 60 % of each receiver: 300, 240 and 60 of the fee of 100.
        Assert.Equal(300, taint.Outputs[0].Units);
        Assert.Equal(240, taint.Outputs[1].Units);
        Assert.Equal(60, taint.Fee);
    }

    [Fact]
    public void Left_over_satoshi_go_to_the_largest_remainders_so_the_sum_is_exact()
    {
        // 10 traced over three equal outputs: 3.33 each; the left-over satoshi goes to the first.
        var shares = HaircutMethod.Shares(10, 30, [10, 10, 10, 0]);

        Assert.Equal([4, 3, 3, 0], shares);
    }

    [Fact]
    public void A_receiver_never_gets_more_than_its_amount()
    {
        var shares = HaircutMethod.Shares(999, 1_000, [1, 1, 1, 997, 0]);

        Assert.Equal(999, shares.Sum());
        Assert.All(shares.Zip(new long[] { 1, 1, 1, 997, 0 }), pair => Assert.True(pair.First <= pair.Second));
    }
}

public sealed class ConservationTests
{
    /// <summary>
    /// Random flows of consolidations and splits with fees: by either method, the ends add up to exactly what was
    /// traced, every transaction gives out what went in, and no coin holds more than its amount.
    /// </summary>
    [Theory]
    [InlineData(TracingMethodKind.Fifo, 1)]
    [InlineData(TracingMethodKind.Fifo, 2)]
    [InlineData(TracingMethodKind.Fifo, 3)]
    [InlineData(TracingMethodKind.Haircut, 1)]
    [InlineData(TracingMethodKind.Haircut, 2)]
    [InlineData(TracingMethodKind.Haircut, 3)]
    public void Nothing_is_created_and_nothing_disappears(TracingMethodKind kind, int seed)
    {
        var random = new Random(seed);
        var transactions = new List<BitcoinTransaction>();
        var unspent = new List<(BitcoinTransaction Tx, int Index)>();
        for (var r = 0; r < 4; r++)
        {
            var root = Tx($"root-{seed}-{r}", 900_000 + r)
                .In(Wpkh($"funder-{seed}-{r}"), 50_000_000 + random.Next(1_000_000));
            AddOutputs(root, random, 50_000_000, $"root-{seed}-{r}");
            var built = root.Build();
            transactions.Add(built);
            unspent.AddRange(built.Outputs.Select(output => (built, output.Index)));
        }

        var seeds = transactions.SelectMany(tx => tx.Outputs.Take(1).Select(output =>
            new TraceSeed(new Outpoint(tx.Txid, output.Index), new Satoshi(output.Amount.Value - random.Next(1_000)).ToAmount())))
            .ToList();
        for (var n = 0; n < 40 && unspent.Count > 0; n++)
        {
            var take = Math.Min(unspent.Count, 1 + random.Next(4));
            var builder = Tx($"tx-{seed}-{n}", 900_100 + n);
            long total = 0;
            for (var i = 0; i < take; i++)
            {
                var pick = random.Next(unspent.Count);
                var (tx, index) = unspent[pick];
                unspent.RemoveAt(pick);
                builder.Spend(tx, index);
                total += tx.Outputs[index].Amount.Value;
            }

            AddOutputs(builder, random, total - random.Next((int)Math.Min(5_000, total / 10)), $"tx-{seed}-{n}");
            var built = builder.Build();
            transactions.Add(built);
            unspent.AddRange(built.Outputs.Select(output => (built, output.Index)));
        }

        var graph = PureGraph.Of(transactions);
        IUtxoTracingMethod method = kind == TracingMethodKind.Fifo ? new FifoMethod() : new HaircutMethod();

        var result = TraceCalculator.Trace(graph, method, seeds);

        var traced = seeds.Aggregate(AssetAmount.Zero(Asset.Bitcoin), (sum, s) => sum + s.Amount!.Value);
        Assert.Equal(traced, result.Traced);
        Assert.Equal(traced, result.Ends.Aggregate(AssetAmount.Zero(Asset.Bitcoin), (sum, end) => sum + end.Amount.Amount));
        Assert.All(result.Transactions, tx => Assert.Equal(
            tx.In, tx.Outputs.Aggregate(tx.Fee, (sum, amount) => sum + amount)));
        Assert.All(result.Coins, coin => Assert.True(
            coin.Taint.End <= graph.Transactions[coin.Coin.Txid].Transaction.Outputs[coin.Coin.Index].Amount.Value));
        Assert.All(result.Ends, end => Assert.Equal(method.Id, end.Amount.Method));
        Assert.Contains(result.Ends, end => end.Reason == TrailEndReason.MinerFee);
    }

    private static void AddOutputs(TxBuilder builder, Random random, long total, string label)
    {
        var count = 1 + random.Next(4);
        long left = total;
        for (var i = 0; i < count - 1 && left > 2_000; i++)
        {
            var amount = 1_000 + random.NextInt64(left / 2);
            builder.Out(Wpkh($"{label}-out-{i}"), amount);
            left -= amount;
        }

        builder.Out(Wpkh($"{label}-out-last"), left);
    }
}
