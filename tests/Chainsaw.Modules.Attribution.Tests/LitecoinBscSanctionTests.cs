using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution.Tests;

/// <summary>
/// The sanction list's Litecoin and BSC addresses: <c>LTC</c> entries are found on Litecoin (the older <c>3…</c> form
/// as its <c>M…</c> form) and never on Bitcoin, <c>BSC</c> entries on BSC and never on Ethereum for the same 20 bytes,
/// and a hit is labelled on the address and read back on its chain.
/// </summary>
public sealed class LitecoinBscSanctionTests : IDisposable
{
    /// <summary>A made-up Litecoin address: P2PKH (<c>L…</c>) of 20 bytes of 0xaa.</summary>
    private static readonly BitcoinAddress LitecoinKey = BitcoinAddress.FromHash(
        BitcoinScriptType.PayToPublicKeyHash, Bytes(0xaa), BitcoinNetwork.Litecoin);

    /// <summary>A made-up Litecoin address: P2SH (<c>M…</c>) of 20 bytes of 0xbb.</summary>
    private static readonly BitcoinAddress LitecoinScript = BitcoinAddress.FromHash(
        BitcoinScriptType.PayToScriptHash, Bytes(0xbb), BitcoinNetwork.Litecoin);

    /// <summary>The same script as <see cref="LitecoinScript"/> in the older form, Bitcoin's <c>3…</c>.</summary>
    private static readonly BitcoinAddress OldForm = BitcoinAddress.FromHash(
        BitcoinScriptType.PayToScriptHash, Bytes(0xbb), BitcoinNetwork.Mainnet);

    /// <summary>A made-up BSC address: 20 bytes of 0xcc.</summary>
    private static readonly EthereumAddress OnBsc = EthereumAddress.Parse(Chain.Bsc, "0x" + new string('c', 40));

    /// <summary>The same 20 bytes on Ethereum.</summary>
    private static readonly EthereumAddress Twin = EthereumAddress.Parse("0x" + new string('c', 40));

    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Litecoin_entries_are_found_on_litecoin_and_never_on_bitcoin()
    {
        _harness.Sanctions.Import(new SdnFile()
            .Entry("9301", "SYNTETISK TVÄTTARE", "CYBER3", ("Digital Currency Address - LTC", LitecoinKey.Value))
            .Entry("9302", "SYNTETISK MÄKLARE", "CYBER3", ("Digital Currency Address - LTC", OldForm.Value))
            .Stream());

        Assert.Equal("LTC", Assert.Single(_harness.Sanctions.Find(LitecoinKey)!.Hits).Currency);
        Assert.Equal("9302", Assert.Single(_harness.Sanctions.Find(LitecoinScript)!.Hits).EntryUid);
        Assert.Empty(_harness.Sanctions.Find(OldForm)!.Hits);
    }

    [Fact]
    public void Bsc_entries_are_found_on_bsc_and_never_on_ethereum()
    {
        _harness.Sanctions.Import(new SdnFile()
            .Entry("9303", "SYNTETISK", "CYBER3", ("Digital Currency Address - BSC", OnBsc.Checksummed))
            .Stream());

        Assert.Equal("BSC", Assert.Single(_harness.Sanctions.Find(OnBsc)!.Hits).Currency);
        Assert.Empty(_harness.Sanctions.Find(Twin)!.Hits);
    }

    [Fact]
    public void An_ethereum_entry_is_not_taken_for_bsc()
    {
        _harness.Sanctions.Import(new SdnFile()
            .Entry("9304", "SYNTETISK", "CYBER3", ("Digital Currency Address - ETH", Twin.Value))
            .Stream());

        Assert.Empty(_harness.Sanctions.Find(OnBsc)!.Hits);
    }

    [Fact]
    public void Hits_on_litecoin_and_bsc_are_labelled_and_read_back_on_their_chains()
    {
        _harness.Sanctions.Import(new SdnFile()
            .Entry("9301", "SYNTETISK TVÄTTARE", "CYBER3", ("Digital Currency Address - LTC", LitecoinKey.Value))
            .Entry("9303", "SYNTETISK", "CYBER3", ("Digital Currency Address - BSC", OnBsc.Value))
            .Stream());

        var litecoin = Assert.Single(_harness.Labels.CheckSanctions(_harness.Case, [LitecoinKey]).Added);
        var bsc = Assert.Single(_harness.Labels.CheckSanctions(_harness.Case, [OnBsc]).Added);

        Assert.Equal(LitecoinKey.ToChainAddress(), litecoin.Subject.Target);
        Assert.Equal(OnBsc.ToChainAddress(), bsc.Subject.Target);
        _harness.Reopen();
        var labels = _harness.Labels.GetLabels(
            _harness.Case, [LitecoinKey.ToChainAddress(), OnBsc.ToChainAddress(), Twin.ToChainAddress()]);
        Assert.Equal(Chain.Litecoin, labels[LitecoinKey.ToChainAddress()].Single().Subject.Target.Chain);
        Assert.Equal(Chain.Bsc, labels[OnBsc.ToChainAddress()].Single().Subject.Target.Chain);
        Assert.False(labels.ContainsKey(Twin.ToChainAddress()));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_subject_on_bsc_is_stored_with_its_chain_and_on_litecoin_as_written()
    {
        var bsc = LabelSubject.ForAccount(OnBsc.ToChainAddress());
        var litecoin = LabelSubject.ForAccount(LitecoinKey.ToChainAddress());

        Assert.Equal("bsc:" + OnBsc.Value, bsc.Stored);
        Assert.Equal(LitecoinKey.Value, litecoin.Stored);
        Assert.Equal(Twin.Value, LabelSubject.ForAccount(Twin.ToChainAddress()).Stored);
    }

    /// <summary>Gives 20 bytes of one value.</summary>
    private static byte[] Bytes(byte value) => Enumerable.Repeat(value, 20).ToArray();
}
