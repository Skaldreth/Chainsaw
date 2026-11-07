using System.Numerics;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// FIFO and haircut for the account model against worked examples, and against random ledgers to show that neither
/// creates nor loses traced money.
/// </summary>
public sealed class AccountMethodTests
{
    private static readonly ChainAddress Account = new(Chain.Ethereum, "0x" + new string('1', 40));

    private static readonly Asset Usdt =
        Asset.Token(Chain.Ethereum, "0xdac17f958d2ee523a2206206994597c13d831ec7", "USDT", 6);

    [Fact]
    public void FifoSendsTheTracedMoneyWithTheFirstTransfersAfterIt()
    {
        // 30 untraced at the start, then 50 in of which 50 traced; 40 out, 30 out, 10 left.
        var ledger = Ledger(Usdt, 30, In(50, 50), Out(40), Out(30));

        var taint = AccountChecks.Run(new AccountFifoMethod(), ledger);

        Assert.Equal([10, 30], taint.Outgoing.Select(share => (int)share.Units));
        Assert.Equal(10, (int)taint.Remaining.Units);
        Assert.True(taint.Fees.IsZero);
    }

    [Fact]
    public void HaircutSpreadsTheTracedShareOfTheBalance()
    {
        // 50 untraced and 50 traced: half of each transfer is traced.
        var ledger = Ledger(Usdt, 50, In(50, 50), Out(40), Out(20));

        var taint = AccountChecks.Run(new AccountHaircutMethod(), ledger);

        Assert.Equal([20, 10], taint.Outgoing.Select(share => (int)share.Units));
        Assert.Equal(20, (int)taint.Remaining.Units);
    }

    [Fact]
    public void FeesInEtherTakeTheirShare()
    {
        var ledger = Ledger(Asset.Ether, 0, In(100, 100), Fee(10), Out(50));

        var fifo = AccountChecks.Run(new AccountFifoMethod(), ledger);
        var haircut = AccountChecks.Run(new AccountHaircutMethod(), ledger);

        Assert.Equal(10, (int)fifo.Fees.Units);
        Assert.Equal(10, (int)haircut.Fees.Units);
        Assert.Equal(40, (int)fifo.Remaining.Units);
    }

    [Fact]
    public void MoreOutThanTheHistoryShowsCameInCreatesNothing()
    {
        var ledger = Ledger(Usdt, 0, In(10, 10), Out(25));

        Assert.Equal(10, (int)AccountChecks.Run(new AccountFifoMethod(), ledger).Outgoing[0].Units);
        Assert.Equal(10, (int)AccountChecks.Run(new AccountHaircutMethod(), ledger).Outgoing[0].Units);
    }

    [Fact]
    public void ATokenNeverPaysFees() =>
        Assert.Throws<ArgumentException>(() => new AccountFifoMethod().Propagate(Ledger(Usdt, 0, In(5, 5), Fee(1))));

    [Fact]
    public void AnEntryInAnotherAssetIsRefused()
    {
        var ledger = new AccountLedger(Account, Usdt, AssetAmount.Zero(Usdt), [Entry(Asset.Ether, 5, TransferDirection.In, 5)]);
        Assert.Throws<ArgumentException>(() => new AccountHaircutMethod().Propagate(ledger));
    }

    [Fact]
    public void MoreTracedThanCameInIsRefused() =>
        Assert.Throws<ArgumentException>(() => new AccountFifoMethod().Propagate(Ledger(Usdt, 0, In(5, 6))));

    [Fact]
    public void AMethodThatCreatesMoneyIsRefused() =>
        Assert.Throws<InvalidOperationException>(() => AccountChecks.Run(new Generous(), Ledger(Usdt, 0, In(5, 5), Out(5))));

    [Fact]
    public void BothMethodsKeepEveryUnitOverRandomLedgers()
    {
        var random = new Random(23);
        for (var round = 0; round < 300; round++)
        {
            var entries = new List<(int Amount, int Traced, TransferDirection Direction)>();
            for (var i = 0; i < random.Next(1, 12); i++)
            {
                var direction = (TransferDirection)random.Next(3);
                var amount = random.Next(0, 1_000);
                entries.Add((amount, direction == TransferDirection.In ? random.Next(0, amount + 1) : 0, direction));
            }

            var ledger = new AccountLedger(Account, Asset.Ether, new AssetAmount(Asset.Ether, random.Next(0, 500)),
                [.. entries.Select(e => Entry(Asset.Ether, e.Amount, e.Direction, e.Traced))]);
            AccountChecks.Run(new AccountFifoMethod(), ledger);
            AccountChecks.Run(new AccountHaircutMethod(), ledger);
        }
    }

    private static AccountLedger Ledger(Asset asset, int opening, params (int, int, TransferDirection)[] entries) =>
        new(Account, asset, new AssetAmount(asset, opening),
            [.. entries.Select(e => Entry(asset, e.Item1, e.Item3, e.Item2))]);

    private static (int, int, TransferDirection) In(int amount, int traced) => (amount, traced, TransferDirection.In);

    private static (int, int, TransferDirection) Out(int amount) => (amount, 0, TransferDirection.Out);

    private static (int, int, TransferDirection) Fee(int amount) => (amount, 0, TransferDirection.Fee);

    private static AccountEntry Entry(Asset asset, int amount, TransferDirection direction, int traced) => new(
        new AssetTransfer(new ChainTransactionRef(Chain.Ethereum, "0x" + new string('a', 64)),
            new AssetAmount(asset, amount), null, null),
        direction,
        new AssetAmount(asset, traced));

    /// <summary>A broken method that sends every outgoing transfer out fully traced.</summary>
    private sealed class Generous : IAccountTracingMethod
    {
        public TracingMethodId Id { get; } = new(TracingMethodKind.Fifo, 9);

        public ChainScope Scope { get; } = ChainScope.AllOf(LedgerModel.Account);

        public AccountTaint Propagate(AccountLedger ledger) => new(
            [.. ledger.Entries.Where(e => e.Direction == TransferDirection.Out).Select(e => e.Transfer.Amount)],
            AssetAmount.Zero(ledger.Asset),
            new AssetAmount(ledger.Asset, BigInteger.One));
    }
}
