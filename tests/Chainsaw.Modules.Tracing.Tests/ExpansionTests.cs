using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Microsoft.Extensions.DependencyInjection;
using static Chainsaw.Modules.Tracing.Tests.Synthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// What an expansion offers and how fast it asks: a coin that can never be spent is not shown as waiting to be
/// expanded, and where the coins went is asked for several transactions at once.
/// </summary>
public sealed class ExpansionTests : IDisposable
{
    private readonly SlowSpends _source = new();
    private readonly CaseHarness _harness;

    public ExpansionTests()
    {
        _harness = new CaseHarness(services => services.AddSingleton<IBitcoinSource>(_source));
        _source.Inner = _harness.Source;
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task An_op_return_coin_is_all_there_is_and_waits_for_no_expansion()
    {
        // A mining pool's payouts each carry an OP_RETURN (the witness commitment); pressing E on one could only
        // ever say that there was nothing new to fetch.
        var payout = Tx("payout").In(Wpkh("miner"), 100_000).Out(Wpkh("pool"), 90_000).OpReturn().Build();
        _harness.Source.Add(payout);

        var result = await _harness.Expand(Wpkh("pool"));

        var coin = Assert.Single(result.Graph.Nodes, node => node.Kind == GraphNodeKind.Coin);
        Assert.Equal(new Outpoint(payout.Txid, 1), coin.Coin);
        Assert.Equal(NodeExpansion.Expanded, coin.Expansion);
    }

    [Fact]
    public async Task Where_the_coins_went_is_asked_for_several_transactions_at_once()
    {
        var payouts = Enumerable.Range(0, 8)
            .Select(index => Tx($"payout-{index}", 900_000 + index)
                .In(Wpkh($"payer-{index}"), 100_000).Out(Wpkh("pool"), 90_000).Build())
            .ToArray();
        _harness.Source.Add(payouts);

        var result = await _harness.Expand(Wpkh("pool"));

        Assert.Equal(payouts.Length, result.NewTransactions.Count);
        Assert.InRange(_source.MostAtOnce, 2, TracingService.SpendLookupsAtOnce);
    }

    /// <summary>
    /// The fake source behind a pause on every question about where coins went, which counts how many are asked at
    /// the same time.
    /// </summary>
    private sealed class SlowSpends : IBitcoinSource
    {
        private int _atOnce;
        private int _mostAtOnce;

        public FakeSource Inner { get; set; } = null!;

        public int MostAtOnce => Volatile.Read(ref _mostAtOnce);

        public BitcoinNetwork Network => Inner.Network;

        public Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
            ICase @case, BitcoinAddress address, Txid? after = null, CancellationToken cancellationToken = default) =>
            Inner.GetAddressTransactionsAsync(@case, address, after, cancellationToken);

        public Task<BitcoinLookup<BitcoinTransaction>> GetTransactionAsync(
            ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default) =>
            Inner.GetTransactionAsync(@case, txid, mode, cancellationToken);

        public Task<BitcoinLookup<BitcoinTransaction>> VerifyTransactionAsync(
            ICase @case, Txid txid, CancellationToken cancellationToken = default) =>
            Inner.VerifyTransactionAsync(@case, txid, cancellationToken);

        public Task<BitcoinLookup<BitcoinAddressSummary>> GetAddressSummaryAsync(
            ICase @case, BitcoinAddress address, CancellationToken cancellationToken = default) =>
            Inner.GetAddressSummaryAsync(@case, address, cancellationToken);

        public async Task<BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>> GetOutputSpendsAsync(
            ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default)
        {
            if (mode == LookupMode.CaseOnly)
            {
                return await Inner.GetOutputSpendsAsync(@case, txid, mode, cancellationToken);
            }

            var now = Interlocked.Increment(ref _atOnce);
            InterlockedMax(now);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
                return await Inner.GetOutputSpendsAsync(@case, txid, mode, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _atOnce);
            }
        }

        public Task<BitcoinLookup<BitcoinBlock>> GetBlockAsync(
            ICase @case, BlockHash hash, CancellationToken cancellationToken = default) =>
            Inner.GetBlockAsync(@case, hash, cancellationToken);

        public Task<BitcoinLookup<BitcoinTip>> GetTipHeightAsync(
            ICase @case, CancellationToken cancellationToken = default) =>
            Inner.GetTipHeightAsync(@case, cancellationToken);

        private void InterlockedMax(int value)
        {
            var seen = Volatile.Read(ref _mostAtOnce);
            while (value > seen)
            {
                var before = Interlocked.CompareExchange(ref _mostAtOnce, value, seen);
                if (before == seen)
                {
                    return;
                }

                seen = before;
            }
        }
    }
}
