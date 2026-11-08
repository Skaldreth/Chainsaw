using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>
/// What an address holds now (sketch IN4a): each asset that is left, in its smallest unit, the block it was read at
/// and when. A balance is a fact from the chain as the source gave it; when the source could not be asked, the case's
/// newest answer stands, and <see cref="MayBeOutdated"/> says so (principle 9).
/// </summary>
/// <param name="Held">What is left of each asset; empty when nothing is.</param>
/// <param name="Tip">The newest block when it was read, or <see langword="null"/> when it is not known.</param>
/// <param name="FetchedAt">When the balance was fetched, or <see langword="null"/> when it could not be had.</param>
/// <param name="MayBeOutdated">Whether it comes from the case because the source could not be asked.</param>
/// <param name="Problem">Why it could not be had, or what is missing, for the user; otherwise <see langword="null"/>.</param>
public sealed record NodeHoldings(
    IReadOnlyList<AssetAmount> Held, ChainTip? Tip, DateTimeOffset? FetchedAt, bool MayBeOutdated, string? Problem)
{
    /// <summary>Gets whether the balance could be had at all.</summary>
    public bool HasBalance => FetchedAt is not null;

    /// <summary>
    /// Gets what waits in the mempool, in and out together (negative when more goes out), or <see langword="null"/>
    /// when nothing does or the source cannot say; it is not in <see cref="Held"/> until it is confirmed.
    /// </summary>
    public AssetAmount? Pending { get; init; }
}

/// <summary>What the graph reads of the chains themselves from the case, without asking (sketches GR9a and TL2a).</summary>
/// <param name="Changes">The transactions that left the block the case first saw them in.</param>
/// <param name="Tip">The newest Bitcoin block the case has fetched, or <see langword="null"/>.</param>
public sealed record ChainMarksReading(IReadOnlyList<BlockChange> Changes, ChainTip? Tip)
{
    /// <summary>Gets a reading with nothing in it.</summary>
    public static ChainMarksReading Empty { get; } = new([], null);
}

/// <summary>
/// The graph's way to what the chains say about now, off the UI thread: an actor's balance and today's rate for the
/// card's back (sketch IN4a), and the block changes and the newest block for the marks and the strip (sketches GR9a
/// and TL2a). A balance and a rate are asked for through the exit and sealed in the case like every answer
/// (principles 1 and 4); the marks are read from the case only.
/// </summary>
public interface IGraphChainState
{
    /// <summary>Gets what an actor holds now, through the exit, or from the case when it cannot be asked.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="node">The actor.</param>
    /// <param name="cancellationToken">Stops the lookups.</param>
    /// <returns>The balance.</returns>
    Task<NodeHoldings> HoldingsAsync(ICase @case, GraphNode node, CancellationToken cancellationToken);

