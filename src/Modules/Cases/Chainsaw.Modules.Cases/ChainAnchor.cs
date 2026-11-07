namespace Chainsaw.Modules.Cases;

/// <summary>How far a chain had come when this computer last saved the case.</summary>
/// <param name="Chain">The chain's name.</param>
/// <param name="Sequence">The sequence number of the chain's last record then.</param>
/// <param name="Hash">That record's hash.</param>
internal sealed record ChainAnchor(string Chain, long Sequence, byte[] Hash);
