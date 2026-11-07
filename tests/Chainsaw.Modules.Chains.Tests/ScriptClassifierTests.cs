using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Chains.Tests;

public sealed class ScriptClassifierTests
{
    private const string Key64 = "7777777777777777777777777777777777777777777777777777777777777777";

    [Theory]
    [InlineData("76a914" + "1111111111111111111111111111111111111111" + "88ac",
        BitcoinScriptType.PayToPublicKeyHash, "pkh")]
    [InlineData("a914" + "2222222222222222222222222222222222222222" + "87", BitcoinScriptType.PayToScriptHash, "sh")]
    [InlineData("0014" + "3333333333333333333333333333333333333333",
        BitcoinScriptType.PayToWitnessPublicKeyHash, "wpkh")]
    [InlineData("0020" + "4444444444444444444444444444444444444444444444444444444444444444",
        BitcoinScriptType.PayToWitnessScriptHash, "wsh")]
    [InlineData("5120" + "5555555555555555555555555555555555555555555555555555555555555555",
        BitcoinScriptType.PayToTaproot, "tr")]
    public void Scripts_with_addresses_give_the_fixture_address(string script, BitcoinScriptType type, string key)
    {
        var (actualType, address) = ScriptClassifier.Classify(Convert.FromHexString(script), BitcoinNetwork.Mainnet);

        Assert.Equal(type, actualType);
        Assert.Equal(Fixtures.Address(key), address);
    }

    [Theory]
    [InlineData("6a0b68656c6c6f20776f726c64", BitcoinScriptType.OpReturn)]
    [InlineData("6a", BitcoinScriptType.OpReturn)]
    [InlineData("21" + "02" + "7777777777777777777777777777777777777777777777777777777777777777" + "ac",
        BitcoinScriptType.PayToPublicKey)]
    [InlineData("41" + "04" + Key64 + Key64 + "ac",
        BitcoinScriptType.PayToPublicKey)]
    [InlineData("5121" + "02" + "7777777777777777777777777777777777777777777777777777777777777777" + "51ae",
        BitcoinScriptType.Multisig)]
    [InlineData("0018" + "333333333333333333333333333333333333333333333333",
        BitcoinScriptType.NonStandard)] // v0, 24 bytes
    [InlineData("", BitcoinScriptType.NonStandard)]
    [InlineData("ac", BitcoinScriptType.NonStandard)]
    public void Scripts_without_addresses_have_none(string script, BitcoinScriptType type)
    {
        var (actualType, address) = ScriptClassifier.Classify(Convert.FromHexString(script), BitcoinNetwork.Mainnet);

        Assert.Equal(type, actualType);
        Assert.Null(address);
    }

    [Fact]
    public void A_future_witness_version_has_a_bech32m_address()
    {
        var (type, address) = ScriptClassifier.Classify(Convert.FromHexString("5202751e"), BitcoinNetwork.Mainnet);

        Assert.Equal(BitcoinScriptType.WitnessUnknown, type);
        Assert.Equal(BitcoinScriptType.WitnessUnknown, address!.Type);
        Assert.True(BitcoinAddress.TryParse(address.Value, out _));
    }

    [Fact]
    public void The_network_decides_the_prefix()
    {
        var script = Convert.FromHexString("0014" + "3333333333333333333333333333333333333333");

        var (_, address) = ScriptClassifier.Classify(script, BitcoinNetwork.Testnet);

        Assert.StartsWith("tb1q", address!.Value, StringComparison.Ordinal);
        Assert.Equal(BitcoinNetwork.Testnet, address.Network);
    }
}
