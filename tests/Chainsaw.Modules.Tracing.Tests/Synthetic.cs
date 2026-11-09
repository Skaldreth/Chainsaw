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
        BitcoinAddress.FromHash(
            BitcoinScriptType.PayToPublicKeyHash, Hash(label).AsSpan(0, 20), BitcoinNetwork.Mainnet);

    public static BitcoinAddress Tr(string label) =>
        BitcoinAddress.FromWitness(1, Hash(label), BitcoinNetwork.Mainnet);

    public static Txid Txid(string label) =>
        Chainsaw.Modules.Chains.Contracts.Txid.Parse(Convert.ToHexStringLower(Hash("tx:" + label)));

    /// <summary>A synthetic txid on a UTXO chain; the same label gives the same hex on every chain.</summary>
    /// <param name="label">The label.</param>
    /// <param name="chain">The chain.</param>
    /// <returns>The txid.</returns>
    public static Txid Txid(string label, Chain chain) => Txid(label).On(chain);

    /// <summary>A synthetic P2PKH address on Litecoin (<c>L…</c>).</summary>
    /// <param name="label">The label.</param>
    /// <returns>The address.</returns>
    public static BitcoinAddress LitecoinPkh(string label) =>
        BitcoinAddress.FromHash(
            BitcoinScriptType.PayToPublicKeyHash, Hash(label).AsSpan(0, 20), BitcoinNetwork.Litecoin);

    /// <summary>A synthetic transparent P2PKH address on Zcash (<c>t1…</c>).</summary>
    /// <param name="label">The label.</param>
    /// <returns>The address.</returns>
    public static BitcoinAddress ZcashPkh(string label) =>
        BitcoinAddress.FromHash(
            BitcoinScriptType.PayToPublicKeyHash, Hash(label).AsSpan(0, 20), BitcoinNetwork.Zcash);

    public static BlockReference Block(long height) => new(
        height,
        BlockHash.Parse(Convert.ToHexStringLower(Hash("block:" + height))),
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(10 * height));

    public static TxBuilder Tx(string label, long? height = 900_000) => new(label, height, Chain.Bitcoin);

    /// <summary>Starts a synthetic transaction on a UTXO chain.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="label">The label.</param>
    /// <param name="height">The block height, or null for unconfirmed.</param>
    /// <returns>The builder.</returns>
    public static TxBuilder Tx(Chain chain, string label, long? height = 900_000) => new(label, height, chain);

    private static byte[] Hash(string label) => SHA256.HashData(Encoding.UTF8.GetBytes(label));
}

/// <summary>
/// Builds one synthetic transaction on a UTXO chain; the fee is what the inputs (and what comes out of a shielded pool)
/// leave over.
/// </summary>
internal sealed class TxBuilder(string label, long? height, Chain chain)
{
    private readonly List<BitcoinInput> _inputs = [];
    private readonly List<BitcoinOutput> _outputs = [];
    private ShieldedFlow? _shielded;

    /// <summary>An input from an output of a transaction that is not in the case.</summary>
    public TxBuilder In(BitcoinAddress address, long satoshi, string? funding = null) =>
        AddInput(address, satoshi, Synthetic.Txid(funding ?? $"{label}:funding:{_inputs.Count}", chain), 0);

    /// <summary>Moves money between the transparent part and Zcash's Sapling pool: positive into it.</summary>
    /// <param name="netIntoPool">The net amount into the pool, negative out of it.</param>
    /// <returns>The builder.</returns>
    public TxBuilder Pool(long netIntoPool)
    {
        _shielded = ShieldedFlow.FromNet(netIntoPool, [ShieldedPool.Sapling]);
        return this;
    }

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
        var inputs = _inputs.Aggregate(0L, (sum, input) => sum + input.Amount!.Value.Value)
            + (_shielded?.OutOfPool.Value ?? 0);
        var outputs = _outputs.Aggregate(0L, (sum, output) => sum + output.Amount.Value)
            + (_shielded?.IntoPool.Value ?? 0);
        if (outputs > inputs)
        {
            throw new InvalidOperationException($"{label}: the outputs are more than the inputs.");
        }

        return new BitcoinTransaction(
            Synthetic.Txid(label, chain),
            height is { } h ? Synthetic.Block(h) : null,
            _inputs,
            _outputs,
            new Satoshi(inputs - outputs),
            250,
            1000,
            2,
            0)
        {
            Shielded = _shielded,
        };
    }

    private TxBuilder AddInput(BitcoinAddress address, long satoshi, Txid previous, int index)
    {
        _inputs.Add(new BitcoinInput(
            _inputs.Count, false, previous, index, address, address.Type, new Satoshi(satoshi), 0xFFFFFFFD));
        return this;
    }
}
