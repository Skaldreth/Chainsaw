using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

public sealed class AddressTests
{
    [Theory]
    [InlineData("pkh", BitcoinScriptType.PayToPublicKeyHash)]
    [InlineData("sh", BitcoinScriptType.PayToScriptHash)]
    [InlineData("wpkh", BitcoinScriptType.PayToWitnessPublicKeyHash)]
    [InlineData("wsh", BitcoinScriptType.PayToWitnessScriptHash)]
    [InlineData("tr", BitcoinScriptType.PayToTaproot)]
    public void The_fixture_addresses_are_valid_with_their_type(string key, BitcoinScriptType type)
    {
        var address = Fixtures.Address(key);

        Assert.Equal(type, address.Type);
        Assert.Equal(BitcoinNetwork.Mainnet, address.Network);
        Assert.Equal(Chain.Bitcoin, address.Chain);
        Assert.Equal(Fixtures.AddressText(key), address.Value);
    }

    [Theory]
    [InlineData("1BgGZ9tcN4rm9KBzDn7KprQz87SZ26SAMH", BitcoinScriptType.PayToPublicKeyHash, BitcoinNetwork.Mainnet)]
    [InlineData("bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4", BitcoinScriptType.PayToWitnessPublicKeyHash, BitcoinNetwork.Mainnet)]
    [InlineData("BC1QW508D6QEJXTDG4Y5R3ZARVARY0C5XW7KV8F3T4", BitcoinScriptType.PayToWitnessPublicKeyHash, BitcoinNetwork.Mainnet)]
    [InlineData("tb1qrp33g0q5c5txsp9arysrx4k6zdkfs4nce4xj0gdcccefvpysxf3q0sl5k7", BitcoinScriptType.PayToWitnessScriptHash, BitcoinNetwork.Testnet)]
    [InlineData("bc1p0xlxvlhemja6c4dqv22uapctqupfhlxm9h8z3k2e72q4k9hcz7vqzk5jj0", BitcoinScriptType.PayToTaproot, BitcoinNetwork.Mainnet)]
    [InlineData("bc1pw508d6qejxtdg4y5r3zarvary0c5xw7kw508d6qejxtdg4y5r3zarvary0c5xw7kt5nd6y", BitcoinScriptType.WitnessUnknown, BitcoinNetwork.Mainnet)]
    [InlineData("BC1SW50QGDZ25J", BitcoinScriptType.WitnessUnknown, BitcoinNetwork.Mainnet)]
    [InlineData("bc1zw508d6qejxtdg4y5r3zarvaryvaxxpcs", BitcoinScriptType.WitnessUnknown, BitcoinNetwork.Mainnet)]
    public void Test_vectors_from_BIP_173_and_350_are_valid(string text, BitcoinScriptType type, BitcoinNetwork network)
    {
        Assert.True(BitcoinAddress.TryParse(text, out var address));
        Assert.Equal(type, address.Type);
        Assert.Equal(network, address.Network);
    }

    [Fact]
    public void A_segwit_address_is_kept_in_lower_case()
    {
        var address = BitcoinAddress.Parse("BC1QW508D6QEJXTDG4Y5R3ZARVARY0C5XW7KV8F3T4");

        Assert.Equal("bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4", address.Value);
        Assert.Equal(BitcoinAddress.Parse("bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4"), address);
    }

    [Theory]
    [InlineData("bc1p0xlxvlhemja6c4dqv22uapctqupfhlxm9h8z3k2e72q4k9hcz7vqh2y7hd")] // v1 with a Bech32 checksum
    [InlineData("BC1S0XLXVLHEMJA6C4DQV22UAPCTQUPFHLXM9H8Z3K2E72Q4K9HCZ7VQ54WELL")] // v16 with a Bech32 checksum
    [InlineData("bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kemeawh")] // v0 with a Bech32m checksum
    [InlineData("tb1q0xlxvlhemja6c4dqv22uapctqupfhlxm9h8z3k2e72q4k9hcz7vq24jc47")] // v0 with a Bech32m checksum
    [InlineData("bc1p38j9r5y49hruaue7wxjce0updqjuyyx0kh56v8s25huc6995vvpql3jow4")] // 'o' is not in the charset
    [InlineData("BC130XLXVLHEMJA6C4DQV22UAPCTQUPFHLXM9H8Z3K2E72Q4K9HCZ7VQ7ZWS8R")] // version 17
    [InlineData("bc1pw5dgrnzv")] // a program of one byte
    [InlineData("BC1QR508D6QEJXTDG4Y5R3ZARVARYV98GJ9P")] // v0 with a 16-byte program
    [InlineData("tb1qrp33g0q5c5txsp9arysrx4k6zdkfs4nce4xj0gdcccefvpysxf3q0sL5k7")] // mixed case
    [InlineData("bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t5")] // one character changed
    [InlineData("ltc1qw508d6qejxtdg4y5r3zarvary0c5xw7kgmn4n9")] // another chain's prefix
    [InlineData("bc1gmk9yu")] // no data
    [InlineData("")]
    [InlineData(" bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4")]
    public void Invalid_segwit_addresses_are_refused(string text)
    {
        Assert.False(BitcoinAddress.TryParse(text, out _));
    }

