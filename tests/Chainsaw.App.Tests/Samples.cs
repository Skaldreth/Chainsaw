using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>Made-up start points: valid in form, never real addresses or transactions.</summary>
internal static class Samples
{
    /// <summary>A made-up Bitcoin address (P2WPKH over the bytes 1 to 20).</summary>
    public static readonly string Address = BitcoinAddress.FromWitness(
        0, Enumerable.Range(1, 20).Select(b => (byte)b).ToArray(), BitcoinNetwork.Mainnet).Value;

    /// <summary>Another made-up Bitcoin address.</summary>
    public static readonly string OtherAddress = BitcoinAddress.FromHash(
        BitcoinScriptType.PayToPublicKeyHash, Enumerable.Range(40, 20).Select(b => (byte)b).ToArray(),
        BitcoinNetwork.Mainnet).Value;

    /// <summary>A made-up txid.</summary>
    public static readonly string Txid = string.Concat(Enumerable.Repeat("4f2a", 16));

    /// <summary>A made-up Ethereum address, all lower case so that it carries no checksum.</summary>
    public static readonly string Ethereum = "0x" + string.Concat(Enumerable.Repeat("11", 20));

    /// <summary>A made-up Ethereum transaction hash.</summary>
    public static readonly string EthereumTx = "0x" + string.Concat(Enumerable.Repeat("b1", 32));

    /// <summary>A made-up TRON address: Base58Check with the version byte 0x41.</summary>
    public static readonly string Tron = Base58Check.Encode([0x41, .. Enumerable.Range(1, 20).Select(b => (byte)b)]);
}
