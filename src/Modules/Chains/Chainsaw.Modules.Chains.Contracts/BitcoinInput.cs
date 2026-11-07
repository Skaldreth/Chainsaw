using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// One input of a transaction: the earlier output it spends. In Bitcoin's UTXO model an input always spends a whole
/// output; what is not paid on goes back as change in an output of the same transaction.
/// </summary>
/// <param name="Index">The input's place in the transaction, from 0.</param>
/// <param name="IsCoinbase">Whether this is the coinbase input that creates new coins; it spends nothing.</param>
/// <param name="PreviousTxid">The transaction whose output is spent; <see langword="null"/> for a coinbase.</param>
/// <param name="PreviousIndex">The index of the spent output; <see langword="null"/> for a coinbase.</param>
/// <param name="Address">The address of the spent output, when it has one.</param>
/// <param name="ScriptType">The script type of the spent output; <see langword="null"/> for a coinbase.</param>
/// <param name="Amount">The amount of the spent output; <see langword="null"/> for a coinbase.</param>
/// <param name="Sequence">The input's sequence number (used for replace-by-fee and relative time locks).</param>
public sealed record BitcoinInput(
    int Index,
    bool IsCoinbase,
    Txid? PreviousTxid,
    int? PreviousIndex,
    BitcoinAddress? Address,
    BitcoinScriptType? ScriptType,
    Satoshi? Amount,
    uint Sequence);
