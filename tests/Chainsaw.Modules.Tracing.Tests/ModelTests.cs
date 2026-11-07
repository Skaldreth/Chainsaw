using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using static Chainsaw.Modules.Tracing.Tests.Synthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// The contracts per model of chain: the methods registered per model and chain, the UTXO model's flow, the edges as
/// transfers of an asset, and the account model's contract, which no chain uses yet.
/// </summary>
public sealed class ModelTests
{
    private static readonly ChainAddress Account = new(Chain.Ethereum, "0x" + new string('a', 40));

    [Fact]
    public void Bitcoin_gets_the_utxo_methods_and_an_account_method_of_the_same_name_stays_apart()
    {
        ITracingMethod[] methods = [new FifoMethod(), new HaircutMethod(), new ProportionalAccounts()];

        var bitcoin = TracingMethods.For<IUtxoTracingMethod>(methods, Chain.Bitcoin);
        var ethereum = TracingMethods.For<IAccountTracingMethod>(methods, Chain.Ethereum);

        Assert.Equal([TracingMethodKind.Fifo, TracingMethodKind.Haircut], bitcoin.Keys.Order());
        Assert.IsType<FifoMethod>(bitcoin[TracingMethodKind.Fifo]);
        Assert.IsType<ProportionalAccounts>(Assert.Single(ethereum).Value);
        Assert.Empty(TracingMethods.For<IUtxoTracingMethod>(methods, Chain.Ethereum));
        Assert.Empty(TracingMethods.For<IAccountTracingMethod>(methods, Chain.Bitcoin));
    }

    [Fact]
    public void A_method_that_does_not_implement_its_models_contract_is_refused_for_its_chain_only()
    {
        ITracingMethod[] methods = [new FifoMethod(), new MisplacedMethod()];

        Assert.Throws<ArgumentException>(() => TracingMethods.For<IAccountTracingMethod>(methods, Chain.Ethereum));
        Assert.Single(TracingMethods.For<IUtxoTracingMethod>(methods, Chain.Bitcoin));
    }

    [Fact]
    public void The_same_method_and_version_twice_for_a_chain_is_refused()
    {
        ITracingMethod[] methods = [new FifoMethod(), new FifoMethod()];

        Assert.Throws<ArgumentException>(() => TracingMethods.For<IUtxoTracingMethod>(methods, Chain.Bitcoin));
    }

    [Fact]
    public void The_service_traces_bitcoin_by_its_methods_when_an_account_method_is_registered()
    {
        using var harness = new CaseHarness(services =>
            services.AddSingleton<ITracingMethod>(new ProportionalAccounts()));

        Assert.Equal(
            [new TracingMethodId(TracingMethodKind.Fifo, 1), new TracingMethodId(TracingMethodKind.Haircut, 1)],
            harness.Service.Methods);
    }

    [Fact]
    public void A_utxo_flow_knows_its_fee_and_refuses_what_cannot_be()
    {
        var flow = new UtxoFlow([600, 400], [700, 290]);

        Assert.Equal(1_000, flow.TotalIn);
        Assert.Equal(990, flow.TotalOut);
        Assert.Equal(10, flow.Fee);
        Assert.Throws<ArgumentException>(() => new UtxoFlow([], [0]));
        Assert.Throws<ArgumentException>(() => new UtxoFlow([100], [-1]));
        Assert.Throws<ArgumentException>(() => new UtxoFlow([100], [101]));
    }

    [Fact]
    public void The_methods_only_see_amounts_and_bitcoin_gives_them_its_transaction()
    {
        var tx = Tx("flow").In(Wpkh("a"), 600).In(Wpkh("b"), 400).Out(Wpkh("c"), 700).Out(Wpkh("d"), 290).Build();

        var flow = MethodChecks.Flow(tx);

        Assert.Equal([600L, 400L], flow.Inputs);
        Assert.Equal([700L, 290L], flow.Outputs);
        Assert.Equal(tx.Fee.Value, flow.Fee);
    }

    [Fact]
    public void A_transfer_of_a_coin_names_bitcoins_transaction_and_the_coin()
    {
        var tx = Tx("paid").In(Wpkh("a"), 1_000).Out(Wpkh("b"), 990).Build();
        var coin = new Outpoint(tx.Txid, 0);

        var transfer = AssetTransfer.OfCoin(tx.Txid, coin, new Satoshi(990), tx.Block);

        Assert.Equal(new ChainTransactionRef(Chain.Bitcoin, tx.Txid.Value), transfer.Transaction);
        Assert.Equal(new AssetAmount(Asset.Bitcoin, 990), transfer.Amount);
        Assert.Equal(coin, transfer.Coin);
        Assert.Equal(tx.Block, transfer.Block);
    }

