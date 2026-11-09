namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// Litecoin's facts for a case, through the UTXO model's contract (<see cref="IBitcoinSource"/>): Litecoin keeps
/// Bitcoin's coins, scripts and API (Esplora, at litecoinspace.org), with its own addresses
/// (<see cref="BitcoinNetwork.Litecoin"/>) and its responses sealed under its own chain name. Its shielded part
/// (MWEB) shows on the chain only as peg-ins (<see cref="BitcoinScriptType.MwebPegIn"/>) and as the block's
/// integration transaction (HogEx); what happens inside cannot be read. The interface only names the chain, so that
/// the container gives Litecoin's source where Litecoin is asked.
/// </summary>
public interface ILitecoinSource : IBitcoinSource
{
}
