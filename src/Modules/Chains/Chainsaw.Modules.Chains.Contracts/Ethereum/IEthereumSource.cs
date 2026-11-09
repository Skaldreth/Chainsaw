using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// Ethereum's facts for a case, through the account model's contract (<see cref="IAccountSource"/>); the first sort of
/// API is Blockscout's, which can also be run in-house. The interface only names the chain, so that the container
/// gives Ethereum's source where Ethereum is asked.
/// </summary>
public interface IEthereumSource : IAccountSource
{
    /// <summary>Gets the chain: <see cref="SharedKernel.Chain.Ethereum"/>.</summary>
    Chain IAccountSource.Chain => Chain.Ethereum;

    /// <summary>Gives where a page that goes back from a block starts: Ethereum's sources page by block.</summary>
    /// <param name="block">The block.</param>
    /// <returns>The block's number.</returns>
    long IAccountSource.PageStartOf(EthereumBlockReference block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return block.Height;
    }
}

/// <summary>
/// TRON's facts for a case, through the account model's contract (<see cref="IAccountSource"/>): TRX in sun, TRC-20
/// tokens (USDT first) from their <c>Transfer</c> events, and TRX sent by contracts. The first sort of API is
/// TronGrid's, whose wallet part every TRON node also answers. The interface only names the chain, so that the
/// container gives TRON's source where TRON is asked.
/// </summary>
public interface ITronSource : IAccountSource
{
    /// <summary>Gets the chain: <see cref="SharedKernel.Chain.Tron"/>.</summary>
    Chain IAccountSource.Chain => Chain.Tron;
}

/// <summary>
/// An account chain's facts for a case (Ethereum and TRON): an address's history (its transactions, internal
/// transactions and token transfers), a transaction with every movement of value in it (the coin, internal and tokens
/// with their contract and decimals), a block and the chain's height. One contract for the kind of chain, not per
/// source (principle 8); each chain's interface (<see cref="IEthereumSource"/>, <see cref="ITronSource"/>) names it.
/// </summary>
/// <remarks>
/// <para>
/// Every response is sealed in the case with its endpoint, exit, time, who fetched it and its SHA-256 before it is used
/// (principle 1). Requests go only through the exit and carry only what the lookup needs (principle 4); what cannot
/// have changed is never fetched again; and without an exit the case answers and says what is missing (principle 9).
/// Nothing is calculated and given as a fact: the fee is the chain's gas used times its price, and a token's decimals
/// come from the source.
/// </para>
/// <para>
/// The methods never throw for a failed lookup; the answer's <see cref="BitcoinLookup{T}.Status"/> says what
/// happened (the type is shared by every chain). They throw only for invalid arguments, cancellation, or a
/// <see cref="CaseException"/> (a closed case, or a sealed response that no longer matches its SHA-256).
/// </para>
/// </remarks>
public interface IAccountSource
{
    /// <summary>Gets the chain the source answers for; every address and transaction it takes is on it.</summary>
    Chain Chain { get; }

    /// <summary>
    /// Gives where a page of an address's history starts when it goes back from a block (the unit of
    /// <see cref="EthereumAddressPage.AtOrBelow"/>): the block's number on Ethereum, whose sources page by block, and
    /// the block's time in milliseconds since 1970 on TRON, whose sources page by time.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <returns>The page's start.</returns>
    long PageStartOf(EthereumBlockReference block);

    /// <summary>
    /// Gets one page of an address's history for a check that needs nothing older than a block's time. A source that
    /// must look each entry up on its own (TRON's token history, which lacks the block) leaves the older entries out,
    /// neither looked up nor counted; a source that does not need to gives the whole page.
    /// </summary>
    /// <param name="case">The open case the responses are sealed in.</param>
    /// <param name="address">The address.</param>
    /// <param name="history">Which history.</param>
    /// <param name="atOrBelow">Where the page starts, or <see langword="null"/> for the newest.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="notBefore">The oldest block time the check needs.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The page.</returns>
    Task<BitcoinLookup<EthereumAddressPage>> GetRecentHistoryAsync(
        ICase @case, EthereumAddress address, EthereumHistory history, long? atOrBelow, LookupMode mode,
        DateTimeOffset notBefore, CancellationToken cancellationToken = default) =>
        GetAddressHistoryAsync(@case, address, history, atOrBelow, mode, cancellationToken);


