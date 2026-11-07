using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// Synthetic addresses and transactions for the facit. Every address and hash is derived from a label with SHA-256,
/// so none is a real one that anyone has used.
/// </summary>
internal static class Synthetic
{
    public static BitcoinAddress Wpkh(string label) =>
        BitcoinAddress.FromWitness(0, Hash(label).AsSpan(0, 20), BitcoinNetwork.Mainnet);

    public static BitcoinAddress Pkh(string label) =>
        BitcoinAddress.FromHash(BitcoinScriptType.PayToPublicKeyHash, Hash(label).AsSpan(0, 20), BitcoinNetwork.Mainnet);

    public static BitcoinAddress Tr(string label) =>
        BitcoinAddress.FromWitness(1, Hash(label), BitcoinNetwork.Mainnet);

    public static Txid Txid(string label) =>
        Chainsaw.Modules.Chains.Contracts.Txid.Parse(Convert.ToHexStringLower(Hash("tx:" + label)));

    public static BlockReference Block(long height) => new(
        height,
        BlockHash.Parse(Convert.ToHexStringLower(Hash("block:" + height))),
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(10 * height));

    public static TxBuilder Tx(string label, long? height = 900_000) => new(label, height);

    private static byte[] Hash(string label) => SHA256.HashData(Encoding.UTF8.GetBytes(label));
}

/// <summary>Builds one synthetic transaction; the fee is what the inputs leave over.</summary>
internal sealed class TxBuilder(string label, long? height)
{
    private readonly List<BitcoinInput> _inputs = [];
    private readonly List<BitcoinOutput> _outputs = [];

    /// <summary>An input from an output of a transaction that is not in the case.</summary>
    public TxBuilder In(BitcoinAddress address, long satoshi, string? funding = null) =>
        AddInput(address, satoshi, Synthetic.Txid(funding ?? $"{label}:funding:{_inputs.Count}"), 0);

    /// <summary>An input that spends an output of another synthetic transaction.</summary>
    public TxBuilder Spend(BitcoinTransaction previous, int index)
    {
        var output = previous.Outputs[index];
        return AddInput(output.Address!, output.Amount.Value, previous.Txid, index);
    }

    public TxBuilder Out(BitcoinAddress address, long satoshi)
    {
        _outputs.Add(new BitcoinOutput(_outputs.Count, address.Type, address, new Satoshi(satoshi), "", null));
        return this;
    }

    public TxBuilder OpReturn()
    {
        _outputs.Add(new BitcoinOutput(_outputs.Count, BitcoinScriptType.OpReturn, null, Satoshi.Zero, "6a", null));
        return this;
    }

    public BitcoinTransaction Build()
    {
        var inputs = _inputs.Aggregate(0L, (sum, input) => sum + input.Amount!.Value.Value);
        var outputs = _outputs.Aggregate(0L, (sum, output) => sum + output.Amount.Value);
        if (outputs > inputs)
        {
            throw new InvalidOperationException($"{label}: the outputs are more than the inputs.");
        }

        return new BitcoinTransaction(
            Synthetic.Txid(label),
            height is { } h ? Synthetic.Block(h) : null,
            _inputs,
            _outputs,
            new Satoshi(inputs - outputs),
            250,
            1000,
            2,
            0);
    }

    private TxBuilder AddInput(BitcoinAddress address, long satoshi, Txid previous, int index)
    {
        _inputs.Add(new BitcoinInput(
            _inputs.Count, false, previous, index, address, address.Type, new Satoshi(satoshi), 0xFFFFFFFD));
        return this;
    }
}
