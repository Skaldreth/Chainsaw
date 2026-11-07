using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// The Ethereum facts the graph is built from, read from the case: every movement and fee each once, the whole
/// transactions, the accounts whose history was read, and the blocks' hashes.
/// </summary>
/// <param name="Transfers">The movements, each once, in chain order (<see cref="AccountGraph.ChainOrder"/>).</param>
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

        return new AccountData(
            [.. transfers.Values.OrderBy(AccountGraph.ChainOrder)],
            [.. fees.Values.Where(fee => fee.Amount.Units.Sign > 0).OrderBy(fee => fee.Block?.Height ?? long.MaxValue)
                .ThenBy(fee => fee.Transaction.Value, StringComparer.Ordinal)],
            whole,
            read,
            outdated);
    }

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
                        Sum(bought[0].Key, bought[0])));
                }
            }
        }

        return swaps;
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
