using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution.Tests;

/// <summary>
/// The sanction list's Ethereum addresses: ether and USDT and USDC on Ethereum are found whatever the letters' case,
/// an address under another chain's code is never taken for Ethereum, and a hit is labelled as Bitcoin's are.
/// </summary>
public sealed class EthereumSanctionTests : IDisposable
{
    private static readonly EthereumAddress Mixer = EthereumAddress.Parse("0x" + new string('a', 40));

    private static readonly EthereumAddress Broker = EthereumAddress.Parse("0x" + new string('b', 38) + "12");

    private static readonly EthereumAddress Bridge = EthereumAddress.Parse("0x" + new string('c', 40));

    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Ether_and_tokens_on_ethereum_are_found_and_other_chains_are_not()
    {
        _harness.Sanctions.Import(new SdnFile()
            .Entry("9101", "SYNTETISK MIXER", "CYBER3", ("Digital Currency Address - ETH", Mixer.Checksummed))
            .Entry("9102", "SYNTETISK MÄKLARE", "CYBER3", ("Digital Currency Address - USDT", Broker.Value))
            .Entry("9103", "SYNTETISK BRYGGA", "CYBER3", ("Digital Currency Address - BSC", Bridge.Value))
            .Stream());

        var mixer = _harness.Sanctions.Find(Mixer)!;
        var broker = _harness.Sanctions.Find(Broker)!;
        var bridge = _harness.Sanctions.Find(Bridge)!;

        Assert.Equal("SYNTETISK MIXER", Assert.Single(mixer.Hits).EntryName);
        Assert.Equal("USDT", Assert.Single(broker.Hits).Currency);
        Assert.Empty(bridge.Hits);
    }

    [Fact]
    public void An_ether_address_whose_checksum_is_wrong_can_never_match_and_is_counted_as_skipped()
    {
        var wrong = SwapCaseOfFirstLetter(Broker.Checksummed);
        var info = _harness.Sanctions.Import(new SdnFile()
            .Entry("9104", "SYNTETISK", "CYBER3", ("Digital Currency Address - ETH", wrong))
            .Entry("9105", "SYNTETISK TRON", "CYBER3", ("Digital Currency Address - USDT", "TQ7mX9inteEthereum"))
            .Stream());

        Assert.Equal(1, info.Skipped);
        Assert.Empty(_harness.Sanctions.Find(Broker)!.Hits);
    }

    [Fact]
    public void Without_a_list_nothing_is_found()
    {
        Assert.Null(_harness.Sanctions.Find(Mixer));
    }

    [Fact]
    public void An_ethereum_hit_is_labelled_sanctioned_on_the_account_with_the_list_as_its_origin()
    {
        var info = _harness.Sanctions.Import(new SdnFile()
            .Entry("9101", "SYNTETISK MIXER", "CYBER3", ("Digital Currency Address - ETH", Mixer.Checksummed))
            .Stream());

        var check = _harness.Labels.CheckSanctions(_harness.Case, [Mixer, Bridge]);

        var label = Assert.Single(check.Added);
        Assert.Equal(LabelKind.Sanctioned, label.Kind);
        Assert.Equal(new ChainAddress(Chain.Ethereum, Mixer.Value), label.Subject.Target);
        Assert.Equal(info.Version, label.Origin.SourceVersion);
        Assert.Contains("Digital Currency Address - ETH", label.Origin.Reason, StringComparison.Ordinal);
        Assert.Contains(Mixer.Value, _harness.Case.ReadJournal().Last().Details, StringComparison.Ordinal);
        var labels = _harness.Labels.GetLabels(_harness.Case, [new ChainAddress(Chain.Ethereum, Mixer.Value)]);
        Assert.Single(labels.Values.Single());
        Assert.Empty(_harness.Labels.CheckSanctions(_harness.Case, [Mixer]).Added);
        Assert.True(_harness.Verify().IsIntact);
    }

    /// <summary>Changes the case of the first letter after <c>0x</c>, which breaks an EIP-55 checksum.</summary>
    private static string SwapCaseOfFirstLetter(string checksummed)
    {
        var characters = checksummed.ToCharArray();
        var index = Array.FindIndex(characters, 2, char.IsLetter);
        characters[index] = char.IsUpper(characters[index])
            ? char.ToLowerInvariant(characters[index])
            : char.ToUpperInvariant(characters[index]);
        return new string(characters);
    }
}
