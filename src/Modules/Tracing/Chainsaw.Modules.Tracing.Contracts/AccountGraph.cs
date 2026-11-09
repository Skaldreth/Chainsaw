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
/// then by position in it (<see cref="AccountGraph.ChainOrder(EthereumTransfer)"/>).
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

    /// <summary>
    /// Gets the transactions that would read as a swap but for a counterparty the trail must reach (a known bridge or
    /// mixer, or a label as exchange, mixer or bridge), so that <em>Varför?</em> can say why there is no swap.
    /// </summary>
    public IReadOnlyList<SwapRefusal> RefusedSwaps { get; init; } = [];

    /// <summary>Gets whether the part holds anything.</summary>
    public bool IsEmpty => Transfers.Count == 0 && Fees.Count == 0 && Transactions.Count == 0 && ReadAccounts.Count == 0;

    /// <summary>
    /// Gives the order Chainsaw applies movements in: by block (unconfirmed last), then by the transaction's place in
    /// the block (<see cref="EthereumBlockReference.TransactionIndex"/>), then by the transaction's hash, then by
    /// position in the transaction. A transaction whose place no answer gave comes after those in the same block whose
    /// place is known, ordered by hash; that only matters to FIFO when an account both receives and pays in one block.
    /// </summary>
    /// <param name="transfer">The movement.</param>
    /// <returns>The key to sort by.</returns>
    public static (long Block, long Index, string Transaction, long Position, int Kind) ChainOrder(
        EthereumTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        return ChainOrder(AssetTransfer.OfEthereum(transfer, null));
    }

    /// <summary>
    /// Gives the order of a movement as a graph's line carries it, the same as
    /// <see cref="ChainOrder(EthereumTransfer)"/>, so that the exposure reads an account's ledger in the order the
    /// graph's trace does.
    /// </summary>
    /// <param name="transfer">The movement on an account chain.</param>
    /// <returns>The key to sort by.</returns>
    public static (long Block, long Index, string Transaction, long Position, int Kind) ChainOrder(
        AssetTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        return (transfer.Height ?? long.MaxValue, transfer.AccountBlock?.TransactionIndex ?? long.MaxValue,
            transfer.Transaction.Id, transfer.Position, (int?)transfer.Movement ?? 0);
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
public sealed record TokenSwap(EthereumTxHash Transaction, ChainAddress Account, AssetAmount Sold, AssetAmount Bought)
{
    /// <summary>
    /// Gets the known exchange or aggregator the swap went through (<see cref="KnownContracts"/>): the contract the
    /// transaction was sent to, or a contract the account sent to or received from in it; <see langword="null"/> when
    /// none of them is on the list. It names the swap in <em>varför?</em>, and rests on the list's source.
    /// </summary>
    public KnownContract? Venue { get; init; }

    /// <summary>Gets the swap's block time, which its price is tested at, or <see langword="null"/> while unconfirmed.</summary>
    public DateTimeOffset? Time { get; init; }

    /// <summary>
    /// Gets the movements the account sent into the swap: only those to the other side of the swap (an address that
    /// sent the bought asset back, the contract the transaction called, or the known exchange), or the one movement
    /// in the sold asset when the transaction says no more. A payment to a third party in the same transaction is not
    /// part of the swap and is followed as any other.
    /// </summary>
    public IReadOnlyList<AccountSeed> SoldMovements { get; init; } = [];

    /// <summary>
    /// Says whether a movement is part of what the account sent into the swap (<see cref="SoldMovements"/>).
    /// </summary>
    /// <param name="transfer">The movement.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool IsSold(EthereumTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        return SoldMovements.Any(movement => movement.Is(transfer));
    }

    /// <summary>Says whether a transfer on one of the graph's lines is part of what went into the swap.</summary>
    /// <param name="transfer">The transfer.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool IsSold(AssetTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        return SoldMovements.Any(movement => movement.Is(transfer));
    }

    /// <summary>
    /// Says whether another reading is the same swap: one transaction and one account make at most one swap, so they
    /// name it, whatever else was read with it.
    /// </summary>
    /// <param name="other">The other swap.</param>
    /// <returns><see langword="true"/> when it is the same swap.</returns>
    public bool Equals(TokenSwap? other) =>
        other is not null && other.Transaction == Transaction && other.Account == Account;

    /// <summary>Gives the hash of the swap's identity: its transaction and account.</summary>
    /// <returns>The hash.</returns>
    public override int GetHashCode() => HashCode.Combine(Transaction, Account);

    /// <summary>
    /// Says whether a movement is part of what came out of the swap: in the swap's transaction, from another address
    /// to the account, in the bought asset.
    /// </summary>
    /// <param name="transfer">The movement.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool IsBought(EthereumTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        return transfer.Transaction == Transaction && transfer.From != transfer.To
            && transfer.To.ToChainAddress() == Account && transfer.Amount.Asset == Bought.Asset;
    }
}

