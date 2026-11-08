namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// A chain's newest block as the source knew it when it was asked: its height and its hash, read together, so that
/// what was fetched at that height can be checked against the chain with any tool (principle 1). The height alone
/// says how far the chain had come; only the hash says which chain it was.
/// </summary>
/// <param name="Height">The block's height.</param>
/// <param name="Hash">
/// The block's hash as the chain's tools write it: 64 hex characters for Bitcoin, <c>0x</c> and 64 for Ethereum,
/// always in lower case.
/// </param>
/// <param name="Time">The block's time in UTC.</param>
public sealed record ChainTip(long Height, string Hash, DateTimeOffset Time);
