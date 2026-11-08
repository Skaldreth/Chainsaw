using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Tracing.Tests.EthSynthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// When a transaction is read as a swap (SW1a), and when in doubt not: a false swap moves the traced money away from a
/// real recipient, while a missed swap only leaves the trail honestly at the pool. From the second review's probes
/// (a bridge with a refunded fee, a payment with cashback, a router's fee beside the pair) and the shapes real
/// exchanges have: Uniswap V2 through its router, a V3 pool directly, and 1inch.
/// </summary>
public sealed class SwapRecognitionTests : IDisposable
{
    private static readonly EthereumAddress Victim = Address("q-victim");
    private static readonly EthereumAddress Scammer = Address("q-scammer");
    private static readonly EthereumAddress Router = Address("q-router");
    private static readonly EthereumAddress BridgePool = Address("q-bridge-pool");
    private static readonly EthereumAddress Merchant = Address("q-merchant");
    private static readonly EthereumAddress Pair = Address("q-pair");
    private static readonly EthereumAddress Weth = EthereumAddress.Parse(KnownTokens.EthereumWeth.Contract!);
    private static readonly EthereumAddress FeeCollector = Address("q-fee-collector");
    private static readonly EthereumAddress Exchange = Address("q-exchange");

    private static readonly EthereumAddress UniswapV2 =
        KnownContracts.All.First(contract => contract.Name.Contains("V2 Router02", StringComparison.Ordinal)).Address;

    private static readonly EthereumAddress OneInch =
        KnownContracts.All.First(contract => contract.Category == KnownContractCategory.Aggregator).Address;

