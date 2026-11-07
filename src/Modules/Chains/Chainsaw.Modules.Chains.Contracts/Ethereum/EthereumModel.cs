using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// The block an Ethereum transaction is in, as the source reported it: its height, hash and time. It goes with
/// everything built on a confirmed transaction, so that every step can be checked against the chain (principle 1).
/// </summary>
/// <param name="Height">The block's number.</param>
/// <param name="Hash">
/// The block's hash, <c>0x</c> and 64 hex digits in lower case, or <see langword="null"/> when the answer it came in
/// did not give it (a list of internal transactions); the block's own lookup gives it.
/// </param>
/// <param name="Time">The block's time in UTC (set by the proposer, to the second).</param>
public sealed record EthereumBlockReference(long Height, string? Hash, DateTimeOffset Time);

/// <summary>How money moves on Ethereum, which decides where the chain records it.</summary>
public enum EthereumTransferKind
{
    /// <summary>
    /// Ether sent by the transaction itself (its <c>value</c>), from the account that signed it to the address it was
    /// sent to.
    /// </summary>
    Native,

    /// <summary>
    /// Ether sent by a contract while the transaction ran (an <em>internal transaction</em>, a call with value). It
    /// is in no transaction's fields and no event; only a trace of the execution shows it.
    /// </summary>
    Internal,

    /// <summary>
    /// A token moved by its contract, recorded as the ERC-20 event <c>Transfer(from, to, value)</c> in the
    /// transaction's logs. The amount is in the token's smallest unit, with the contract's decimals.
    /// </summary>
    Token,
}

/// <summary>
/// One movement of value on Ethereum: ether or a token, from one address to another, in a transaction, as the chain
/// recorded it (principle 2). For ether that is the chain's own ledger. For a token it is the <c>Transfer</c> event the
/// token's contract wrote: the contract's own claim about its ledger, which for a contract Chainsaw does not know (see
/// <see cref="KnownTokens"/>) need not be honest. Transfers of zero are never given, since anyone can make a token
/// contract write one from any address (address poisoning).
/// </summary>
/// <param name="Transaction">The transaction it happened in.</param>
/// <param name="Kind">Whether it is ether sent by the transaction, ether sent by a contract, or a token.</param>
/// <param name="From">The address the value left.</param>
/// <param name="To">The address that received it.</param>
/// <param name="Amount">
/// The amount, in ether (wei) or in the token named by its contract, with the decimals the chain or the source gave.
/// </param>
/// <param name="Position">
/// Where in the transaction it happened, so that two movements in one transaction stay apart: 0 for ether sent by the
/// transaction, the trace's index for an internal one and the log's index for a token. When the source gives neither
/// index, the entry's order among the transaction's entries in the answer.
/// </param>
/// <param name="Block">The block, or <see langword="null"/> while the transaction is unconfirmed.</param>
public sealed record EthereumTransfer(
    EthereumTxHash Transaction,
    EthereumTransferKind Kind,
    EthereumAddress From,
    EthereumAddress To,
    AssetAmount Amount,
    long Position,
    EthereumBlockReference? Block);

/// <summary>How a transaction ended.</summary>
public enum EthereumOutcome
{
    /// <summary>It ran in a block and succeeded.</summary>
    Succeeded,

    /// <summary>It ran in a block and failed (reverted): it moved nothing, but its sender paid the fee.</summary>
    Failed,

    /// <summary>It is not in a block yet, so whether it succeeds and what it moves is not known.</summary>
    Pending,
}

