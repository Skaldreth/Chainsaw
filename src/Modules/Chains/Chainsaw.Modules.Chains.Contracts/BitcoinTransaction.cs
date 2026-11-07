using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// A Bitcoin transaction as it stands on the chain (principle 2: facts). Inputs spend earlier outputs, outputs lock
/// new amounts, and the difference is the miners' fee: the inputs' sum is always the outputs' sum plus the fee, which
/// Chainsaw checks when it reads a response.
/// </summary>
/// <param name="Txid">The transaction's id.</param>
/// <param name="Block">The block it is in; <see langword="null"/> while it is unconfirmed (in the mempool).</param>
/// <param name="Inputs">The inputs, in order.</param>
/// <param name="Outputs">The outputs, in order.</param>
/// <param name="Fee">The fee; zero for a coinbase.</param>
/// <param name="Size">The size in bytes, witness data included.</param>
/// <param name="Weight">The weight in weight units (four per byte outside the witness, one per byte inside it).</param>
/// <param name="Version">The transaction's version field.</param>
/// <param name="LockTime">The lock time: a block height (below 500 000 000) or a Unix time before which it is invalid.</param>
public sealed record BitcoinTransaction(
    Txid Txid,
    BlockReference? Block,
    IReadOnlyList<BitcoinInput> Inputs,
    IReadOnlyList<BitcoinOutput> Outputs,
    Satoshi Fee,
    int Size,
    int Weight,
    long Version,
    uint LockTime)
{
    /// <summary>Gets whether the transaction is in a block.</summary>
    public bool IsConfirmed => Block is not null;

    /// <summary>Gets whether this is a coinbase transaction, which creates new coins.</summary>
    public bool IsCoinbase => Inputs.Count == 1 && Inputs[0].IsCoinbase;

    /// <summary>Gets the virtual size in vbytes (the weight divided by four, rounded up), which fees are priced in.</summary>
    public int VirtualSize => (Weight + 3) / 4;

    /// <summary>Gets the sum of the outputs.</summary>
    public Satoshi TotalOut => Outputs.Aggregate(Satoshi.Zero, (sum, output) => sum + output.Amount);
}