    private readonly CaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private async Task<TraceGraph> Build(string[] whole, params EthereumTransaction[] txs)
    {
        _harness.Ethereum.Add(txs);
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));
        await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Scammer));
        foreach (var label in whole)
        {
            await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumTransaction(Hash(label)));
        }

        return await _harness.Load();
    }

    private Task<AccountTraceResult> FromVictim(TraceGraph graph) =>
        _harness.Service.TraceAccountsAsync(graph, new AccountTraceRequest(TracingMethodKind.Fifo,
            [.. graph.Account.Transfers.Where(t => t.From == Victim).Select(AccountSeed.Of)]));

    private static string Key(TraceGraph graph, EthereumAddress address) =>
        graph.NodeOf(address.ToChainAddress())!.Key;

    private static AssetAmount WrappedEther(decimal ether) =>
        new(KnownTokens.EthereumWeth, new BigInteger(ether * 1_000_000_000_000_000_000m));

    private static (EthereumTransferKind, EthereumAddress, EthereumAddress, AssetAmount) Internal(
        EthereumAddress from, EthereumAddress to, decimal ether) =>
        (EthereumTransferKind.Internal, from, to, Ether(ether));

    [Fact]
    public async Task A_bridge_that_refunds_part_of_its_fee_is_no_swap_and_the_money_reaches_its_pool()
    {
        var graph = await Build(["r-bridge"],
            Tx("r-victim", 101, Victim, 0m, Token(Victim, Scammer, Usdt(1000))),
            Tx("r-bridge", 110, Scammer, 0m, Eth(Scammer, Router, 0.01m), Token(Scammer, BridgePool, Usdt(1000)),
                Internal(Router, Scammer, 0.004m)),
            Tx("r-out", 130, Scammer, 0m, Eth(Scammer, Exchange, 0.004m)));

        var result = await FromVictim(graph);

        Assert.Empty(graph.Account.Swaps);
        Assert.Equal(Usdt(1000), Assert.Single(result.Ends, end => end.NodeKey == Key(graph, BridgePool)).Amount.Amount);
    }

    [Fact]
    public async Task A_payment_through_a_contract_with_cashback_is_no_swap_and_reaches_the_merchant()
    {
        var graph = await Build(["p-pay"],
            Tx("p-victim", 101, Victim, 0m, Token(Victim, Scammer, Usdt(1000))),
            Tx("p-pay", 110, Scammer, 0m, Eth(Scammer, Router, 0m), Token(Scammer, Merchant, Usdt(1000)),
                Internal(Router, Scammer, 0.001m)),
            Tx("p-out", 130, Scammer, 0m, Eth(Scammer, Exchange, 0.001m)));

        var result = await FromVictim(graph);

        Assert.Empty(graph.Account.Swaps);
        Assert.Equal(Usdt(1000), Assert.Single(result.Ends, end => end.NodeKey == Key(graph, Merchant)).Amount.Amount);
    }

    [Fact]
    public async Task A_routers_fee_beside_a_pair_that_shows_nothing_is_no_swap_and_creates_no_money()
    {
        var graph = await Build(["f-swap"],
            Tx("f-victim", 101, Victim, 0m, Token(Victim, Scammer, Usdt(1000))),
            Tx("f-swap", 110, Scammer, 0m, Token(Scammer, Router, Usdt(10)), Token(Scammer, BridgePool, Usdt(990)),
                Internal(Router, Scammer, 0.5m)),
            Tx("f-out", 130, Scammer, 0m, Eth(Scammer, Exchange, 0.5m)));

        var result = await FromVictim(graph);

        Assert.Empty(graph.Account.Swaps);
        Assert.Equal(Usdt(990), Assert.Single(result.Ends, end => end.NodeKey == Key(graph, BridgePool)).Amount.Amount);
        Assert.Single(result.Assets);
    }

    [Fact]
    public async Task Uniswap_v2_through_its_router_is_a_swap_when_the_transaction_is_read_whole()
    {
        // The router pulls the USDT into the pair, the pair pays WETH to the router, which unwraps it and pays ether.
        var swap = Tx("v2-swap", 110, Scammer, 0.001m, Token(Scammer, Pair, Usdt(1000)),
            Token(Pair, UniswapV2, WrappedEther(0.5m)), Internal(Weth, UniswapV2, 0.5m),
            Internal(UniswapV2, Scammer, 0.5m)) with { To = UniswapV2 };
        var graph = await Build(["v2-swap"],
            Tx("v2-victim", 101, Victim, 0.001m, Token(Victim, Scammer, Usdt(1000))),
            swap,
            Tx("v2-out", 130, Scammer, 0.001m, Eth(Scammer, Exchange, 0.5m)));

        var found = Assert.Single(graph.Account.Swaps);
        var result = await FromVictim(graph);

        Assert.Equal((Usdt(1000), Ether(0.5m)), (found.Sold, found.Bought));
        Assert.Equal(UniswapV2, found.Venue!.Address);
        Assert.Equal(Usdt(1000), result.Swaps.Single().TracedSold);
    }

    [Fact]
    public async Task Uniswap_v2_through_its_router_is_no_swap_from_the_histories_alone()
    {
        // The account's histories show the USDT to the pair and the ether from the router, but not the pair's WETH to
        // the router; that the pair was the trade's side cannot be shown, so the trail ends honestly at the pair.
        var graph = await Build([],
            Tx("v2h-victim", 101, Victim, 0.001m, Token(Victim, Scammer, Usdt(1000))),
            Tx("v2h-swap", 110, Scammer, 0.001m, Token(Scammer, Pair, Usdt(1000)),
                Token(Pair, UniswapV2, WrappedEther(0.5m)), Internal(Weth, UniswapV2, 0.5m),
                Internal(UniswapV2, Scammer, 0.5m)) with { To = UniswapV2 });

        Assert.Empty(graph.Account.Swaps);
    }

    [Fact]
    public async Task A_v3_pool_paid_and_paying_directly_is_a_swap_from_the_histories()
    {
        var graph = await Build([],
            Tx("v3-victim", 101, Victim, 0.001m, Token(Victim, Scammer, Usdt(1000))),
            Tx("v3-swap", 110, Scammer, 0.001m, Token(Scammer, Pair, Usdt(1000)),
                Token(Pair, Scammer, new AssetAmount(KnownTokens.EthereumUsdc, 999_000_000))));

        var swap = Assert.Single(graph.Account.Swaps);

        Assert.Equal(KnownTokens.EthereumUsdc, swap.Bought.Asset);
    }

    [Fact]
    public async Task A_counterparty_labelled_as_an_exchange_is_never_a_swaps_other_side()
    {
        _harness.Labels.Accounts[Pair.ToChainAddress()] = TrailEndReason.Exchange;
        var graph = await Build([],
            Tx("x3-victim", 101, Victim, 0.001m, Token(Victim, Scammer, Usdt(1000))),
            Tx("x3-swap", 110, Scammer, 0.001m, Token(Scammer, Pair, Usdt(1000)),
                Token(Pair, Scammer, new AssetAmount(KnownTokens.EthereumUsdc, 999_000_000))));

        Assert.Empty(graph.Account.Swaps);
    }

    [Fact]
    public async Task An_aggregator_is_a_swap_and_its_front_end_fee_to_another_address_makes_it_none()
    {
        var graph = await Build([],
            Tx("in-victim", 101, Victim, 0.001m, Token(Victim, Scammer, Usdt(2000))),
            Tx("in-swap", 110, Scammer, 0.001m, Token(Scammer, OneInch, Usdt(1000)), Internal(OneInch, Scammer, 0.5m)),
            Tx("in-fee", 120, Scammer, 0.001m, Token(Scammer, OneInch, Usdt(990)), Token(Scammer, FeeCollector, Usdt(10)),
                Internal(OneInch, Scammer, 0.49m)));

        var swap = Assert.Single(graph.Account.Swaps);

        Assert.Equal(Hash("in-swap"), swap.Transaction);
        Assert.Equal(KnownContractCategory.Aggregator, swap.Venue!.Category);
    }

    [Fact]
    public async Task Dust_from_the_recipient_never_makes_a_payment_a_swap_and_the_money_reaches_the_recipient()
    {
        // A deliberate obfuscation: a made-up token's dust from the recipient back in the same transaction.
        var fake = Asset.Token(SharedKernel.Chain.Ethereum, "0x1111111111111111111111111111111111111111", "FAKE", 18);
        var graph = await Build(["z-pay"],
            Tx("z-victim", 101, Victim, 0m, Token(Victim, Scammer, Usdt(1000))),
            Tx("z-pay", 110, Scammer, 0m, Token(Scammer, Merchant, Usdt(1000)),
                Token(Merchant, Scammer, new AssetAmount(fake, 1))),
            Tx("z-out", 130, Scammer, 0m, Token(Scammer, Exchange, new AssetAmount(fake, 1))));

        var result = await FromVictim(graph);

        Assert.Empty(graph.Account.Swaps);
        Assert.Equal(Usdt(1000), Assert.Single(result.Ends, end => end.NodeKey == Key(graph, Merchant)).Amount.Amount);
    }

    [Theory]
    [InlineData("FAKE")]
    [InlineData("USDC")]
    public async Task An_own_pool_with_a_fake_token_through_the_official_router_is_no_swap(string symbol)
    {
        // Uniswap pairs are open to anyone: the scammer's own pair USDT/FAKE through Router02, later emptied by an
        // accomplice. A known router is never enough; what came back must have a known value.
        var fake = Asset.Token(SharedKernel.Chain.Ethereum, "0x2222222222222222222222222222222222222222", symbol, 6);
        var graph = await Build(["o-swap"],
            Tx("o-victim", 101, Victim, 0m, Token(Victim, Scammer, Usdt(1000))),
            Tx("o-swap", 110, Scammer, 0m, Eth(Scammer, UniswapV2, 0m), Token(Scammer, BridgePool, Usdt(1000)),
                Token(BridgePool, Scammer, new AssetAmount(fake, 5))),
            Tx("o-out", 130, Scammer, 0m, Token(Scammer, Exchange, new AssetAmount(fake, 5))));

        var result = await FromVictim(graph);

        Assert.Empty(graph.Account.Swaps);
        Assert.Equal(Usdt(1000), Assert.Single(result.Ends, end => end.NodeKey == Key(graph, BridgePool)).Amount.Amount);
    }

    [Fact]
    public async Task A_label_set_later_refuses_the_swap_and_says_why()
    {
        _harness.Labels.Accounts[Pair.ToChainAddress()] = TrailEndReason.Exchange;
        var graph = await Build([],
            Tx("lr-victim", 101, Victim, 0.001m, Token(Victim, Scammer, Usdt(1000))),
            Tx("lr-swap", 110, Scammer, 0.001m, Token(Scammer, Pair, Usdt(1000)),
                Token(Pair, Scammer, new AssetAmount(KnownTokens.EthereumUsdc, 999_000_000))));

        var refusal = Assert.Single(graph.Account.RefusedSwaps);

        Assert.Equal(Pair.ToChainAddress(), refusal.Counterparty);
        Assert.Contains(TrailEndReason.Exchange.Describe(), refusal.Why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refund_in_the_asset_sent_is_never_a_purchase()
    {
        var graph = await Build(["u-refund"],
            Tx("u-victim", 101, Victim, 0m, Token(Victim, Scammer, Usdt(1000))),
            Tx("u-refund", 110, Scammer, 0m, Token(Scammer, Pair, Usdt(1000)), Token(Pair, Scammer, Usdt(5))));

        Assert.Empty(graph.Account.Swaps);
    }
}
