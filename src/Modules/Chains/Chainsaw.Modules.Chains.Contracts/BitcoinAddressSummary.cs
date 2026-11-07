using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// What the source says about an address as a whole (<c>/address/:address</c>): how many transactions it has and how
/// much it has received and spent, in blocks and in the mempool. It always changes, so it is fetched anew each time.
/// </summary>
/// <param name="Address">The address.</param>
/// <param name="Confirmed">What is in blocks.</param>
/// <param name="Mempool">What is unconfirmed.</param>
public sealed record BitcoinAddressSummary(
    BitcoinAddress Address, BitcoinAddressStats Confirmed, BitcoinAddressStats Mempool)
{
    /// <summary>Gets how many transactions the address has, confirmed and unconfirmed.</summary>
    public long TransactionCount => Confirmed.TransactionCount + Mempool.TransactionCount;
}

/// <summary>The counts and sums of an address in one part of the chain.</summary>
/// <param name="TransactionCount">How many transactions involve the address.</param>
/// <param name="FundedOutputs">How many outputs paid to it.</param>
/// <param name="Funded">How much was paid to it.</param>
/// <param name="SpentOutputs">How many of its outputs were spent.</param>
/// <param name="Spent">How much of it was spent.</param>
public sealed record BitcoinAddressStats(
    long TransactionCount, long FundedOutputs, Satoshi Funded, long SpentOutputs, Satoshi Spent);
