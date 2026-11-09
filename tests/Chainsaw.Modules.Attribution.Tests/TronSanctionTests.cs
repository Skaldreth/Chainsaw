using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution.Tests;

/// <summary>
/// The sanction list's TRON addresses: TRX, and USDT written with a TRON address, are found on TRON, never on Ethereum
/// for the same 20 bytes, and a hit is labelled on the TRON account as on the other chains.
/// </summary>
public sealed class TronSanctionTests : IDisposable
{
    /// <summary>A made-up TRON address: the version byte 0x41 and 20 bytes of 0xaa.</summary>
    private static readonly EthereumAddress Launderer = EthereumAddress.Parse(Chain.Tron, "41" + new string('a', 40));

    /// <summary>A made-up TRON address: the version byte 0x41 and 20 bytes of 0xbb.</summary>
    private static readonly EthereumAddress Broker = EthereumAddress.Parse(Chain.Tron, "41" + new string('b', 40));

    /// <summary>The same 20 bytes as <see cref="Launderer"/> on Ethereum.</summary>
    private static readonly EthereumAddress Twin = EthereumAddress.Parse("0x" + new string('a', 40));

    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Trx_and_usdt_on_tron_are_found_and_never_for_the_same_bytes_on_ethereum()
    {
        _harness.Sanctions.Import(new SdnFile()
            .Entry("9201", "SYNTETISK TVÄTTARE", "CYBER3", ("Digital Currency Address - TRX", Launderer.Value))
            .Entry("9202", "SYNTETISK MÄKLARE", "CYBER3", ("Digital Currency Address - USDT", Broker.Value))
            .Stream());

        Assert.Equal("TRX", Assert.Single(_harness.Sanctions.Find(Launderer)!.Hits).Currency);
        Assert.Equal("USDT", Assert.Single(_harness.Sanctions.Find(Broker)!.Hits).Currency);
        Assert.Empty(_harness.Sanctions.Find(Twin)!.Hits);
    }

    [Fact]
    public void A_tron_address_under_ether_is_not_taken_for_tron()
    {
        _harness.Sanctions.Import(new SdnFile()
            .Entry("9203", "SYNTETISK", "CYBER3", ("Digital Currency Address - ETH", Launderer.Value))
            .Stream());

        Assert.Empty(_harness.Sanctions.Find(Launderer)!.Hits);
    }

    [Fact]
    public void A_tron_hit_is_labelled_sanctioned_on_the_account_and_read_back()
    {
        _harness.Sanctions.Import(new SdnFile()
            .Entry("9201", "SYNTETISK TVÄTTARE", "CYBER3", ("Digital Currency Address - TRX", Launderer.Value))
            .Stream());

        var check = _harness.Labels.CheckSanctions(_harness.Case, [Launderer, Broker]);

        var label = Assert.Single(check.Added);
        Assert.Equal(LabelKind.Sanctioned, label.Kind);
        Assert.Equal(Launderer.ToChainAddress(), label.Subject.Target);
        Assert.Equal(Launderer.Value, label.Subject.ToString());
        var labels = _harness.Labels.GetLabels(_harness.Case, [Launderer.ToChainAddress()]);
        Assert.Equal(Launderer.ToChainAddress(), labels.Values.Single().Single().Subject.Target);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_label_subject_on_tron_keeps_its_chain()
    {
        var subject = LabelSubject.ForAccount(Launderer.ToChainAddress());

        Assert.Equal(Chain.Tron, subject.Target.Chain);
        Assert.Throws<ArgumentException>(() => LabelSubject.ForAccount(new ChainAddress(Chain.Tron, "T123")));
    }
}