    /// <summary>Gets one page of an address's history of one kind, newest first.</summary>
    /// <param name="case">The open case the responses are sealed in.</param>
    /// <param name="address">The address.</param>
    /// <param name="history">Which history.</param>
    /// <param name="atOrBelow">
    /// <see langword="null"/> for the first page, or <see cref="EthereumAddressPage.NextAtOrBelow"/> of the previous.
    /// </param>
    /// <param name="mode">Whether the source may be asked; <see cref="LookupMode.CaseOnly"/> sends nothing.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The page.</returns>
    Task<BitcoinLookup<EthereumAddressPage>> GetAddressHistoryAsync(
        ICase @case, EthereumAddress address, EthereumHistory history, long? atOrBelow = null,
        LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a transaction with every movement of value in it: its ether, its internal transactions and its token
    /// transfers (read from the logs' <c>Transfer</c> events, with each token's decimals), and its block with the hash.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="hash">The transaction's hash.</param>
    /// <param name="mode">Whether the source may be asked; <see cref="LookupMode.CaseOnly"/> sends nothing.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The transaction.</returns>
    Task<BitcoinLookup<EthereumTransaction>> GetTransactionAsync(
        ICase @case, EthereumTxHash hash, LookupMode mode = LookupMode.AsNeeded,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the block at a height: its hash and time.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="height">The block's number.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The block.</returns>
    Task<BitcoinLookup<EthereumBlockReference>> GetBlockAsync(
        ICase @case, long height, LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default);

    /// <summary>Gets the height of the newest block.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The height.</returns>
    Task<BitcoinLookup<EthereumTip>> GetTipHeightAsync(ICase @case, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the newest block the source has read, with its height, hash and time in one answer (always fetched anew),
    /// so that the height a lookup was made at comes with the hash that names the chain. A source that cannot say
    /// answers <see cref="LookupStatus.Failed"/> and sends nothing.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The newest block.</returns>
    Task<BitcoinLookup<ChainTip>> GetTipAsync(ICase @case, CancellationToken cancellationToken = default) =>
        Task.FromResult(new BitcoinLookup<ChainTip>(
            LookupStatus.Failed, null, [], "Källan kan inte säga kedjans hash. Inget skickades."));

    /// <summary>
    /// Gets what an address holds now: its ether, and the ERC-20 tokens the source says it holds, each with the
    /// decimals the source gave. A balance always changes, so it is fetched anew (and sealed) every time; offline the
    /// case answers and says when it was fetched. When the tokens cannot be read the ether is still given, the
    /// balance says it is not complete, and the notes say why. A source that cannot say answers
    /// <see cref="LookupStatus.Failed"/> and sends nothing.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The balance.</returns>
    Task<BitcoinLookup<EthereumBalance>> GetBalanceAsync(
        ICase @case, EthereumAddress address, CancellationToken cancellationToken = default) =>
        Task.FromResult(new BitcoinLookup<EthereumBalance>(
            LookupStatus.Failed, null, [], "Källan kan inte säga adressens saldo. Inget skickades."));

    /// <summary>
    /// Gets the transactions that a newer sealed answer shows in another block than the case first saw them in, or
    /// unconfirmed again, or no longer known to the source: the reorganisations the case has seen, compared as
    /// Bitcoin's are (<see cref="IBitcoinSource.GetBlockChangesAsync"/>). Only what was fetched while its block was
    /// shallow can change, since Ethereum's blocks are final after 64. It reads the case only and sends nothing;
    /// every answer stays in the case as it was sealed (principle 1).
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    /// <returns>The changes, each on <see cref="SharedKernel.Chain.Ethereum"/>; empty when there are none.</returns>
    Task<IReadOnlyList<BlockChange>> GetBlockChangesAsync(
        ICase @case, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BlockChange>>([]);
}

/// <summary>The settings of the Ethereum source.</summary>
public sealed record EthereumSourceOptions
{
    /// <summary>Blockscout's API for Ethereum, the default: open without a key, and it can be run in-house.</summary>
    public static readonly Uri Blockscout = new("https://eth.blockscout.com/api/");

    /// <summary>Gets Ethereum's endpoints until the user changes them in the settings (sketch KL1a).</summary>
    public IReadOnlyList<Uri> DefaultEndpoints { get; init; } = [Blockscout];

    /// <summary>
    /// Gets how many blocks deep a block must be before what rests on it is never fetched again. Ethereum's blocks are
    /// final after two epochs (64 blocks, about 13 minutes); a reorganisation deeper than that cannot happen.
    /// </summary>
    public int SafeConfirmations { get; init; } = 64;

    /// <summary>Gets how many entries a page of an address's history asks for.</summary>
    public int PageSize { get; init; } = 100;
}