/// <summary>
/// An Ethereum transaction as the chain recorded it: who signed it, where it was sent, how it ended, what it cost and
/// every movement of value in it. A failed transaction moves nothing, but its sender still pays the fee.
/// </summary>
/// <param name="Hash">The transaction's hash.</param>
/// <param name="Block">The block, or <see langword="null"/> while it is unconfirmed.</param>
/// <param name="From">The account that signed it and pays the fee.</param>
/// <param name="To">The address it was sent to, or <see langword="null"/> when it created a contract.</param>
/// <param name="Value">The ether it sent with it, in wei (also when it failed; then nothing moved).</param>
/// <param name="Fee">
/// What the sender paid for its gas, in wei: the gas used times the price per gas (zero while pending). A blob
/// transaction's blob gas is not in it.
/// </param>
/// <param name="Outcome">How it ended; a failed one moves no value.</param>
/// <param name="Transfers">
/// Every movement of value in it, in the order the chain applied them: the ether it sent, then the internal
/// transactions, then the tokens by their logs. Empty when it failed; only the ether it would send while it is pending.
/// </param>
public sealed record EthereumTransaction(
    EthereumTxHash Hash,
    EthereumBlockReference? Block,
    EthereumAddress From,
    EthereumAddress? To,
    AssetAmount Value,
    AssetAmount Fee,
    EthereumOutcome Outcome,
    IReadOnlyList<EthereumTransfer> Transfers)
{
    /// <summary>Gets whether the transaction is in a block.</summary>
    public bool IsConfirmed => Block is not null;

    /// <summary>Gets whether it ran in a block and succeeded.</summary>
    public bool Succeeded => Outcome == EthereumOutcome.Succeeded;

    /// <summary>
    /// Gets whether every part of it could be read: its internal transactions, its block's hash and every token's
    /// decimals. When not, the lookup's notes say what is missing, and a movement may be left out.
    /// </summary>
    public bool IsComplete { get; init; } = true;
}

/// <summary>Which of an address's histories is asked for; Ethereum's sources keep the three apart.</summary>
public enum EthereumHistory
{
    /// <summary>The transactions the address signed or was sent (<c>txlist</c>), with their ether and fees.</summary>
    Transactions,

    /// <summary>The internal transactions to or from the address (<c>txlistinternal</c>).</summary>
    Internal,

    /// <summary>The ERC-20 transfers to or from the address (<c>tokentx</c>).</summary>
    Tokens,
}

/// <summary>
/// The fee one transaction cost the account that signed it, as an address's history gives it: a fact from the chain
/// even when the transaction failed.
/// </summary>
/// <param name="Transaction">The transaction.</param>
/// <param name="Payer">The account that signed it.</param>
/// <param name="Amount">The fee, in wei.</param>
/// <param name="Succeeded">Whether the transaction succeeded.</param>
/// <param name="Block">The block, or <see langword="null"/> while it is unconfirmed.</param>
public sealed record EthereumFee(
    EthereumTxHash Transaction, EthereumAddress Payer, AssetAmount Amount, bool Succeeded,
    EthereumBlockReference? Block);

/// <summary>
/// One page of an address's history of one kind, newest first. A page goes back from a block: the first page from the
/// newest, the next from the oldest block on the page before (the source pages by block, so that a page already read
/// does not shift when new transactions arrive). The next page starts <em>at</em> that block, so the entries in it
/// come again: a reader drops them by transaction, kind and position.
/// </summary>
/// <param name="Address">The address.</param>
/// <param name="History">Which history.</param>
/// <param name="AtOrBelow">The block the page goes back from, or <see langword="null"/> for the newest.</param>
/// <param name="Transfers">The movements of value on the page that touch the address.</param>
/// <param name="Fees">The fees on the page (only in <see cref="EthereumHistory.Transactions"/>).</param>
/// <param name="NextAtOrBelow">
/// Where the next page starts, or <see langword="null"/> when the page was not full (the history ends here) or every
/// entry is in one block (the source cannot page further back; see <see cref="IsCutShort"/>).
/// </param>
/// <param name="IsCutShort">
/// Whether a full page lay in one block, so that the rest of that block and everything older cannot be read by
/// paging; the history is then incomplete, and says so.
/// </param>
/// <param name="IsIncomplete">
/// Whether the source said it has not read all of the history yet (Blockscout's internal transactions, status 2); such
/// a page is fetched again.
/// </param>
public sealed record EthereumAddressPage(
    EthereumAddress Address,
    EthereumHistory History,
    long? AtOrBelow,
    IReadOnlyList<EthereumTransfer> Transfers,
    IReadOnlyList<EthereumFee> Fees,
    long? NextAtOrBelow,
    bool IsCutShort,
    bool IsIncomplete)
{
    /// <summary>
    /// Gets how many entries Chainsaw does not count: a token whose decimals Chainsaw cannot count in (anyone can
    /// make a contract say 255), an NFT and a token transfer of zero (address poisoning). They do not keep the page from
    /// being final.
    /// </summary>
    public int Skipped { get; init; }

    /// <summary>
    /// Gets how many token entries came without decimals, because the source has not read the token yet. They are not
    /// counted now, and keep the page from being final, so that it is asked for again.
    /// </summary>
    public int Unread { get; init; }
}

/// <summary>The height of Ethereum's newest block.</summary>
/// <param name="Height">The block's number.</param>
public sealed record EthereumTip(long Height);
