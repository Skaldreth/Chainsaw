using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// The Ethereum facts the graph is built from, read from the case: every movement and fee each once, the whole
/// transactions, the accounts whose history was read, and the blocks' hashes.
/// </summary>
/// <param name="Transfers">
/// The movements, each once, in chain order (<see cref="AccountGraph.ChainOrder(EthereumTransfer)"/>).
/// </param>
/// <param name="Fees">The fees, one per transaction.</param>
/// <param name="Transactions">The transactions read whole.</param>
/// <param name="ReadAccounts">The accounts whose history was read.</param>
/// <param name="MayBeOutdated">The accounts whose read history may have changed since.</param>
internal sealed record AccountData(
    IReadOnlyList<EthereumTransfer> Transfers,
    IReadOnlyList<EthereumFee> Fees,
    IReadOnlyDictionary<EthereumTxHash, EthereumTransaction> Transactions,
    IReadOnlySet<ChainAddress> ReadAccounts,
    IReadOnlySet<ChainAddress> MayBeOutdated)
{
    /// <summary>Gets data with nothing in it, for a graph without Ethereum.</summary>
    public static AccountData Empty { get; } = new(
        [], [], new Dictionary<EthereumTxHash, EthereumTransaction>(), new HashSet<ChainAddress>(),
        new HashSet<ChainAddress>());

    /// <summary>Gets each block's hash by height, where the case has it.</summary>
    public IReadOnlyDictionary<long, string> BlockHashes { get; init; } = new Dictionary<long, string>();

    /// <summary>
    /// Gets the accounts whose history was read only in part (held back at the limit or incomplete); they do not carry
    /// money on.
    /// </summary>
    public IReadOnlySet<ChainAddress> PartlyRead { get; init; } = new HashSet<ChainAddress>();

    /// <summary>Gets what could not be read, in Swedish.</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];

    /// <summary>
    /// Joins what the whole transactions and the histories say. A transaction read whole is the surest answer and
    /// gives its own movements and fee; the histories add the movements and fees of the other transactions, each once.
    /// </summary>
    /// <param name="whole">The transactions read whole.</param>
    /// <param name="history">The movements the histories gave.</param>
    /// <param name="historyFees">The fees the histories gave.</param>
    /// <param name="read">The accounts whose history was read.</param>
    /// <param name="outdated">The accounts whose read history may have changed.</param>
    /// <returns>The data.</returns>
    public static AccountData Combine(
        IReadOnlyDictionary<EthereumTxHash, EthereumTransaction> whole,
        IEnumerable<EthereumTransfer> history,
        IEnumerable<EthereumFee> historyFees,
        IReadOnlySet<ChainAddress> read,
        IReadOnlySet<ChainAddress> outdated)
    {
        var transfers = new Dictionary<TransferKey, EthereumTransfer>();
        foreach (var transfer in whole.Values.SelectMany(transaction => transaction.Transfers)
            .Concat(history.Where(transfer => !whole.ContainsKey(transfer.Transaction))))
        {
            transfers.TryAdd(TransferKey.Of(transfer), transfer);
        }

        var fees = new Dictionary<EthereumTxHash, EthereumFee>();
        foreach (var transaction in whole.Values.Where(transaction => transaction.IsConfirmed))
        {
            fees[transaction.Hash] = new EthereumFee(
                transaction.Hash, transaction.From, transaction.Fee, transaction.Succeeded, transaction.Block);
        }

        foreach (var fee in historyFees)
        {
            fees.TryAdd(fee.Transaction, fee);
        }

        var places = Places(transfers.Values.Select(transfer => (transfer.Transaction, transfer.Block))
            .Concat(fees.Values.Select(fee => (fee.Transaction, fee.Block))));
        return new AccountData(
            [.. transfers.Values.Select(transfer => WithPlace(transfer, places)).OrderBy(AccountGraph.ChainOrder)],
            [.. fees.Values.Where(fee => fee.Amount.Units.Sign > 0).Select(fee => WithPlace(fee, places))
                .OrderBy(fee => fee.Block?.Height ?? long.MaxValue)
                .ThenBy(fee => fee.Block?.TransactionIndex ?? long.MaxValue)
                .ThenBy(fee => fee.Transaction.Value, StringComparer.Ordinal)],
            whole,
            read,
            outdated);
    }

    /// <summary>
    /// Gathers each transaction's place in its block from the answers that gave it, so that every movement of one
    /// transaction is ordered the same way whichever history it came from (a list of internal transactions gives no
    /// place, the list of the address's transactions does).
    /// </summary>
    /// <param name="blocks">Each movement's or fee's transaction and block.</param>
    /// <returns>The places by transaction.</returns>
    private static Dictionary<EthereumTxHash, long> Places(
        IEnumerable<(EthereumTxHash Transaction, EthereumBlockReference? Block)> blocks)
    {
        var places = new Dictionary<EthereumTxHash, long>();
        foreach (var (transaction, block) in blocks)
        {
            if (block?.TransactionIndex is { } place)
            {
                places.TryAdd(transaction, place);
            }
        }

        return places;
    }

    /// <summary>Gives a movement with its transaction's place in the block, when an answer gave the place.</summary>
    /// <param name="transfer">The movement.</param>
    /// <param name="places">The places by transaction.</param>
    /// <returns>The movement, with the place on its block.</returns>
    private static EthereumTransfer WithPlace(EthereumTransfer transfer, Dictionary<EthereumTxHash, long> places) =>
        transfer.Block is { TransactionIndex: null } block && places.TryGetValue(transfer.Transaction, out var place)
            ? transfer with { Block = block with { TransactionIndex = place } }
            : transfer;

    /// <summary>Gives a fee with its transaction's place in the block, when an answer gave the place.</summary>
    /// <param name="fee">The fee.</param>
    /// <param name="places">The places by transaction.</param>
    /// <returns>The fee, with the place on its block.</returns>
    private static EthereumFee WithPlace(EthereumFee fee, Dictionary<EthereumTxHash, long> places) =>
        fee.Block is { TransactionIndex: null } block && places.TryGetValue(fee.Transaction, out var place)
            ? fee with { Block = block with { TransactionIndex = place } }
            : fee;

    /// <summary>
    /// Finds the swaps the transactions show: in one transaction the account that signed it sends exactly one asset
    /// away and receives exactly one other back. Only the signer is asked, since the pool on the other side does the
    /// same in reverse; when the signer is not known (a history that is not its own), or it sends or receives more
    /// than one asset so that which was swapped for which cannot be read, no swap is shown.
    /// </summary>
    /// <returns>The swaps, in chain order.</returns>
    public IReadOnlyList<TokenSwap> Swaps()
    {
        var signers = Fees.ToDictionary(fee => fee.Transaction, fee => fee.Payer);
        foreach (var transaction in Transactions.Values)
        {
            signers[transaction.Hash] = transaction.From;
        }

        var swaps = new List<TokenSwap>();
        foreach (var transaction in Transfers.GroupBy(transfer => transfer.Transaction))
        {
            var moves = transaction.Where(transfer => transfer.From != transfer.To).ToList();
            if (signers.GetValueOrDefault(transaction.Key) is { } account)
            {
                var sold = moves.Where(transfer => transfer.From == account).GroupBy(transfer => transfer.Amount.Asset)
                    .ToList();
                var bought = moves.Where(transfer => transfer.To == account).GroupBy(transfer => transfer.Amount.Asset)
                    .ToList();
                if (sold.Count == 1 && bought.Count == 1 && sold[0].Key != bought[0].Key)
                {
                    swaps.Add(new TokenSwap(
                        transaction.Key,
                        account.ToChainAddress(),
                        Sum(sold[0].Key, sold[0]),
                        Sum(bought[0].Key, bought[0]))
                    {
                        Venue = VenueOf(transaction.Key, account, moves),
                    });
                }
            }
        }

        return swaps;
    }

    /// <summary>
    /// Finds the known exchange or aggregator a swap went through: the contract the transaction was sent to, when the
    /// whole transaction is read, or else a contract the account sent to or received from in it. Only an exchange or
    /// an aggregator names a swap; a mixer or a bridge on the other side is not one.
    /// </summary>
    /// <param name="transaction">The swap's transaction.</param>
    /// <param name="account">The account that swapped.</param>
    /// <param name="moves">The transaction's movements between two addresses.</param>
    /// <returns>The contract, or <see langword="null"/> when none is known.</returns>
    private KnownContract? VenueOf(
        EthereumTxHash transaction, EthereumAddress account, IEnumerable<EthereumTransfer> moves)
    {
        var called = Transactions.GetValueOrDefault(transaction)?.To;
        var counterparties = moves.Select(move => CounterpartyOf(move, account)).OfType<EthereumAddress>();
        return (called is null ? counterparties : counterparties.Prepend(called))
            .Select(KnownContracts.Find)
            .FirstOrDefault(known => known?.Category is KnownContractCategory.Dex or KnownContractCategory.Aggregator);
    }

    /// <summary>Gives the other side of a movement an account is on.</summary>
    /// <param name="move">The movement.</param>
    /// <param name="account">The account.</param>
    /// <returns>The other address, or <see langword="null"/> when the account is not on the movement.</returns>
    private static EthereumAddress? CounterpartyOf(EthereumTransfer move, EthereumAddress account)
    {
        if (move.From == account)
        {
            return move.To;
        }

        return move.To == account ? move.From : null;
    }

    /// <summary>Gives a movement's block with its hash, when the case has the hash.</summary>
    /// <param name="block">The movement's block.</param>
    /// <returns>The block for the graph, or <see langword="null"/> while unconfirmed or the hash is not known.</returns>
    public BlockReference? BlockOf(EthereumBlockReference? block)
    {
        if (block is null)
        {
            return null;
        }


        var text = block.Hash ?? BlockHashes.GetValueOrDefault(block.Height);
        return BlockHash.TryParseEthereum(text, out var hash) ? new BlockReference(block.Height, hash, block.Time) : null;
    }

    /// <summary>Adds the amounts of some movements of one asset.</summary>
    /// <param name="asset">The asset.</param>
    /// <param name="transfers">The movements.</param>
    /// <returns>The sum.</returns>
    private static AssetAmount Sum(Asset asset, IEnumerable<EthereumTransfer> transfers) =>
        transfers.Aggregate(AssetAmount.Zero(asset), (sum, transfer) => sum + transfer.Amount);
}