/// <summary>
/// A transaction that would read as a swap but is not one, because a counterparty is one the trail must reach: the
/// money goes on to it as an ordinary payment.
/// </summary>
/// <param name="Swap">The swap it would have been.</param>
/// <param name="Counterparty">The counterparty the trail must reach.</param>
/// <param name="Why">Why, in Swedish, with the label or list it rests on and its source.</param>
public sealed record SwapRefusal(TokenSwap Swap, ChainAddress Counterparty, string Why);

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

    /// <summary>Says whether a transfer on one of the graph's lines is this movement.</summary>
    /// <param name="transfer">The transfer.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool Is(AssetTransfer transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        return transfer.Movement == Kind && transfer.Position == Position
            && transfer.Transaction.Chain == Transaction.Chain && transfer.Transaction.Id == Transaction.Value;
    }
}

/// <summary>What to trace on the account chain and how.</summary>
/// <param name="Method">The tracing method; the newest version registered for Ethereum is used.</param>
/// <param name="Seeds">The movements the traced money starts in, whole; of any assets.</param>
public sealed record AccountTraceRequest(TracingMethodKind Method, IReadOnlyList<AccountSeed> Seeds)
{
    /// <summary>
    /// Gets the case's rates for the swaps' price check (<see cref="SwapValue.CheckPrice"/>), or
    /// <see langword="null"/>: then no price is tested, and the value follows as the default, said in each check.
    /// </summary>
    public SwapPricing? Pricing { get; init; }
}

/// <summary>
/// The result of a trace on the account chain: where the traced money went by one method, per asset. Through a swap
/// the traced value follows into the new asset by <see cref="SwapValue"/>, unless the user chose that it does not;
/// everything in it is a conclusion that names its method and, past a swap, the analysis.
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

    /// <summary>
    /// Gets what the rule <see cref="SwapValue"/> did at each swap the traced money reached, followed or not, in chain
    /// order; empty when it reached none.
    /// </summary>
    public IReadOnlyList<SwapConversion> Swaps { get; init; } = [];

    /// <summary>
    /// Gets the price check of every swap in the graph (<see cref="SwapValue.CheckPrice"/>); a swap with an absurd
    /// price was traced as an ordinary payment.
    /// </summary>
    public IReadOnlyList<SwapPriceCheck> PriceChecks { get; init; } = [];

    /// <summary>Gives a swap's price check, or <see langword="null"/> when it has none.</summary>
    /// <param name="swap">The swap.</param>
    /// <returns>The check.</returns>
    public SwapPriceCheck? PriceOf(TokenSwap swap) => PriceChecks.FirstOrDefault(check => check.Swap.Equals(swap));

    /// <summary>Gives what the rule did at a swap, when the traced money reached it.</summary>
    /// <param name="swap">The swap.</param>
    /// <returns>The conversion, or <see langword="null"/>.</returns>
    public SwapConversion? SwapOf(TokenSwap swap)
    {
        ArgumentNullException.ThrowIfNull(swap);
        return Swaps.FirstOrDefault(conversion => conversion.Swap.Transaction == swap.Transaction
            && conversion.Swap.Account == swap.Account);
    }
}

/// <summary>Where the traced money of one asset went on the account chain, by one method.</summary>
/// <param name="Traced">How much was traced from the seeds of the asset (the user's choice, not a conclusion).</param>
/// <param name="Edges">The traced money per edge of the graph; only edges with some.</param>
/// <param name="Ends">Where the money stops; they add up to <see cref="Total"/>.</param>
public sealed record AssetTrace(AssetAmount Traced, IReadOnlyList<TracedEdge> Edges, IReadOnlyList<TraceEnd> Ends)
{
    /// <summary>
    /// Gets how much of the asset follows traced value that came in through swaps (<see cref="SwapValue"/>), a
    /// conclusion by the analysis; zero when none did.
    /// </summary>
    public AssetAmount SwappedIn { get; init; } = AssetAmount.Zero(Traced.Asset);

    /// <summary>Gets all the traced money of the asset: from the seeds and through swaps.</summary>
    public AssetAmount Total => Traced + SwappedIn;
}