    [Fact]
    public void An_edge_of_the_chains_coin_keeps_its_key_and_a_tokens_edge_gets_its_own()
    {
        var usdt = Asset.Token(Chain.Ethereum, "0x" + new string('d', 40), "USDT", 6);

        Assert.Equal("transfer|a|b", GraphBuilder.EdgeKey(GraphEdgeKind.Transfer, "a", "b", Asset.Bitcoin));
        Assert.Equal(
            "other-chain|a|b", GraphBuilder.EdgeKey(GraphEdgeKind.ContinuesOnOtherChain, "a", "b", Asset.Bitcoin));
        Assert.Equal("transfer|a|b|Ethereum", GraphBuilder.EdgeKey(GraphEdgeKind.Transfer, "a", "b", Asset.Ether));
        Assert.Equal(
            "transfer|a|b|Ethereum:0x" + new string('d', 40),
            GraphBuilder.EdgeKey(GraphEdgeKind.Transfer, "a", "b", usdt));
    }

    [Fact]
    public void The_account_contract_carries_wei_beyond_64_bits_and_fees_without_losing_any()
    {
        var thousand = AssetAmount.Parse(Asset.Ether, "1000");
        var ledger = new AccountLedger(
            Account,
            Asset.Ether,
            AssetAmount.Parse(Asset.Ether, "3000"),
            [
                Entry('1', thousand, TransferDirection.In, traced: thousand),
                Entry('2', AssetAmount.Parse(Asset.Ether, "2000"), TransferDirection.Out),
                Entry('3', AssetAmount.Parse(Asset.Ether, "0.4"), TransferDirection.Fee),
            ]);

        var taint = new ProportionalAccounts().Propagate(ledger);

        // 1 000 of 4 000 ether is traced, so a quarter of the 2 000 that leave: 500 ether, 5 × 10^20 wei. Of the
        // 2 000 left, 500 are traced, so a quarter of the 0.4 ether gas: 0.1 ether.
        var outgoing = Assert.Single(taint.Outgoing);
        Assert.Equal(AssetAmount.Parse(Asset.Ether, "500"), outgoing);
        Assert.True(outgoing.Units > long.MaxValue);
        Assert.Equal(AssetAmount.Parse(Asset.Ether, "0.1"), taint.Fees);
        Assert.Equal(thousand, outgoing + taint.Fees + taint.Remaining);
    }

    private static AccountEntry Entry(
        char hash, AssetAmount amount, TransferDirection direction, AssetAmount? traced = null) => new(
        new AssetTransfer(new ChainTransactionRef(Chain.Ethereum, "0x" + new string(hash, 64)), amount, null, null),
        direction,
        traced ?? AssetAmount.Zero(amount.Asset));

    /// <summary>
    /// A made-up method for accounts that lets each outgoing transfer take the traced share of the balance, to show
    /// that the contract holds one; it is not one of Chainsaw's methods.
    /// </summary>
    private sealed class ProportionalAccounts : IAccountTracingMethod
    {
        public TracingMethodId Id { get; } = new(TracingMethodKind.Fifo, 1);

        public ChainScope Scope { get; } = ChainScope.AllOf(LedgerModel.Account);

        public AccountTaint Propagate(AccountLedger ledger)
        {
            var balance = ledger.Opening.Units;
            var traced = BigInteger.Zero;
            var fees = BigInteger.Zero;
            var outgoing = new List<AssetAmount>();
            foreach (var entry in ledger.Entries)
            {
                if (entry.Direction == TransferDirection.In)
                {
                    balance += entry.Transfer.Amount.Units;
                    traced += entry.Traced.Units;
                    continue;
                }

                var share = balance.IsZero ? BigInteger.Zero : traced * entry.Transfer.Amount.Units / balance;
                if (entry.Direction == TransferDirection.Fee)
                {
                    fees += share;
                }
                else
                {
                    outgoing.Add(new AssetAmount(ledger.Asset, share));
                }

                (balance, traced) = (balance - entry.Transfer.Amount.Units, traced - share);
            }

            return new AccountTaint(
                outgoing, new AssetAmount(ledger.Asset, fees), new AssetAmount(ledger.Asset, traced));
        }
    }

    /// <summary>A method for coins that is wrongly registered for accounts.</summary>
    private sealed class MisplacedMethod : IUtxoTracingMethod
    {
        public TracingMethodId Id { get; } = new(TracingMethodKind.Haircut, 7);

        public ChainScope Scope { get; } = ChainScope.Only(Chain.Ethereum);

        public TransactionTaint Propagate(UtxoFlow flow, IReadOnlyList<TaintSet> inputs) =>
            throw new InvalidOperationException("Never called.");
    }
}
