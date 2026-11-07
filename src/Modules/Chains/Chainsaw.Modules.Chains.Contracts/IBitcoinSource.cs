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
    /// Gets a transaction. Its outputs say which input spends them when the case has the answer from
    /// <see cref="GetOutputSpendsAsync"/>.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="txid">The transaction's id.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The transaction.</returns>
    Task<BitcoinLookup<BitcoinTransaction>> GetTransactionAsync(
        ICase @case, Txid txid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets which transactions spend a transaction's outputs, which is how the money is followed forward.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="txid">The transaction's id.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>One entry per output, in order.</returns>
    Task<BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>> GetOutputSpendsAsync(
        ICase @case, Txid txid, CancellationToken cancellationToken = default);

    /// <summary>Gets a block's header.</summary>
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
