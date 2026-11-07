using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>Synthetic Ethereum addresses and transactions, never real ones.</summary>
internal static class EthSynthetic
{
    public static EthereumAddress Address(string label) =>
        EthereumAddress.Parse("0x" + Hex(label, 40));

    public static EthereumTxHash Hash(string label) => EthereumTxHash.Parse("0x" + Hex(label, 64));

    public static EthereumBlockReference Block(long height) =>
        new(height, FakeEthereumSource.HashOf(height),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(12 * height));

    public static AssetAmount Ether(decimal ether) =>
        new(Asset.Ether, new System.Numerics.BigInteger(ether * 1_000_000_000_000_000_000m));

    public static AssetAmount Usdt(decimal amount) =>
        new(KnownTokens.EthereumUsdt, new System.Numerics.BigInteger(amount * 1_000_000m));

    /// <summary>Makes a transaction: the signer, its block and its movements as (kind, from, to, amount).</summary>
    public static EthereumTransaction Tx(
        string label, long height, EthereumAddress from, decimal feeEther,
        params (EthereumTransferKind Kind, EthereumAddress From, EthereumAddress To, AssetAmount Amount)[] moves)
    {
        var hash = Hash(label);
        var block = Block(height);
        var transfers = moves.Select((move, index) => new EthereumTransfer(
            hash, move.Kind, move.From, move.To, move.Amount, move.Kind == EthereumTransferKind.Native ? 0 : index,
            block)).ToList();
        var value = moves.Where(move => move.Kind == EthereumTransferKind.Native).Select(move => move.Amount)
            .FirstOrDefault(AssetAmount.Zero(Asset.Ether));
        var to = moves.Length > 0 ? moves[0].To : from;
        return new EthereumTransaction(
            hash, block, from, to, value, Ether(feeEther), EthereumOutcome.Succeeded, transfers);
    }

    public static (EthereumTransferKind, EthereumAddress, EthereumAddress, AssetAmount) Eth(
        EthereumAddress from, EthereumAddress to, decimal ether) =>
        (EthereumTransferKind.Native, from, to, Ether(ether));

    public static (EthereumTransferKind, EthereumAddress, EthereumAddress, AssetAmount) Token(
        EthereumAddress from, EthereumAddress to, AssetAmount amount) =>
        (EthereumTransferKind.Token, from, to, amount);

    private static string Hex(string label, int digits)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(label));
        var text = Convert.ToHexString(bytes).ToLower(CultureInfo.InvariantCulture);
        return (text + text)[..digits];
    }
}
