using System.Text.Json;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// Litecoin: its addresses (<c>L…</c>, <c>M…</c>, the older <c>3…</c> and <c>ltc1…</c>) never taken for Bitcoin's,
/// and its source against synthetic litecoinspace.org responses (<c>Litecoin/generate.py</c>), with a peg-in to MWEB
/// read from the script and the responses sealed under Litecoin's own chain name.
/// </summary>
public sealed class LitecoinTests
{
    /// <summary>The synthetic addresses, by kind.</summary>
    private static readonly IReadOnlyDictionary<string, string> Addresses =
        JsonSerializer.Deserialize<Dictionary<string, string>>(Fixtures.Bytes("../Litecoin/addresses.json"))!;

    private static readonly Txid PegIn = Txid.Parse(Fixtures.Repeat("d1", 32));

    private static readonly Txid HogEx = Txid.Parse(Fixtures.Repeat("d2", 32));

    [Theory]
    [InlineData("legacy", BitcoinScriptType.PayToPublicKeyHash)]
    [InlineData("wpkh", BitcoinScriptType.PayToWitnessPublicKeyHash)]
    [InlineData("p2sh", BitcoinScriptType.PayToScriptHash)]
    [InlineData("pegin", BitcoinScriptType.MwebPegIn)]
    [InlineData("hog", BitcoinScriptType.MwebHogAddress)]
    public void ALitecoinAddressIsOnLitecoinAndNeverOnBitcoin(string kind, BitcoinScriptType type)
    {
        Assert.True(BitcoinAddress.TryParse(Chain.Litecoin, Addresses[kind], out var address));
        Assert.Equal(Chain.Litecoin, address.Chain);
        Assert.Equal(BitcoinNetwork.Litecoin, address.Network);
        Assert.Equal(type, address.Type);
        Assert.Equal(new ChainAddress(Chain.Litecoin, Addresses[kind]), address.ToChainAddress());

        Assert.False(BitcoinAddress.TryParse(Addresses[kind], out _));
        Assert.False(BitcoinAddress.TryParse(Chain.Bitcoin, Addresses[kind], out _));
    }

    [Fact]
    public void TheOlderP2shFormIsReadAsItsLitecoinForm()
    {
        Assert.True(BitcoinAddress.TryParse(Chain.Litecoin, Addresses["p2sh-old"], out var old));

        Assert.Equal(Addresses["p2sh"], old.Value);

        // Without its chain the same text is Bitcoin's.
        Assert.Equal(Chain.Bitcoin, BitcoinAddress.Parse(Addresses["p2sh-old"]).Chain);
    }

    [Fact]
    public void AWitnessProgramIsWrittenWithLitecoinsPrefixAndMwebsChecksum()
    {
        var pegIn = BitcoinAddress.FromWitness(9, Enumerable.Repeat((byte)0x66, 32).ToArray(), BitcoinNetwork.Litecoin);
        var wpkh = BitcoinAddress.FromWitness(0, Enumerable.Repeat((byte)0x33, 20).ToArray(), BitcoinNetwork.Litecoin);

        Assert.Equal(Addresses["pegin"], pegIn.Value);
        Assert.Equal(Addresses["wpkh"], wpkh.Value);

        // On Bitcoin a version 9 program is only an unknown witness, written with Bech32m.
        var bitcoin = BitcoinAddress.FromWitness(
            9, Enumerable.Repeat((byte)0x66, 32).ToArray(), BitcoinNetwork.Mainnet);
        Assert.Equal(BitcoinScriptType.WitnessUnknown, bitcoin.Type);
        Assert.Equal(Chain.Bitcoin, bitcoin.Chain);
    }

