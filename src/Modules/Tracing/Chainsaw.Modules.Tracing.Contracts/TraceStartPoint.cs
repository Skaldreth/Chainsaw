using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// Where the trace starts: an address (for example the one the victim paid to) or a transaction (for example the
/// victim's payment), on a UTXO chain (Bitcoin, Litecoin or Zcash's transparent part) or on an account chain
/// (Ethereum, TRON or BSC); addresses and transactions carry their chain. Exactly one of <see cref="Address"/>, <see cref="Txid"/>,
/// <see cref="EthereumAddress"/> and <see cref="EthereumTransaction"/> is set.
/// </summary>
public sealed record TraceStartPoint
{
    /// <summary>Creates a start point; use one of the factories.</summary>
    /// <param name="address">The address on a UTXO chain, or <see langword="null"/>.</param>
    /// <param name="txid">The transaction on a UTXO chain, or <see langword="null"/>.</param>
    /// <param name="ethereumAddress">The Ethereum address, or <see langword="null"/>.</param>
    /// <param name="ethereumTransaction">The Ethereum transaction, or <see langword="null"/>.</param>
    private TraceStartPoint(
        BitcoinAddress? address, Txid? txid, EthereumAddress? ethereumAddress, EthereumTxHash? ethereumTransaction) =>
        (Address, Txid, EthereumAddress, EthereumTransaction) = (address, txid, ethereumAddress, ethereumTransaction);

    /// <summary>Gets the address on a UTXO chain (Bitcoin, Litecoin, Zcash), when the start point is one.</summary>
    public BitcoinAddress? Address { get; }

    /// <summary>Gets the transaction on a UTXO chain, when the start point is one.</summary>
    public Txid? Txid { get; }

    /// <summary>Gets the Ethereum address, when the start point is one.</summary>
    public EthereumAddress? EthereumAddress { get; }

    /// <summary>Gets the Ethereum transaction, when the start point is one.</summary>
    public EthereumTxHash? EthereumTransaction { get; }

    /// <summary>Gets the chain the start point is on.</summary>
    public Chain Chain => Address?.Chain ?? Txid?.Chain ?? EthereumAddress?.Chain ?? EthereumTransaction!.Chain;

    /// <summary>Gets the address with its chain, when the start point is an address on any chain.</summary>
    public ChainAddress? Account => Address?.ToChainAddress() ?? EthereumAddress?.ToChainAddress();

    /// <summary>Gives a start point at an address; the actor it belongs to is the trace's first column.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The start point.</returns>
    public static TraceStartPoint ForAddress(BitcoinAddress address) =>
        new(address ?? throw new ArgumentNullException(nameof(address)), null, null, null);

    /// <summary>
    /// Gives a start point at a transaction; the actors that pay into it are the trace's first column and those it
    /// pays to the next.
    /// </summary>
    /// <param name="txid">The transaction.</param>
    /// <returns>The start point.</returns>
    public static TraceStartPoint ForTransaction(Txid txid) =>
        new(null, txid ?? throw new ArgumentNullException(nameof(txid)), null, null);

    /// <summary>Gives a start point at an Ethereum address; its account is the trace's first column.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The start point.</returns>
    public static TraceStartPoint ForEthereumAddress(EthereumAddress address) =>
        new(null, null, address ?? throw new ArgumentNullException(nameof(address)), null);

    /// <summary>
    /// Gives a start point at an Ethereum transaction; the account that signed it is the trace's first column and
    /// the accounts its movements pay the next.
    /// </summary>
    /// <param name="hash">The transaction's hash.</param>
    /// <returns>The start point.</returns>
    public static TraceStartPoint ForEthereumTransaction(EthereumTxHash hash) =>
        new(null, null, null, hash ?? throw new ArgumentNullException(nameof(hash)));

    /// <summary>Writes the address or the transaction as the chain writes it.</summary>
    /// <returns>The value; an Ethereum address with its EIP-55 checksum.</returns>
    public override string ToString() =>
        Address?.Value ?? Txid?.Value ?? EthereumAddress?.Checksummed ?? EthereumTransaction!.Value;
}
