using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// The cross-chain search (<see cref="ICrossChainLookup"/>): reads the first page of history of each recipient a
/// swap's memo names on another chain, through the chain's source (which seals every answer with its hash and goes
/// only through the exit, principles 1 and 4), and the case's rates for the payments the analysis weighs. Only the
/// address is sent; the case never is. What the case already has is read from it first; a recipient is asked of the
/// source again only after <see cref="MinimumInterval"/>, so that a graph shown again and again does not ask again.
/// </summary>
/// <param name="bitcoin">Bitcoin's source.</param>
/// <param name="ethereum">Ethereum's source.</param>
/// <param name="prices">The rates, or <see langword="null"/> when Pricing is not registered.</param>
/// <param name="tron">TRON's source, or <see langword="null"/> where TRON is not registered.</param>
/// <param name="litecoin">Litecoin's source, or <see langword="null"/> where it is not registered.</param>
/// <param name="bsc">BSC's source, or <see langword="null"/> where it is not registered.</param>
/// <param name="clock">The clock, for how old an answer is; the system's when <see langword="null"/>.</param>
internal sealed class CrossChainLookup(
    IBitcoinSource bitcoin,
    IEthereumSource ethereum,
    IPriceService? prices = null,
    ITronSource? tron = null,
    ILitecoinSource? litecoin = null,
    IBscSource? bsc = null,
    IClock? clock = null)
    : ICrossChainLookup
{
    /// <summary>
    /// The most recipients asked of a source in one search, so that many swaps do not ask too much; those the case
    /// already has are read from it and do not count.
    /// </summary>
    public const int MostTargets = 8;

    /// <summary>The most payments valued in one search, the nearest in time to their departure first.</summary>
    public const int MostValued = 200;

    /// <summary>The most rates asked of the source in one search; the rest wait for the next.</summary>
    public const int MostRateLookups = 24;

    /// <summary>
    /// How long a recipient's sealed history counts as fresh: within it the source is not asked again, however often
    /// the checks run.
    /// </summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(15);

    /// <summary>The histories of an account read for payments to it: ether, ether from contracts, and tokens.</summary>
    private static readonly EthereumHistory[] AccountHistories =
        [EthereumHistory.Transactions, EthereumHistory.Internal, EthereumHistory.Tokens];

    /// <summary>The clock.</summary>
    private readonly IClock _clock = clock ?? new SystemClock();

    /// <inheritdoc/>
    public async Task<CrossChainArrivals> FindAsync(
        ICase @case, TraceGraph graph, LookupMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(graph);
        var payments = CrossChainPayments.All(graph);
        var departures = CrossChainPayments.DeparturesOf(graph, payments, CrossChainCheck.Identity.ToString());
        if (departures.Count == 0)
        {
            return CrossChainArrivals.Empty;
        }

        var arrivals = new List<ChainMovement>();
        var targets = new List<CrossChainTarget>();
        var budget = new FetchBudget(mode, MostTargets);
        var wanted = departures.Select(CrossChainCheck.NamedRecipient)
            .OfType<ChainAddress>()
            .Distinct()
            .Where(address => !IsReadInGraph(graph, address));
        foreach (var address in wanted)
        {
            var (found, target) = await ReadAsync(@case, address, budget, cancellationToken).ConfigureAwait(false);
            arrivals.AddRange(found);
            targets.Add(target);
        }

        var known = payments.Select(payment => payment.Key).ToHashSet(StringComparer.Ordinal);
        var fresh = arrivals.Where(arrival => known.Add(arrival.Key)).ToList();
        var (values, currency) = await ValueAsync(
            @case, departures, [.. payments, .. fresh], mode, cancellationToken).ConfigureAwait(false);
        return new CrossChainArrivals(fresh, targets) { Values = values, Currency = currency };
    }

    /// <summary>
    /// Says whether the graph already holds an address's history (an expansion of it), so that it is not read again.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool IsReadInGraph(TraceGraph graph, ChainAddress address) =>
        graph.Account.ReadAccounts.Contains(address)
        || graph.Expansions.Any(expansion => expansion.Address?.ToChainAddress() == address);

    /// <summary>Reads the first page of a recipient's history and gives the payments to it.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The recipient.</param>
    /// <param name="budget">Whether, and how often more, a source may be asked.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The payments to it, and whether it was read.</returns>
    private async Task<(IReadOnlyList<ChainMovement> Found, CrossChainTarget Target)> ReadAsync(
        ICase @case, ChainAddress address, FetchBudget budget, CancellationToken cancellationToken)
    {
        try
        {
            if (address.Chain.Model() == LedgerModel.Utxo)
            {
                if (CoinSourceOf(address.Chain) is not { } coins)
                {
                    return ([], NoSource(address));
                }

                var recipient = BitcoinAddress.Parse(address.Value);
                var page = await LookUpAsync(
                    address,
                    budget,
                    mode => coins.GetAddressTransactionsAsync(@case, recipient, null, mode, cancellationToken))
                    .ConfigureAwait(false);
                var paid = page.Value?.Transactions.SelectMany(CrossChainPayments.CoinsOf) ?? [];
                return ([.. paid.Where(coin => coin.To == address)], TargetOf(address, page.Status, page.Problem));
            }

            return SourceOf(address.Chain) is { } source
                ? await ReadAccountAsync(@case, source, address, budget, cancellationToken).ConfigureAwait(false)
                : ([], NoSource(address));
        }
        catch (NotSupportedException)
        {
            // A source that cannot read only the case in this mode has nothing here yet.
            return ([], new CrossChainTarget(address, CrossChainTargetState.NotFetched, null));
        }
    }

    /// <summary>Says that Chainsaw has no source for a recipient's chain here.</summary>
    /// <param name="address">The recipient.</param>
    /// <returns>The target.</returns>
    private static CrossChainTarget NoSource(ChainAddress address) => new(
        address, CrossChainTargetState.Unreachable, $"Chainsaw har ingen källa för {address.Chain} här.");

    /// <summary>
    /// Reads an account's ether, ether from contracts (where a bridge's router pays out) and token transfers, and gives
    /// the payments to it.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="source">The chain's source.</param>
    /// <param name="address">The recipient.</param>
    /// <param name="budget">Whether, and how often more, a source may be asked.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The payments to it, and whether it was read.</returns>
    private async Task<(IReadOnlyList<ChainMovement> Found, CrossChainTarget Target)> ReadAccountAsync(
        ICase @case, IAccountSource source, ChainAddress address, FetchBudget budget,
        CancellationToken cancellationToken)
    {
        var account = EthereumAddress.Parse(address.Chain, address.Value);
        var found = new List<ChainMovement>();
        CrossChainTarget? target = null;
        foreach (var history in AccountHistories)
        {
            var page = await LookUpAsync(
                address,
                budget,
                mode => source.GetAddressHistoryAsync(@case, account, history, null, mode, cancellationToken))
                .ConfigureAwait(false);
            found.AddRange((page.Value?.Transfers ?? [])
                .Where(transfer => transfer.To == account)
                .Select(CrossChainPayments.Movement));

            // The address counts as read only when every history is; the first that is not says why.
            var read = TargetOf(address, page.Status, page.Problem);
            target = target is { State: not CrossChainTargetState.Read } ? target : read;
        }

        return (found, target!);
    }

    /// <summary>
    /// Reads one answer from the case first, and asks the source only when the search may, the case's answer is
    /// missing or older than <see cref="MinimumInterval"/>, and the budget allows the recipient.
    /// </summary>
    /// <typeparam name="T">What is looked up.</typeparam>
    /// <param name="address">The recipient, which the budget counts.</param>
    /// <param name="budget">Whether, and how often more, a source may be asked.</param>
    /// <param name="ask">Asks in a mode.</param>
    /// <returns>The answer.</returns>
    private async Task<BitcoinLookup<T>> LookUpAsync<T>(
        ChainAddress address, FetchBudget budget, Func<LookupMode, Task<BitcoinLookup<T>>> ask)
        where T : class
    {
        var stored = await ask(LookupMode.CaseOnly).ConfigureAwait(false);
        if (!budget.MayFetch || (stored.HasValue && IsFresh(stored)) || !budget.TryTake(address))
        {
            return stored;
        }

        return await ask(LookupMode.AsNeeded).ConfigureAwait(false);
    }

    /// <summary>Says whether a sealed answer is newer than <see cref="MinimumInterval"/>.</summary>
    /// <typeparam name="T">What was looked up.</typeparam>
    /// <param name="lookup">The answer.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private bool IsFresh<T>(BitcoinLookup<T> lookup)
        where T : class =>
        lookup.Origins.Count > 0
        && lookup.Origins.Max(origin => origin.ReceivedUtc) >= _clock.UtcNow - MinimumInterval;

    /// <summary>Gives a UTXO chain's source, or <see langword="null"/> when Chainsaw has none here.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>The source.</returns>
    private IBitcoinSource? CoinSourceOf(Chain chain) => chain switch
    {
        Chain.Bitcoin => bitcoin,
        Chain.Litecoin => litecoin,
        _ => null,
    };

    /// <summary>Gives an account chain's source, or <see langword="null"/> when Chainsaw has none here.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>The source.</returns>
    private IAccountSource? SourceOf(Chain chain) => chain switch
    {
        Chain.Ethereum => ethereum,
        Chain.Tron => tron,
        Chain.Bsc => bsc,
        _ => null,
    };

    /// <summary>Says what a lookup's answer means for a recipient: read, not fetched yet, or out of reach.</summary>
    /// <param name="address">The recipient.</param>
    /// <param name="status">The answer's status.</param>
    /// <param name="problem">The answer's problem, or <see langword="null"/>.</param>
    /// <returns>The target.</returns>
    private static CrossChainTarget TargetOf(ChainAddress address, LookupStatus status, string? problem) =>
        status switch
        {
            LookupStatus.Fetched or LookupStatus.FromCase or LookupStatus.FromCaseMayBeOutdated
                or LookupStatus.NotFound => new CrossChainTarget(address, CrossChainTargetState.Read, null),
            LookupStatus.NotInCase => new CrossChainTarget(address, CrossChainTargetState.NotFetched, null),
            LookupStatus.Offline => new CrossChainTarget(
                address,
                CrossChainTargetState.Unreachable,
                problem ?? "Utgången är inte ansluten, eller kedjan är avstängd under Källor."),
            _ => new CrossChainTarget(
                address, CrossChainTargetState.Unreachable, problem ?? "Källan svarade inte som väntat."),
        };

    /// <summary>
    /// Values each departure, and the payments on other chains in a departure's window (nearest first, at most
    /// <see cref="MostValued"/>), by the case's rates; with <see cref="LookupMode.AsNeeded"/> a rate the case lacks is
    /// asked of the source, at most <see cref="MostRateLookups"/>, and none more once it asks Chainsaw to wait.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="departures">The departures.</param>
    /// <param name="pool">Every payment known.</param>
    /// <param name="mode">Whether the rate source may be asked.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The values by payment's key, and their currency.</returns>
    private async Task<(Dictionary<string, decimal> Values, string? Currency)> ValueAsync(
        ICase @case,
        IReadOnlyList<CrossChainDeparture> departures,
        IReadOnlyList<ChainMovement> pool,
        LookupMode mode,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (prices is null)
        {
            return (values, null);
        }

        var asking = new RateAsking(mode == LookupMode.AsNeeded);
        string? currency = null;
        async Task Value(ChainMovement payment)
        {
            if (!values.ContainsKey(payment.Key)
                && await ValueOfAsync(@case, payment, asking, cancellationToken).ConfigureAwait(false) is { } valued
                && (currency is null || currency == valued.Currency))
            {
                currency = valued.Currency;
                values[payment.Key] = valued.Value;
            }
        }

        foreach (var departure in departures)
        {
            await Value(departure.Payment).ConfigureAwait(false);
        }

        // Only a payment that can be weighed is valued: one in the window of a departure whose value is known, or one
        // to the recipient a memo names (weighed also without a value).
        foreach (var payment in InWindows(departures, pool, values))
        {
            await Value(payment).ConfigureAwait(false);
        }

        return (values, currency);
    }

    /// <summary>
    /// Gives the payments that can be weighed against a departure, the nearest in time first: on another chain, within
    /// its window, and either the departure's value is known or the payment goes to the recipient its memo names.
    /// </summary>
    /// <param name="departures">The departures.</param>
    /// <param name="pool">Every payment known.</param>
    /// <param name="values">The values known so far, by payment's key.</param>
    /// <returns>At most <see cref="MostValued"/> payments.</returns>
    private static IEnumerable<ChainMovement> InWindows(
        IReadOnlyList<CrossChainDeparture> departures,
        IReadOnlyList<ChainMovement> pool,
        IReadOnlyDictionary<string, decimal> values) =>
        departures
            .Where(departure => departure.Payment.Time is not null)
            .SelectMany(departure => pool
                .Where(payment => payment.Transaction.Chain != departure.Payment.Transaction.Chain
                    && payment.Time is { } time
                    && CrossChainCheck.TimeScore(time - departure.Payment.Time!.Value) > 0
                    && (values.ContainsKey(departure.Payment.Key)
                        || payment.To == CrossChainCheck.NamedRecipient(departure)))
                .Select(payment => (Payment: payment, Distance: (payment.Time!.Value - departure.Payment.Time!.Value)
                    .Duration())))
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Payment.Key, StringComparer.Ordinal)
            .Select(item => item.Payment)
            .DistinctBy(payment => payment.Key)
            .Take(MostValued);

    /// <summary>Values one payment at its block's time.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="payment">The payment.</param>
    /// <param name="asking">How many rates may still be asked of the source.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The value and its currency, or <see langword="null"/> when there is no rate.</returns>
    private async Task<(decimal Value, string Currency)?> ValueOfAsync(
        ICase @case, ChainMovement payment, RateAsking asking, CancellationToken cancellationToken)
    {
        if (payment.Time is not { } at)
        {
            return null;
        }

        var asset = PricedAs(payment.Amount.Asset);
        var lookup = await LookUpAsync(@case, asset, at, PriceLookupMode.CaseOnly, cancellationToken)
            .ConfigureAwait(false);
        if (lookup.Value is null && lookup.Status == PriceLookupStatus.NotInCase && asking.MayAsk)
        {
            asking.Asked++;
            lookup = await LookUpAsync(@case, asset, at, PriceLookupMode.AsNeeded, cancellationToken)
                .ConfigureAwait(false);
            asking.Throttled |= lookup.IsThrottled;
        }

        if (lookup.Value is not { } price)
        {
            return null;
        }

        try
        {
            return (price.ValueOf(new AssetAmount(asset, payment.Amount.Units)).Value, price.Currency.Code());
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>Asks the rates for an asset at a time, and says a refusal as a failed lookup.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="asset">The asset.</param>
    /// <param name="at">The time.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The lookup.</returns>
    private async Task<PriceLookup<Price>> LookUpAsync(
        ICase @case, Asset asset, DateTimeOffset at, PriceLookupMode mode, CancellationToken cancellationToken)
    {
        try
        {
            return await prices!.GetPriceAsync(
                    @case, new PriceRequest(asset.Chain, at) { Asset = asset }, mode, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or CaseException)
        {
            return new PriceLookup<Price>(PriceLookupStatus.Failed, null, [], null);
        }
    }

    /// <summary>Gives the asset a rate is read for: wrapped ether as ether, since the two are worth the same.</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>The asset to price.</returns>
    private static Asset PricedAs(Asset asset) => asset == KnownTokens.EthereumWeth ? Asset.Ether : asset;

    /// <summary>How many recipients one search may still ask of a source.</summary>
    /// <param name="mode">The search's mode; only <see cref="LookupMode.AsNeeded"/> may ask at all.</param>
    /// <param name="most">The most recipients asked.</param>
    private sealed class FetchBudget(LookupMode mode, int most)
    {
        /// <summary>The recipients already asked, which may be asked again for their other histories.</summary>
        private readonly HashSet<ChainAddress> _asked = [];

        /// <summary>Gets whether the search may ask a source at all.</summary>
        public bool MayFetch => mode == LookupMode.AsNeeded;

        /// <summary>Takes a recipient from the budget, or says it was taken already.</summary>
        /// <param name="address">The recipient.</param>
        /// <returns><see langword="true"/> when it may be asked.</returns>
        public bool TryTake(ChainAddress address) =>
            _asked.Contains(address) || (_asked.Count < most && _asked.Add(address));
    }

    /// <summary>How many rates one search may still ask of the source.</summary>
    /// <param name="mayFetch">Whether the search may ask the source at all.</param>
    private sealed class RateAsking(bool mayFetch)
    {
        /// <summary>Gets or sets how many rates were asked.</summary>
        public int Asked { get; set; }

        /// <summary>Gets or sets whether the source asked Chainsaw to wait.</summary>
        public bool Throttled { get; set; }

        /// <summary>Gets whether one more rate may be asked.</summary>
        public bool MayAsk => mayFetch && !Throttled && Asked < MostRateLookups;
    }
}
