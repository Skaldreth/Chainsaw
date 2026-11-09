using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution.Tests;

/// <summary>
/// The sanction list's Solana addresses: <c>SOL</c> entries (and USDT written with a Solana address) are found on
/// Solana, kept as written since Base58 is case sensitive, and a hit is labelled on the address and read back on
/// Solana.
/// </summary>
public sealed class SolanaSanctionTests : IDisposable
{
    /// <summary>A made-up Solana address: 32 bytes of 0xdd.</summary>
    private static readonly EthereumAddress OnSolana = Key(0xdd);

    /// <summary>Another made-up Solana address: 32 bytes of 0xde.</summary>
    private static readonly EthereumAddress HoldsUsdt = Key(0xde);

    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Sol_and_usdt_entries_with_a_solana_address_are_found_on_solana()
    {
        _harness.Sanctions.Import(new SdnFile()
            .Entry("9401", "SYNTETISK", "CYBER3", ("Digital Currency Address - SOL", OnSolana.Value))
            .Entry("9402", "SYNTETISK", "CYBER3", ("Digital Currency Address - USDT", HoldsUsdt.Value))
            .Stream());

        Assert.Equal("SOL", Assert.Single(_harness.Sanctions.Find(OnSolana)!.Hits).Currency);
        Assert.Equal("USDT", Assert.Single(_harness.Sanctions.Find(HoldsUsdt)!.Hits).Currency);
        Assert.True(SanctionCurrency.On(Chain.Solana, "SOL"));
        Assert.False(SanctionCurrency.On(Chain.Ethereum, "SOL"));
    }

    [Fact]
    public void A_hit_on_solana_is_labelled_and_read_back_on_solana()
    {
        _harness.Sanctions.Import(new SdnFile()
            .Entry("9401", "SYNTETISK", "CYBER3", ("Digital Currency Address - SOL", OnSolana.Value))
            .Stream());

        var label = Assert.Single(_harness.Labels.CheckSanctions(_harness.Case, [OnSolana]).Added);

        Assert.Equal(OnSolana.ToChainAddress(), label.Subject.Target);
        Assert.Equal(OnSolana.Value, label.Subject.Stored);
        _harness.Reopen();
        var labels = _harness.Labels.GetLabels(_harness.Case, [OnSolana.ToChainAddress()]);
        Assert.Equal(Chain.Solana, labels[OnSolana.ToChainAddress()].Single().Subject.Target.Chain);
        Assert.True(_harness.Verify().IsIntact);
    }

    private static EthereumAddress Key(byte value) =>
        EthereumAddress.Parse(Chain.Solana, Base58.Encode(Enumerable.Repeat(value, 32).ToArray()));
}
