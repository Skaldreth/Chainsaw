using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// The transactions the case knows of, as the analyses see them: found by txid, and with every address mapped to the
/// blocks it appears in, so that <see cref="SeenBefore"/> is quick.
/// </summary>
internal sealed class KnownTransactions : IHeuristicContext
{
    /// <summary>The transactions by txid.</summary>
    private readonly Dictionary<Txid, BitcoinTransaction> _byTxid = [];

    /// <summary>Every address and the transactions it appears in (as input or output).</summary>
    private readonly Dictionary<BitcoinAddress, List<BitcoinTransaction>> _byAddress = [];

    /// <summary>Every spent output the case knows of and the transaction that spends it.</summary>
    private readonly Dictionary<(Txid Txid, int Index), BitcoinTransaction> _spenders = [];

    /// <summary>Indexes the transactions; a txid given twice counts once (the first).</summary>
    /// <param name="transactions">The transactions.</param>
    public KnownTransactions(IEnumerable<BitcoinTransaction> transactions)
    {
        foreach (var transaction in transactions)
        {
            if (!_byTxid.TryAdd(transaction.Txid, transaction))
            {
                continue;
            }

            foreach (var input in transaction.Inputs)
            {
                if (input is { PreviousTxid: { } previous, PreviousIndex: { } index })
                {
                    _spenders.TryAdd((previous, index), transaction);
                }
            }

            var addresses = transaction.Inputs.Select(input => input.Address)
                .Concat(transaction.Outputs.Select(output => output.Address))
                .OfType<BitcoinAddress>()
                .Distinct();
            foreach (var address in addresses)
            {
                if (!_byAddress.TryGetValue(address, out var list))
                {
                    list = [];
                    _byAddress[address] = list;
                }

                list.Add(transaction);
            }
        }
    }

    /// <summary>Gets the transactions, in the order they were first given.</summary>
    public IEnumerable<BitcoinTransaction> All => _byTxid.Values;

    /// <inheritdoc/>
    public int Count => _byTxid.Count;

    /// <inheritdoc/>
    public BitcoinTransaction? Find(Txid txid) => _byTxid.GetValueOrDefault(txid);

    /// <inheritdoc/>
    public BitcoinTransaction? FindSpender(Txid txid, int outputIndex) =>
        _spenders.GetValueOrDefault((txid, outputIndex));

    /// <inheritdoc/>
    public BitcoinTransaction? SeenBefore(BitcoinAddress address, BitcoinTransaction transaction) =>
        _byAddress.GetValueOrDefault(address)?
            .Where(other => IsBefore(other, transaction))
            .OrderBy(other => other.Block!.Height)
            .ThenBy(other => other.Txid.Value, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>
    /// Says whether one transaction certainly came before another: confirmed in a lower block, or confirmed while the
    /// other is not. Two transactions in the same block cannot be ordered from what the model has.
    /// </summary>
    /// <param name="earlier">The transaction that may be earlier.</param>
    /// <param name="later">The transaction that may be later.</param>
    /// <returns><see langword="true"/> when it certainly came before.</returns>
    private static bool IsBefore(BitcoinTransaction earlier, BitcoinTransaction later) =>
        earlier.Block is { } block
        && (later.Block is null || block.Height < later.Block.Height);
}
