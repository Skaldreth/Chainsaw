using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// Bitcoin's facts for a case: an address's transactions, a transaction, a block and the chain's height. One contract
/// for the kind of chain, not per source (principle 8): the first source is an Esplora API, and a node of one's own
/// later is only another address behind it.
/// </summary>
/// <remarks>
/// <para>
/// Every response is sealed in the case with its source, time, who fetched it and its SHA-256 before it is used
/// (principle 1). Requests go only through the connected exit (principle 4) and carry nothing but what the lookup
/// needs; what cannot have changed is never fetched again; and without an exit the answer comes from the case, saying
/// honestly what is missing (principle 9).
/// </para>
/// <para>
/// The methods do the case's work off the calling thread and never throw for a failed lookup; the answer's
/// <see cref="BitcoinLookup{T}.Status"/> says what happened. They throw only for invalid arguments, cancellation, or
/// a <see cref="CaseException"/>: <see cref="CaseError.Closed"/> for a closed case, and
/// <see cref="CaseError.Corrupt"/> when a sealed response no longer matches its SHA-256 (the case has been changed
/// outside Chainsaw; <c>/verify</c> says where).
/// </para>
/// </remarks>
public interface IBitcoinSource
{
    /// <summary>Gets the network the source answers for.</summary>
    BitcoinNetwork Network { get; }

    /// <summary>Gets one page of an address's transactions, newest first.</summary>
    /// <param name="case">The open case the responses are sealed in.</param>
    /// <param name="address">The address; it must be on <see cref="Network"/>.</param>
    /// <param name="after">
    /// <see langword="null"/> for the first page, or <see cref="BitcoinAddressPage.NextAfter"/> of the previous page.
    /// </param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The page.</returns>
    Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
        ICase @case, BitcoinAddress address, Txid? after = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one page of an address's transactions, newest first, with a choice of how the source may be asked: the
    /// monitoring asks with <see cref="LookupMode.InStep"/>, so that only an endpoint in step with the chain answers.
    /// A source that has no such choice answers <see cref="LookupMode.AsNeeded"/> as the overload without it does.
    /// </summary>
    /// <param name="case">The open case the responses are sealed in.</param>
    /// <param name="address">The address; it must be on <see cref="Network"/>.</param>
    /// <param name="after">
    /// <see langword="null"/> for the first page, or <see cref="BitcoinAddressPage.NextAfter"/> of the previous page.
    /// </param>
    /// <param name="mode">How the source may be asked.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The page.</returns>
    /// <exception cref="NotSupportedException">The source cannot ask in the mode.</exception>
    Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
        ICase @case, BitcoinAddress address, Txid? after, LookupMode mode, CancellationToken cancellationToken = default)
        => mode == LookupMode.AsNeeded
            ? GetAddressTransactionsAsync(@case, address, after, cancellationToken)
            : throw new NotSupportedException("The source cannot ask in that mode.");

    /// <summary>
    /// Gets a transaction. Its outputs say which input spends them when the case has the answer from
    /// <see cref="GetOutputSpendsAsync"/>. When the case has the transaction's own bytes (see
    /// <see cref="VerifyTransactionAsync"/>), the answer is checked against them without asking: it is marked
    /// <see cref="BitcoinTransaction.BytesVerified"/>, or refused (<see cref="LookupStatus.Failed"/>) when it
    /// contradicts them. A transaction that has left the block the case saw it in says so in the problem.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="txid">The transaction's id.</param>
    /// <param name="mode">Whether the source may be asked; <see cref="LookupMode.CaseOnly"/> sends nothing.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The transaction.</returns>
    Task<BitcoinLookup<BitcoinTransaction>> GetTransactionAsync(
        ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a transaction and checks it against its own bytes (<c>/tx/:txid/hex</c>, fetched once and sealed): the
    /// bytes must hash (double SHA-256) to the txid, and the description must be the bytes' in every part the txid
    /// commits to. Then <see cref="BitcoinTransaction.BytesVerified"/> is set, and every later lookup of the
    /// transaction is checked against the same bytes without asking again.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="txid">The transaction's id.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>
    /// The transaction as <see cref="GetTransactionAsync"/> gives it, checked when the bytes could be had; when they
    /// could not, unchecked with the reason in the problem. A description that contradicts the bytes is never given:
    /// the answer is <see cref="LookupStatus.Failed"/>.
    /// </returns>
    Task<BitcoinLookup<BitcoinTransaction>> VerifyTransactionAsync(
        ICase @case, Txid txid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets what the source says about an address as a whole: its number of transactions and what it has received
    /// and spent. It always changes, so it is fetched anew (and sealed) every time; offline the case answers.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address; it must be on <see cref="Network"/>.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The summary.</returns>
    Task<BitcoinLookup<BitcoinAddressSummary>> GetAddressSummaryAsync(
        ICase @case, BitcoinAddress address, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets which transactions spend a transaction's outputs, which is how the money is followed forward.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="txid">The transaction's id.</param>
    /// <param name="mode">Whether the source may be asked; <see cref="LookupMode.CaseOnly"/> sends nothing.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>One entry per output, in order.</returns>
    Task<BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>> GetOutputSpendsAsync(
        ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a block's header. A block is checked against its own 80-byte header (<c>/block/:hash/header</c>, fetched
    /// once and sealed) when the case has it or it can be fetched; see <see cref="BitcoinBlock.HeaderVerified"/>. A
    /// description that contradicts the header is never given: the answer is <see cref="LookupStatus.Failed"/>.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="hash">The block's hash.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The block.</returns>
    Task<BitcoinLookup<BitcoinBlock>> GetBlockAsync(
        ICase @case, BlockHash hash, CancellationToken cancellationToken = default);

    /// <summary>Gets the height of the newest block.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The height.</returns>
    Task<BitcoinLookup<BitcoinTip>> GetTipHeightAsync(ICase @case, CancellationToken cancellationToken = default);
}