    [Fact]
    public void A_changed_character_in_a_base58_address_breaks_the_checksum()
    {
        var valid = Fixtures.AddressText("pkh");
        var changed = valid[..^1] + (valid[^1] == 'H' ? 'J' : 'H');

        Assert.True(BitcoinAddress.TryParse(valid, out _));
        Assert.False(BitcoinAddress.TryParse(changed, out _));
    }

    [Theory]
    [InlineData("1BgGZ9tcN4rm9KBzDn7KprQz87SZ26SAM0")] // '0' is not in the alphabet
    [InlineData("1BgGZ9tcN4rm9KBzDn7KprQz87SZ26SAMl")] // 'l' is not in the alphabet
    [InlineData("1BgGZ9tcN4rm9KBzDn7KprQz87SZ26SAM")] // a character short
    [InlineData("LVuDpNCSSj6pQ7t9Pv6d6sUkLKoqDEVUnJ")] // a valid checksum with Litecoin's version byte
    [InlineData("1111111111111111111111111111111111111111111111111111111111111111111")]
    public void Invalid_base58_addresses_are_refused(string text)
    {
        Assert.False(BitcoinAddress.TryParse(text, out _));
    }

    [Fact]
    public void Base58Check_round_trips_with_leading_zeros()
    {
        byte[] data = [0, 0, 1, 2, 3];

        var text = Base58Check.Encode(data);

        Assert.StartsWith("11", text, StringComparison.Ordinal);
        Assert.True(Base58Check.TryDecode(text, out var decoded));
        Assert.Equal(data, decoded);
    }

    [Fact]
    public void Addresses_written_from_hashes_read_back_the_same()
    {
        var hash = Enumerable.Repeat((byte)0x11, 20).ToArray();
        var program = Enumerable.Repeat((byte)0x55, 32).ToArray();

        Assert.Equal(Fixtures.KeyHash,
            BitcoinAddress.FromHash(BitcoinScriptType.PayToPublicKeyHash, hash, BitcoinNetwork.Mainnet));
        Assert.Equal(Fixtures.Taproot, BitcoinAddress.FromWitness(1, program, BitcoinNetwork.Mainnet));
        Assert.Equal(BitcoinNetwork.Testnet,
            BitcoinAddress.Parse(BitcoinAddress.FromHash(BitcoinScriptType.PayToScriptHash, hash, BitcoinNetwork.Testnet).Value).Network);
    }

    [Fact]
    public void Parse_throws_for_an_invalid_address()
    {
        Assert.Throws<FormatException>(() => BitcoinAddress.Parse("inte en adress"));
    }

    [Theory]
    [InlineData("A1A1A1A1A1A1A1A1A1A1A1A1A1A1A1A1A1A1A1A1A1A1A1A1A1A1A1A1A1A1A1A1", true)]
    [InlineData("a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a", false)]
    [InlineData("g1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_txid_is_64_hex_characters_kept_in_lower_case(string? text, bool valid)
    {
        Assert.Equal(valid, Txid.TryParse(text, out var txid));
        if (valid)
        {
            Assert.Equal(text!.ToLowerInvariant(), txid!.Value);
        }
    }

    [Fact]
    public void A_start_point_is_recognised()
    {
        var address = BitcoinStartPoint.Recognise("  " + Fixtures.AddressText("tr") + "\n");
        var transaction = BitcoinStartPoint.Recognise(Fixtures.Confirmed.Value.ToUpperInvariant());
        var unknown = BitcoinStartPoint.Recognise("hej");

        Assert.Equal(BitcoinStartPointKind.Address, address.Kind);
        Assert.Equal(Fixtures.Taproot, address.Address);
        Assert.Equal(BitcoinStartPointKind.Transaction, transaction.Kind);
        Assert.Equal(Fixtures.Confirmed, transaction.Txid);
        Assert.Equal(BitcoinStartPointKind.Unknown, unknown.Kind);
    }

    [Fact]
    public void Script_types_have_their_short_names()
    {
        Assert.Equal("P2WPKH", BitcoinScriptType.PayToWitnessPublicKeyHash.ShortName());
        Assert.Equal("P2TR", BitcoinScriptType.PayToTaproot.ShortName());
        Assert.Equal("OP_RETURN", BitcoinScriptType.OpReturn.ShortName());
    }
}
