using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// An Ethereum source without a network: synthetic transactions in memory, paged by block as Blockscout pages them.
/// Online it answers from the chain and remembers what it gave, as the case would; with
/// <see cref="LookupMode.CaseOnly"/> or offline it answers only what it gave before.
/// </summary>
internal sealed class FakeEthereumSource : IEthereumSource
{
    /// <summary>The page size, small so that paging is tested.</summary>
    public const int PageSize = 3;

    private readonly Dictionary<EthereumTxHash, EthereumTransaction> _chain = [];
    private readonly Dictionary<(EthereumAddress, EthereumHistory, long?), EthereumAddressPage> _givenPages = [];
    private readonly Dictionary<EthereumTxHash, EthereumTransaction> _givenTransactions = [];
    private readonly HashSet<long> _givenBlocks = [];

    public bool Online { get; set; } = true;

    /// <summary>Gets how many times the source itself was asked.</summary>
    public int Lookups { get; private set; }

    /// <summary>Whether the internal history leaves out the blocks' hashes, as Blockscout's does.</summary>
    public bool InternalWithoutHash { get; set; } = true;

    public void Add(params EthereumTransaction[] transactions)
    {
        foreach (var transaction in transactions)
        {
            _chain[transaction.Hash] = transaction;
        }
    }

    public static string HashOf(long height) =>
        "0x" + height.ToString("x", CultureInfo.InvariantCulture).PadLeft(64, 'b');

    public Task<BitcoinLookup<EthereumAddressPage>> GetAddressHistoryAsync(
        ICase @case, EthereumAddress address, EthereumHistory history, long? atOrBelow = null,
        LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
    {
        var key = (address, history, atOrBelow);
        if (mode == LookupMode.CaseOnly || !Online)
        {
            return Task.FromResult(_givenPages.TryGetValue(key, out var given)
                ? Answer(LookupStatus.FromCase, given)
                : Answer<EthereumAddressPage>(Online ? LookupStatus.NotInCase : LookupStatus.Offline, null));
        }

        Lookups++;
        var transfers = _chain.Values.SelectMany(tx => tx.Transfers)
            .Where(transfer => Kind(history, transfer.Kind) && (transfer.From == address || transfer.To == address))
            .Select(transfer => history == EthereumHistory.Internal && InternalWithoutHash
                ? transfer with { Block = transfer.Block! with { Hash = null } }
                : transfer)
            .Select(transfer => (Height: transfer.Block?.Height ?? long.MaxValue, Hash: transfer.Transaction,
                Transfer: (EthereumTransfer?)transfer, Fee: (EthereumFee?)null));
        var fees = history == EthereumHistory.Transactions
            ? _chain.Values.Where(tx => tx.From == address && tx.IsConfirmed).Select(tx =>
                (Height: tx.Block!.Height, Hash: tx.Hash, Transfer: (EthereumTransfer?)null,
                    Fee: (EthereumFee?)new EthereumFee(tx.Hash, tx.From, tx.Fee, tx.Succeeded, tx.Block)))
            : [];
        var entries = transfers.Concat(fees)
            .Where(entry => atOrBelow is null || entry.Height <= atOrBelow)
            .OrderByDescending(entry => entry.Height).ThenBy(entry => entry.Hash.Value, StringComparer.Ordinal)
            .ToList();
        var hashes = entries.Select(entry => entry.Hash).Distinct().Take(PageSize).ToHashSet();
        var onPage = entries.Where(entry => hashes.Contains(entry.Hash)).ToList();
        var full = entries.Select(entry => entry.Hash).Distinct().Count() > PageSize;
        long? next = full ? onPage.Min(entry => entry.Height) : null;
        var page = new EthereumAddressPage(
            address, history, atOrBelow, [.. onPage.Select(entry => entry.Transfer).OfType<EthereumTransfer>()],
            [.. onPage.Select(entry => entry.Fee).OfType<EthereumFee>()], next, false, false);
        _givenPages[key] = page;
        return Task.FromResult(Answer(LookupStatus.Fetched, page));
    }

    public Task<BitcoinLookup<EthereumTransaction>> GetTransactionAsync(
        ICase @case, EthereumTxHash hash, LookupMode mode = LookupMode.AsNeeded,
        CancellationToken cancellationToken = default)
    {
        if (mode == LookupMode.CaseOnly || !Online)
        {
            return Task.FromResult(_givenTransactions.TryGetValue(hash, out var given)
                ? Answer(LookupStatus.FromCase, given)
                : Answer<EthereumTransaction>(Online ? LookupStatus.NotInCase : LookupStatus.Offline, null));
        }

        Lookups++;
        if (!_chain.TryGetValue(hash, out var transaction))
        {
            return Task.FromResult(Answer<EthereumTransaction>(LookupStatus.NotFound, null));
        }

        _givenTransactions[hash] = transaction;
        return Task.FromResult(Answer(LookupStatus.Fetched, transaction));
    }

    public Task<BitcoinLookup<EthereumBlockReference>> GetBlockAsync(
        ICase @case, long height, LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
    {
        var block = new EthereumBlockReference(
            height, HashOf(height), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(12 * height));
        if (mode == LookupMode.CaseOnly || !Online)
        {
            return Task.FromResult(_givenBlocks.Contains(height)
                ? Answer(LookupStatus.FromCase, block)
                : Answer<EthereumBlockReference>(Online ? LookupStatus.NotInCase : LookupStatus.Offline, null));
        }

        Lookups++;
        _givenBlocks.Add(height);
        return Task.FromResult(Answer(LookupStatus.Fetched, block));
    }

    public Task<BitcoinLookup<EthereumTip>> GetTipHeightAsync(
        ICase @case, CancellationToken cancellationToken = default) =>
        Task.FromResult(Answer(LookupStatus.Fetched, new EthereumTip(1_000)));

    private static bool Kind(EthereumHistory history, EthereumTransferKind kind) => history switch
    {
        EthereumHistory.Transactions => kind == EthereumTransferKind.Native,
        EthereumHistory.Internal => kind == EthereumTransferKind.Internal,
        _ => kind == EthereumTransferKind.Token,
    };

    private static BitcoinLookup<T> Answer<T>(LookupStatus status, T? value)
        where T : class => new(status, value, [], null);
}
