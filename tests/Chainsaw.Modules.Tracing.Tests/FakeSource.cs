using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// A Bitcoin source without a network: a synthetic chain in memory. Online it answers from the chain and remembers
/// what it gave, as the case would; offline it answers only what it gave before, the way Chains does without an exit.
/// </summary>
internal sealed class FakeSource : IBitcoinSource
{
    /// <summary>The page size, small so that paging is tested.</summary>
    public const int PageSize = 3;

    private readonly Dictionary<Txid, BitcoinTransaction> _chain = [];
    private readonly HashSet<Txid> _givenTransactions = [];
    private readonly Dictionary<Txid, IReadOnlyList<BitcoinOutputSpend>> _givenSpends = [];
    private readonly Dictionary<(BitcoinAddress, Txid?), BitcoinAddressPage> _givenPages = [];

    public BitcoinNetwork Network => BitcoinNetwork.Mainnet;

    public bool Online { get; set; } = true;

    public int Lookups { get; private set; }

    /// <summary>How many times the spends were asked for from the source (not from the case).</summary>
    public int SpendLookups { get; private set; }

    /// <summary>Makes every address page point back at itself, as a misbehaving source might.</summary>
    public bool RepeatPages { get; set; }

    public void Add(params BitcoinTransaction[] transactions)
    {
        foreach (var transaction in transactions)
        {
            _chain[transaction.Txid] = transaction;
        }
    }

    public Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
        ICase @case, BitcoinAddress address, Txid? after = null, CancellationToken cancellationToken = default)
    {
        Lookups++;
        if (!Online)
        {
            return Task.FromResult(_givenPages.TryGetValue((address, after), out var page)
                ? Answer(LookupStatus.FromCaseMayBeOutdated, page, "Hämtad tidigare; kan ha ändrats.")
                : Answer<BitcoinAddressPage>(LookupStatus.Offline, null, null));
        }

        var history = _chain.Values
            .Where(tx => tx.Inputs.Any(input => input.Address == address)
                || tx.Outputs.Any(output => output.Address == address))
            .OrderByDescending(tx => tx.Block?.Height ?? long.MaxValue)
            .ThenBy(tx => tx.Txid.Value, StringComparer.Ordinal)
            .ToList();
        var start = after is null ? 0 : history.FindIndex(tx => tx.Txid == after) + 1;
        var transactions = history.Skip(start).Take(PageSize).ToList();
        var next = transactions.Count == PageSize ? transactions[^1].Txid : null;
        if (RepeatPages)
        {
            next = transactions.FirstOrDefault()?.Txid;
        }

        var result = new BitcoinAddressPage(address, after, transactions, next);
        _givenPages[(address, after)] = result;

        // As in Chains, a transaction seen on an address page is in the case.
        _givenTransactions.UnionWith(transactions.Select(tx => tx.Txid));
        return Task.FromResult(Answer(LookupStatus.Fetched, result, null));
    }

    public Task<BitcoinLookup<BitcoinTransaction>> GetTransactionAsync(
        ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
    {
        if (mode == LookupMode.CaseOnly)
        {
            return Task.FromResult(_givenTransactions.Contains(txid)
                ? Answer(LookupStatus.FromCase, _chain[txid], null)
                : Answer<BitcoinTransaction>(LookupStatus.NotInCase, null, null));
        }

        Lookups++;
        if (!Online)
        {
            return Task.FromResult(_givenTransactions.Contains(txid)
                ? Answer(LookupStatus.FromCase, _chain[txid], null)
                : Answer<BitcoinTransaction>(LookupStatus.Offline, null, null));
        }

        if (!_chain.TryGetValue(txid, out var transaction))
        {
            return Task.FromResult(Answer<BitcoinTransaction>(LookupStatus.NotFound, null, null));
        }

        _givenTransactions.Add(txid);
        return Task.FromResult(Answer(LookupStatus.Fetched, transaction, null));
    }

    public Task<BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>> GetOutputSpendsAsync(
        ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
    {
        Lookups += mode == LookupMode.CaseOnly ? 0 : 1;
        SpendLookups += mode == LookupMode.CaseOnly ? 0 : 1;
        if (!Online || mode == LookupMode.CaseOnly)
        {
            return Task.FromResult(_givenSpends.TryGetValue(txid, out var given)
                ? Answer(LookupStatus.FromCaseMayBeOutdated, given, "Hämtad tidigare; kan ha ändrats.")
                : Answer<IReadOnlyList<BitcoinOutputSpend>>(
                    Online ? LookupStatus.NotInCase : LookupStatus.Offline, null, null));
        }

        if (!_chain.TryGetValue(txid, out var transaction))
        {
            return Task.FromResult(Answer<IReadOnlyList<BitcoinOutputSpend>>(LookupStatus.NotFound, null, null));
        }

        IReadOnlyList<BitcoinOutputSpend> spends =
        [
            .. transaction.Outputs.Select(output =>
                new BitcoinOutputSpend(output.Index, SpenderOf(txid, output.Index))),
        ];
        _givenSpends[txid] = spends;
        return Task.FromResult(Answer(LookupStatus.Fetched, spends, null));
    }

    public Task<BitcoinLookup<BitcoinBlock>> GetBlockAsync(
        ICase @case, BlockHash hash, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<BitcoinLookup<BitcoinTip>> GetTipHeightAsync(
        ICase @case, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    private BitcoinSpend? SpenderOf(Txid txid, int index)
    {
        foreach (var transaction in _chain.Values)
        {
            var input = transaction.Inputs.FirstOrDefault(
                input => input.PreviousTxid == txid && input.PreviousIndex == index);
            if (input is not null)
            {
                return new BitcoinSpend(transaction.Txid, input.Index, transaction.Block);
            }
        }

        return null;
    }

    private static BitcoinLookup<T> Answer<T>(LookupStatus status, T? value, string? problem)
        where T : class => new(status, value, [], problem);
}

/// <summary>Labels addresses by hand, as Attribution will: an exchange, a bridge, a mixer.</summary>
internal sealed class FakeLabels : ITrailEndClassifier
{
    public Dictionary<BitcoinAddress, TrailEndReason> Addresses { get; } = [];

    public IReadOnlyList<TrailEndHint> ClassifyAddress(ICase @case, BitcoinAddress address) =>
        Addresses.TryGetValue(address, out var reason)
            ? [new TrailEndHint(reason, "test-labels v1", $"Märkt i testet som {reason.Describe()}.")]
            : [];

    public IReadOnlyList<TrailEndHint> ClassifyTransaction(ICase @case, BitcoinTransaction transaction) => [];
}