    /// <summary>
    /// Gets today's rate of some assets: the newest period that has ended, through the exit; an asset no source
    /// prices, or whose rate could not be had, is left out rather than guessed.
    /// </summary>
    /// <param name="case">The open case, where the rates are sealed.</param>
    /// <param name="assets">The assets.</param>
    /// <param name="now">The time now.</param>
    /// <param name="cancellationToken">Stops the lookups.</param>
    /// <returns>The rates, by asset.</returns>
    Task<IReadOnlyDictionary<Asset, Price>> TodayRatesAsync(
        ICase @case, IReadOnlyCollection<Asset> assets, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Reads the block changes and the newest block from the case, sending nothing.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    /// <returns>The reading.</returns>
    Task<ChainMarksReading> ReadMarksAsync(ICase @case, CancellationToken cancellationToken);
}

/// <summary>The graph's way to the chains' balances, rates and block changes (<see cref="IGraphChainState"/>).</summary>
/// <param name="bitcoin">Bitcoin's source.</param>
/// <param name="ethereum">Ethereum's source.</param>
/// <param name="prices">The rates.</param>
public sealed class GraphChainState(IBitcoinSource bitcoin, IEthereumSource ethereum, IPriceService prices)
    : IGraphChainState
{
    /// <summary>The most addresses of a cluster whose balance is asked for, so that a large one is not asked one by one.</summary>
    public const int MostAddresses = 10;

    /// <summary>What a balance says when the cluster has more addresses than are asked for.</summary>
    public const string TooManyFormat = "Klustret har {0} adresser i grafen; saldot gäller de första {1}.";

    /// <summary>What a balance says when the actor has no address whose balance can be asked for.</summary>
    public const string NoAddressText = "Noden har ingen adress vars saldo kan hämtas.";

    /// <inheritdoc/>
    public async Task<NodeHoldings> HoldingsAsync(ICase @case, GraphNode node, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(node);
        return node.Chain switch
        {
            Chain.Bitcoin => await BitcoinAsync(@case, node.GraphAddresses, cancellationToken).ConfigureAwait(false),
            Chain.Ethereum => await EthereumAsync(@case, node.Accounts, cancellationToken).ConfigureAwait(false),
            _ => new NodeHoldings([], null, null, false, NoAddressText),
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<Asset, Price>> TodayRatesAsync(
        ICase @case, IReadOnlyCollection<Asset> assets, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(assets);
        var rates = new Dictionary<Asset, Price>();
        foreach (var asset in assets)
        {
            if (await TodayRateAsync(@case, asset, now, cancellationToken).ConfigureAwait(false) is { } price)
            {
                rates[asset] = price;
            }
        }

        return rates;
    }

    /// <inheritdoc/>
    public async Task<ChainMarksReading> ReadMarksAsync(ICase @case, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var changes = await bitcoin.GetBlockChangesAsync(@case, cancellationToken).ConfigureAwait(false);
        var tip = await bitcoin.GetTipAsync(@case, LookupMode.CaseOnly, cancellationToken).ConfigureAwait(false);
        return new ChainMarksReading(changes, tip.Value);
    }

    /// <summary>
    /// Gives today's rate of one asset: the newest hour that has ended, or, when the source has none for it yet, the
    /// day before.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="asset">The asset.</param>
    /// <param name="now">The time now.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The price, or <see langword="null"/> when there is none.</returns>
    private async Task<Price?> TodayRateAsync(
        ICase @case, Asset asset, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var back in (TimeSpan[])[TimeSpan.FromHours(1), TimeSpan.FromDays(1)])
        {
            var request = new PriceRequest(asset.Chain, now - back)
            {
                Asset = asset.Kind == AssetKind.Native ? null : asset,
                Resolution = PriceResolution.Hour,
            };
            var lookup = await prices.GetPriceAsync(@case, request, PriceLookupMode.AsNeeded, cancellationToken)
                .ConfigureAwait(false);
            if (lookup.Value is { } price)
            {
                return price;
            }

            if (lookup.Status != PriceLookupStatus.NotAvailable)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Adds what is left at a cluster's addresses in the graph, at most <see cref="MostAddresses"/>: what was paid to
    /// each, less what was spent, in blocks; what waits in the mempool is not counted until it is confirmed.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="addresses">The addresses.</param>
    /// <param name="cancellationToken">Stops the lookups.</param>
    /// <returns>The balance.</returns>
    private async Task<NodeHoldings> BitcoinAsync(
        ICase @case, IReadOnlyList<BitcoinAddress> addresses, CancellationToken cancellationToken)
    {
        if (addresses.Count == 0)
        {
            return new NodeHoldings([], null, null, false, NoAddressText);
        }

        var left = Satoshi.Zero;
        var pending = AssetAmount.Zero(Asset.Bitcoin);
        DateTimeOffset? fetched = null;
        var outdated = false;
        string? problem = addresses.Count > MostAddresses
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, TooManyFormat, addresses.Count, MostAddresses)
            : null;
        foreach (var address in addresses.Take(MostAddresses))
        {
            var lookup = await bitcoin.GetAddressSummaryAsync(@case, address, cancellationToken).ConfigureAwait(false);
            if (lookup.Value is not { } summary)
            {
                return new NodeHoldings([], null, null, false, lookup.Problem ?? NoAddressText);
            }

            left += summary.Confirmed.Funded - summary.Confirmed.Spent;
            pending += summary.Mempool.Funded.ToAmount() - summary.Mempool.Spent.ToAmount();
            outdated |= lookup.Status == LookupStatus.FromCaseMayBeOutdated;
            var at = lookup.Origins.Count > 0 ? lookup.Origins[^1].ReceivedLocal : (DateTimeOffset?)null;
            fetched = Older(fetched, at);
        }

        var tip = await bitcoin.GetTipAsync(@case, cancellationToken).ConfigureAwait(false);
        AssetAmount[] held = left.Value > 0 ? [AssetAmount.Of(left)] : [];
        return new NodeHoldings(held, tip.Value, fetched, outdated, problem)
        {
            Pending = pending.IsZero ? null : pending,
        };
    }

    /// <summary>
    /// Reads what an account on Ethereum holds: its ether and the tokens whose issuer Chainsaw knows (USDT, USDC);
    /// another token is a contract's own claim, and anyone can send one to any address, so it is left out.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="accounts">The node's accounts; the first is asked.</param>
    /// <param name="cancellationToken">Stops the lookups.</param>
    /// <returns>The balance.</returns>
    private async Task<NodeHoldings> EthereumAsync(
        ICase @case, IReadOnlyList<ChainAddress> accounts, CancellationToken cancellationToken)
    {
        if (accounts.Count == 0 || !EthereumAddress.TryParse(accounts[0].Value, out var address))
        {
            return new NodeHoldings([], null, null, false, NoAddressText);
        }

        var lookup = await ethereum.GetBalanceAsync(@case, address, cancellationToken).ConfigureAwait(false);
        if (lookup.Value is not { } balance)
        {
            return new NodeHoldings([], null, null, false, lookup.Problem ?? NoAddressText);
        }

        var tip = await ethereum.GetTipAsync(@case, cancellationToken).ConfigureAwait(false);
        var held = new[] { balance.Ether }
            .Concat(balance.Tokens.Where(token => KnownTokens.IssuerOf(token.Asset) is not null))
            .Where(amount => !amount.IsZero)
            .ToList();
        var fetched = lookup.Origins.Count > 0 ? lookup.Origins[^1].ReceivedLocal : (DateTimeOffset?)null;
        var problem = balance.IsComplete ? null : string.Join(" ", lookup.Notes);
        return new NodeHoldings(
            held, tip.Value, fetched, lookup.Status == LookupStatus.FromCaseMayBeOutdated,
            string.IsNullOrWhiteSpace(problem) ? null : problem);
    }

    /// <summary>Gives the older of two times, either of which may be missing.</summary>
    /// <param name="first">The first.</param>
    /// <param name="second">The second.</param>
    /// <returns>The older, or the one there is.</returns>
    private static DateTimeOffset? Older(DateTimeOffset? first, DateTimeOffset? second)
    {
        if (first is { } a && second is { } b)
        {
            return a < b ? a : b;
        }

        return first ?? second;
    }
}