    [Fact]
    public async Task APegInIsReadFromItsScriptAndSealedUnderLitecoin()
    {
        using var harness = new SourceHarness();
        Answer(harness, $"tx/{PegIn}", "tx-pegin.json");
        Answer(harness, $"block/{Fixtures.Repeat("c0", 32)}", "block.json");
        Answer(harness, "blocks/tip/height", "tip-height.txt");

        var lookup = await harness.Litecoin.GetTransactionAsync(harness.Case, PegIn);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        var tx = lookup.Value!;
        Assert.Equal(BitcoinScriptType.MwebPegIn, tx.Outputs[0].ScriptType);
        Assert.Equal(Addresses["pegin"], tx.Outputs[0].Address!.Value);
        Assert.Equal(Chain.Litecoin, tx.Inputs[0].Address!.Chain);
        Assert.Equal(Addresses["p2sh"], tx.Outputs[2].Address!.Value);
        Assert.Equal(3_000_000, tx.Block!.Height);
        Assert.Equal(Fixtures.Repeat("c0", 32), tx.Block.Hash.Value);
        Assert.All(Chains(harness), chain => Assert.Equal("litecoin-mainnet", chain));
        Assert.DoesNotContain(harness.Http.Sent, request => request.Uri.Host != "litecoin.test");
        Assert.True(harness.Verify().IsIntact);
    }

    [Fact]
    public async Task TheBlocksIntegrationTransactionIsReadWithItsHogAddress()
    {
        using var harness = new SourceHarness();
        Answer(harness, $"tx/{HogEx}", "tx-hogex.json");
        Answer(harness, $"block/{Fixtures.Repeat("c0", 32)}", "block.json");
        Answer(harness, "blocks/tip/height", "tip-height.txt");

        var tx = (await harness.Litecoin.GetTransactionAsync(harness.Case, HogEx)).Value!;

        Assert.Equal(BitcoinScriptType.MwebHogAddress, tx.Inputs[0].ScriptType);
        Assert.Equal(BitcoinScriptType.MwebPegIn, tx.Inputs[1].ScriptType);
        Assert.Equal(BitcoinScriptType.MwebHogAddress, tx.Outputs[0].ScriptType);
        Assert.Equal(BitcoinScriptType.PayToWitnessPublicKeyHash, tx.Outputs[1].ScriptType);
    }

    [Fact]
    public async Task LitecoinsSourceRefusesABitcoinAddress()
    {
        using var harness = new SourceHarness();
        var bitcoin = BitcoinAddress.Parse(Fixtures.AddressText("wpkh"));

        var lookup = await harness.Litecoin.GetAddressTransactionsAsync(harness.Case, bitcoin);

        Assert.Null(lookup.Value);
        Assert.Contains("annan kedja", lookup.Problem, StringComparison.Ordinal);
        Assert.Empty(harness.Http.Sent);
    }

    [Fact]
    public void LitecoinHasItsOwnEsploraWithLitecoinspaceByDefault()
    {
        var defaults = new EsploraApi(BitcoinSourceOptions.ForLitecoin());

        Assert.Equal(Chain.Litecoin, defaults.Info.Chain);
        Assert.Equal("esplora", defaults.Info.Code);
        Assert.Equal("litecoin-mainnet", defaults.ChainName);
        var endpoint = Assert.Single(defaults.Defaults);
        Assert.Equal(BitcoinSourceOptions.LitecoinSpace, endpoint.Address);
        Assert.Equal(Chain.Litecoin, endpoint.Chain);
    }

    [Fact]
    public void A_transaction_with_mweb_data_is_said_to_be_unreadable_rather_than_misread()
    {
        // Version 2, the marker 00 and the flag 08 (MWEB data) as in a block's HogEx, then anything.
        byte[] bytes = [2, 0, 0, 0, 0, 0x08, 1, 0, 0, 0, 0];

        var problem = Assert.Throws<EsploraFormatException>(() => RawTransactionReader.Read(bytes));

        Assert.Contains("MWEB", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>Answers a path under the fake Litecoin Esplora from a fixture in <c>Litecoin/</c>.</summary>
    private static void Answer(SourceHarness harness, string path, string fixture) =>
        harness.Http.Answer(SourceHarness.LitecoinBase + path, "../Litecoin/" + fixture);

    /// <summary>Reads the sealed chain name of every raw response.</summary>
    private static List<string> Chains(SourceHarness harness) => harness.Case.Read(reader =>
    {
        using var command = reader.CreateCommand("SELECT chain FROM chains_raw_responses ORDER BY sequence");
        using var row = command.ExecuteReader();
        var chains = new List<string>();
        while (row.Read())
        {
            chains.Add(row.GetString(0));
        }

        return chains;
    });
}
