namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// The block a confirmed transaction is in, as the source reported it: its height, hash and time. It goes with
/// everything built on a confirmed transaction, so that every step can be checked against the chain with any tool
/// (principle 1).
/// </summary>
/// <param name="Height">The block's height.</param>
/// <param name="Hash">The block's hash.</param>
/// <param name="Time">The time in the block's header, in UTC (set by the miner, accurate to a couple of hours).</param>
public sealed record BlockReference(long Height, BlockHash Hash, DateTimeOffset Time);
