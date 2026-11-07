using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// The account model's part of the graph (Ethereum first): every movement of value and every fee the graph holds, the
/// transactions read with all their movements, which accounts' histories were read, and the swaps the transactions
/// themselves show. The tracing methods for accounts work on this. Everything in it is a fact from the chain, except
/// that a swap is recognised from a pattern (<see cref="TokenSwap"/>).
/// </summary>
/// <param name="Transfers">
/// Every movement of value in the graph, each once, in the order Chainsaw applies them: by block, then by transaction,
/// then by position in it (<see cref="AccountGraph.ChainOrder"/>).
/// </param>
/// <param name="Fees">Every fee the graph knows an account paid, each transaction once.</param>
/// <param name="Transactions">The transactions read whole (start points and expanded transactions), by hash.</param>
/// <param name="ReadAccounts">
/// The accounts whose own history the graph has read whole (the expansion was complete), so that their ledgers can
/// carry money on; money that reaches any other account stops there as <em>not expanded yet</em>.
/// </param>
/// <param name="Swaps">The swaps the transactions show, in the transfers' order.</param>
public sealed record AccountGraph(
    IReadOnlyList<EthereumTransfer> Transfers,
    IReadOnlyList<EthereumFee> Fees,
    IReadOnlyDictionary<EthereumTxHash, EthereumTransaction> Transactions,
    IReadOnlySet<ChainAddress> ReadAccounts,
    IReadOnlyList<TokenSwap> Swaps)
{
    /// <summary>Gets an account part with nothing in it.</summary>
    public static AccountGraph Empty { get; } = new(
        [], [], new Dictionary<EthereumTxHash, EthereumTransaction>(), new HashSet<ChainAddress>(), []);

    /// <summary>
    /// Gets the accounts whose read history may have changed since the case asked (a first page fetched again, or an
    /// answer the case could not renew); the traced amounts that rest on them say so.
    /// </summary>
    public IReadOnlySet<ChainAddress> MayBeOutdated { get; init; } = new HashSet<ChainAddress>();

    /// <summary>
    /// Gets the accounts whose history was read only in part: held back at the limit, or left incomplete. Their ledger
    /// has a gap, so money that reaches them stops there, and no balance is shown for them.
    /// </summary>
    public IReadOnlySet<ChainAddress> PartlyRead { get; init; } = new HashSet<ChainAddress>();

    /// <summary>Gets whether the part holds anything.</summary>
    public bool IsEmpty => Transfers.Count == 0 && Fees.Count == 0 && Transactions.Count == 0 && ReadAccounts.Count == 0;

    /// <summary>
    /// Gives the order Chainsaw applies movements in: by block (unconfirmed last), then by the transaction's hash, then
    /// by position in the transaction. The sources do not give a transaction's place in its block, so two transactions
    /// in the same block are ordered by hash; that only matters to FIFO when an account both receives and pays in
    /// one block.
    /// </summary>
    /// <param name="transfer">The movement.</param>
    /// <returns>The key to sort by.</returns>
    public static (long Block, string Transaction, long Position, int Kind) ChainOrder(EthereumTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        return (transfer.Block?.Height ?? long.MaxValue, transfer.Transaction.Value, transfer.Position,
            (int)transfer.Kind);
    }
}

/// <summary>
/// A swap the transaction itself shows: in one transaction the same account sends one asset away and receives another
/// back, for example USDT out to a DEX's pool and ether in from it (sketch TK1a, the pill <c>₮ → Ξ</c>). The money
/// changes token but not owner. It is recognised from the movements, which are facts; that it was a swap is a reading
/// of them, shown as a pill and not as an end of the trail.
/// </summary>
/// <param name="Transaction">The transaction.</param>
/// <param name="Account">The account that swapped.</param>
/// <param name="Sold">What the account sent away in it, summed.</param>
/// <param name="Bought">What it received back in another asset, summed.</param>
public sealed record TokenSwap(EthereumTxHash Transaction, ChainAddress Account, AssetAmount Sold, AssetAmount Bought);

/// <summary>A movement the traced money starts in on an account chain.</summary>
/// <param name="Transaction">The transaction it is in.</param>
/// <param name="Kind">How the value moved.</param>
/// <param name="Position">Where in the transaction.</param>
public sealed record AccountSeed(EthereumTxHash Transaction, EthereumTransferKind Kind, long Position)
{
    /// <summary>Gives the seed of a movement.</summary>
    /// <param name="transfer">The movement.</param>
    /// <returns>The seed.</returns>
    public static AccountSeed Of(EthereumTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        return new AccountSeed(transfer.Transaction, transfer.Kind, transfer.Position);
    }

    /// <summary>Says whether a movement is this seed's.</summary>
    /// <param name="transfer">The movement.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool Is(EthereumTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        return transfer.Transaction == Transaction && transfer.Kind == Kind && transfer.Position == Position;
    }
}

/// <summary>What to trace on the account chain and how.</summary>
/// <param name="Method">The tracing method; the newest version registered for Ethereum is used.</param>
/// <param name="Seeds">The movements the traced money starts in, whole; of any assets.</param>
public sealed record AccountTraceRequest(TracingMethodKind Method, IReadOnlyList<AccountSeed> Seeds);

/// <summary>
/// The result of a trace on the account chain: where the traced money went by one method, per asset. Money never
/// changes asset in a trace (a swap is shown, not followed); everything in it is a conclusion that names its method.
/// </summary>
/// <param name="Method">The method and its version.</param>
/// <param name="Seeds">The movements the money starts in.</param>
/// <param name="Assets">The trace of each asset the seeds hold, in the order of the first seed of each.</param>
public sealed record AccountTraceResult(
    TracingMethodId Method, IReadOnlyList<AccountSeed> Seeds, IReadOnlyList<AssetTrace> Assets)
{
    /// <summary>Gets the traced money per edge of the graph, of every asset; only edges with some.</summary>
    public IEnumerable<TracedEdge> Edges => Assets.SelectMany(asset => asset.Edges);

    /// <summary>Gets where the money stops, of every asset.</summary>
    public IEnumerable<TraceEnd> Ends => Assets.SelectMany(asset => asset.Ends);
}

/// <summary>Where the traced money of one asset went on the account chain, by one method.</summary>
/// <param name="Traced">How much was traced from the seeds of the asset (the user's choice, not a conclusion).</param>
/// <param name="Edges">The traced money per edge of the graph; only edges with some.</param>
/// <param name="Ends">Where the money stops; they add up to <see cref="Traced"/>.</param>
public sealed record AssetTrace(AssetAmount Traced, IReadOnlyList<TracedEdge> Edges, IReadOnlyList<TraceEnd> Ends);
