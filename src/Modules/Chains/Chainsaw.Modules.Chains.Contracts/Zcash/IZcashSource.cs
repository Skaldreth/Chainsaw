namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// Zcash's facts for a case, its transparent part through the UTXO model's contract (<see cref="IBitcoinSource"/>):
/// transparent addresses (<see cref="BitcoinNetwork.Zcash"/>, <c>t1…</c> and <c>t3…</c>) and coins as Bitcoin's, in
/// zatoshi, sealed under Zcash's own chain name. A transaction that moves money into or out of the shielded pools
/// carries what the chain states about it (<see cref="BitcoinTransaction.Shielded"/>); what happens inside the pools
/// cannot be read. Zcash's newer transaction ids are not a hash of the bytes in Bitcoin's way (ZIP 244), so a
/// transaction is never marked as checked against its own bytes. The interface only names the chain, so that the
/// container gives Zcash's source where Zcash is asked.
/// </summary>
public interface IZcashSource : IBitcoinSource
{
}
