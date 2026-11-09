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
/// <param name="LockTime">The lock time: a block height (below 500 000 000) or a Unix time before which it is
/// invalid.</param>
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
    /// <summary>
    /// Gets whether Chainsaw has checked this description against the transaction's own bytes, sealed in the case:
    /// the bytes hash (double SHA-256) to the txid, and the version, inputs, outputs and lock time are the bytes'.
    /// What the bytes do not hold, the spent outputs' amounts and addresses and the block, rests on the source.
    /// </summary>
    public bool BytesVerified { get; init; }

    /// <summary>
    /// Gets how far Chainsaw has checked that the transaction is in its block (<see cref="InBlockCheck"/>); what it
    /// says is exactly what was checked, never more.
    /// </summary>
    public InBlockCheck InBlock { get; init; }

    /// <summary>
    /// Gets what the transaction moves into or out of Zcash's shielded pools, as the chain states it; <see
    /// langword="null"/> on Bitcoin and Litecoin, and on a Zcash transaction that touches no pool. With a flow, the
    /// transparent inputs and outputs do not add up by themselves: the inputs and what comes out of the pools pay the
    /// outputs, what goes into the pools and the fee.
    /// </summary>
    public ShieldedFlow? Shielded { get; init; }

    /// <summary>Gets whether the transaction is in a block.</summary>
    public bool IsConfirmed => Block is not null;

    /// <summary>Gets whether this is a coinbase transaction, which creates new coins.</summary>
    public bool IsCoinbase => Inputs.Count == 1 && Inputs[0].IsCoinbase;

    /// <summary>
    /// Gets the virtual size in vbytes (the weight divided by four, rounded up), which fees are priced in.
    /// </summary>
    public int VirtualSize => (Weight + 3) / 4;

    /// <summary>Gets the sum of the outputs.</summary>
    public Satoshi TotalOut => Outputs.Aggregate(Satoshi.Zero, (sum, output) => sum + output.Amount);
}

/// <summary>How far Chainsaw has checked that a transaction is in its block, said exactly as it was checked.</summary>
public enum InBlockCheck
{
    /// <summary>Not checked.</summary>
    NotChecked,

    /// <summary>
    /// The source's merkle proof leads from the transaction's own bytes to the merkle root in the block's own header,
    /// and the proof's depth fits the block's transactions; but the block itself rests only on that source's answer:
    /// the header's work is only checked against the network's easiest target, which anyone can meet. On Litecoin the
    /// header hashes to the block; its work (scrypt) is not checked at all.
    /// </summary>
    SourceOnly,

    /// <summary>
    /// As <see cref="SourceOnly"/>, and another endpoint in step with the chain gave the same block hash at that
    /// height, so the block is not one source's alone.
    /// </summary>
    ConfirmedElsewhere,
}
