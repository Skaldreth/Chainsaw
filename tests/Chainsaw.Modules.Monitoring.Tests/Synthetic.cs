using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring.Tests;

/// <summary>
/// Synthetic addresses and transactions. Every address and hash is derived from a label with SHA-256, so none is a
/// real one that anyone has used.
/// </summary>
internal static class Synthetic
{
    public static readonly Asset Usdt = Asset.Token(
        Chain.Ethereum, "0xdac17f958d2ee523a2206206994597c13d831ec7", "USDT", 6);

    public static BitcoinAddress Wpkh(string label) =>
        BitcoinAddress.FromWitness(0, Hash(label).AsSpan(0, 20), BitcoinNetwork.Mainnet);

    public static ChainAddress Btc(string label) => new(Chain.Bitcoin, Wpkh(label).Value);

    public static EthereumAddress Eth(string label) =>
        EthereumAddress.Parse("0x" + Convert.ToHexStringLower(Hash(label).AsSpan(0, 20)));

    public static ChainAddress On(EthereumAddress address) => new(Chain.Ethereum, address.Value);

    public static Txid Txid(string label) =>
        Chainsaw.Modules.Chains.Contracts.Txid.Parse(Convert.ToHexStringLower(Hash("tx:" + label)));

    public static EthereumTxHash EthTx(string label) =>
        EthereumTxHash.Parse("0x" + Convert.ToHexStringLower(Hash("tx:" + label)));

    public static BlockReference Block(long height) => new(
        height,
        BlockHash.Parse(Convert.ToHexStringLower(Hash("block:" + height))),
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(10 * height % 100_000));

    public static string EthBlockHash(long height) => "0x" + Convert.ToHexStringLower(Hash("eth-block:" + height));

    public static EthereumBlockReference EthBlock(long height, bool withHash = true) =>
        new(height, withHash ? EthBlockHash(height) : null, new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero));

    public static EthereumTransfer Transfer(
        string tx, EthereumTransferKind kind, EthereumAddress from, EthereumAddress to, AssetAmount amount,
        long height, bool withHash = true, long position = 0) =>
        new(EthTx(tx), kind, from, to, amount, position, EthBlock(height, withHash));

    public static AssetAmount Ether(decimal ether) =>
        new(Asset.Ether, new BigInteger(ether * 1_000_000) * BigInteger.Pow(10, 12));

    public static AssetAmount Tether(long whole) => new(Usdt, new BigInteger(whole) * 1_000_000);

    public static TxBuilder Tx(string label, long? height = 900_010) => new(label, height);

    private static byte[] Hash(string label) => SHA256.HashData(Encoding.UTF8.GetBytes(label));
}

/// <summary>Builds one synthetic Bitcoin transaction; the fee is what the inputs leave over.</summary>
internal sealed class TxBuilder(string label, long? height)
{
    private readonly List<BitcoinInput> _inputs = [];
    private readonly List<BitcoinOutput> _outputs = [];

    public TxBuilder In(BitcoinAddress address, long satoshi)
    {
        _inputs.Add(new BitcoinInput(
            _inputs.Count, false, Synthetic.Txid($"{label}:funding:{_inputs.Count}"), 0, address, address.Type,
            new Satoshi(satoshi), 0xFFFFFFFD));
        return this;
    }

    public TxBuilder Out(BitcoinAddress address, long satoshi)
    {
        _outputs.Add(new BitcoinOutput(_outputs.Count, address.Type, address, new Satoshi(satoshi), "", null));
        return this;
    }

    public BitcoinTransaction Build()
    {
        var inputs = _inputs.Sum(input => input.Amount!.Value.Value);
        var outputs = _outputs.Sum(output => output.Amount.Value);
        return new BitcoinTransaction(
            Synthetic.Txid(label), height is { } h ? Synthetic.Block(h) : null, _inputs, _outputs,
            new Satoshi(inputs - outputs), 250, 1000, 2, 0);
    }
}
