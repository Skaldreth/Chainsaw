using Chainsaw.Modules.Chains.Contracts;
using static Chainsaw.Modules.Tracing.Tests.Synthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// The sketches' made-up case as synthetic transactions: a victim pays a scammer, the scammer collects two victims'
/// money, peels it off in a chain (to an unknown receiver, to an exchange, into a Whirlpool mix) and keeps the rest.
/// All addresses and hashes are derived from labels; none is real.
/// </summary>
internal sealed class Flows
{
    public Flows()
    {
        Pay = Tx("pay", 900_000)
            .In(Wpkh("victim"), 120_000_000)
            .Out(Wpkh("scammer-1"), 100_000_000)
            .Out(Wpkh("victim-change"), 19_990_000)
            .Build();
        OtherVictim = Tx("pay-2", 900_001)
            .In(Wpkh("victim-2"), 60_000_000)
            .Out(Wpkh("scammer-2"), 50_000_000)
            .Out(Wpkh("victim-2-change"), 9_990_000)
            .Build();
        Collect = Tx("collect", 900_010)
            .Spend(Pay, 0)
            .Spend(OtherVictim, 0)
            .Out(Wpkh("collector"), 149_980_000)
            .Build();
        Peel1 = Tx("peel-1", 900_020)
            .Spend(Collect, 0)
            .Out(Wpkh("unknown-receiver"), 10_000_000)
            .Out(Wpkh("peel-1-rest"), 139_970_000)
            .Build();
        Onward = Tx("onward", 900_025)
            .Spend(Peel1, 0)
            .Out(Wpkh("somewhere"), 9_990_000)
            .Build();
        Further = Tx("further", 900_060)
            .Spend(Onward, 0)
            .Out(Wpkh("further"), 9_980_000)
            .Build();
        Peel2 = Tx("peel-2", 900_030)
            .Spend(Peel1, 1)
            .Out(Wpkh("exchange-deposit"), 20_000_000)
            .Out(Wpkh("peel-2-rest"), 119_960_000)
            .Build();
        Sweep = Tx("sweep", 900_035)
            .Spend(Peel2, 0)
            .In(Wpkh("other-customer"), 5_000_000)
            .Out(Pkh("exchange-hot"), 24_990_000)
            .Build();
        Peel3 = Tx("peel-3", 900_040)
            .Spend(Peel2, 1)
            .Out(Wpkh("premix"), 1_005_000)
            .Out(Wpkh("kept"), 118_945_000)
            .Build();
        var mix = Tx("mix", 900_050).Spend(Peel3, 0);
        mix.In(Wpkh("mixer-in-2"), 1_005_000);
        for (var i = 3; i <= 5; i++)
        {
            mix.In(Wpkh($"mixer-in-{i}"), 1_000_000);
        }

        for (var i = 1; i <= 5; i++)
        {
            mix.Out(Wpkh($"mixed-{i}"), 1_000_000);
        }

        Mix = mix.Build();
    }

    public BitcoinTransaction Pay { get; }

    public BitcoinTransaction OtherVictim { get; }

    public BitcoinTransaction Collect { get; }

    public BitcoinTransaction Peel1 { get; }

    public BitcoinTransaction Onward { get; }

    /// <summary>Spends the unknown receiver's money further; it is on the chain but never expanded.</summary>
    public BitcoinTransaction Further { get; }

    public BitcoinTransaction Peel2 { get; }

    public BitcoinTransaction Sweep { get; }

    public BitcoinTransaction Peel3 { get; }

    public BitcoinTransaction Mix { get; }

    public IEnumerable<BitcoinTransaction> All =>
        [Pay, OtherVictim, Collect, Peel1, Onward, Further, Peel2, Sweep, Peel3, Mix];

    /// <summary>
    /// Puts the flows on the harness's chain, labels the exchange, starts at the victim's payment and expands it hop
    /// by hop as a user would.
    /// </summary>
    public async Task ExpandInto(CaseHarness harness)
    {
        harness.Source.Add([.. All]);
        harness.Labels.Addresses[Wpkh("exchange-deposit")] = Contracts.TrailEndReason.Exchange;
        harness.Service.AddStartPoint(harness.Case, Contracts.TraceStartPoint.ForTransaction(Pay.Txid));
        foreach (var transaction in new[] { Pay, Collect, Peel1, Peel2, Peel3 })
        {
            await harness.Expand(transaction);
        }
    }
}
