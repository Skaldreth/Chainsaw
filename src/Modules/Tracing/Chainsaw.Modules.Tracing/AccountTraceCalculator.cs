using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// Traces money through the account part of the graph, every asset at once. Each account whose history the graph has
/// read carries the traced money of each asset with the chosen method (<see cref="AccountChecks.Run"/> checks every
/// answer); money that reaches an account whose history is not read, or one where the trail ends (an exchange, a
/// mixer), stops there. Through a swap the traced value follows into the asset that came out by
/// <see cref="SwapValue"/>, unless the user chose that it does not (<see cref="SwapStop"/>). Since an account's
/// outgoing share depends on what came in, money can come back, and a swap feeds one asset from another, the accounts
/// and the swaps are worked through again until nothing changes.
/// </summary>
internal static class AccountTraceCalculator
{
    /// <summary>
    /// Why a trace is refused when its seeds lie in each other's flow, also across a swap: money would be counted
    /// twice.
    /// </summary>
    private const string SeedsInFlowText =
        "The seeds lie in each other's flow, so money would be counted twice; trace from one of them.";

    /// <summary>Traces the seeds.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="method">The method for accounts.</param>
    /// <param name="seeds">The movements the money starts in.</param>
    /// <param name="cancellationToken">Stops the trace between rounds.</param>
    /// <returns>The result per asset, and what the rule did at each swap.</returns>
    /// <exception cref="ArgumentException">
    /// There are no seeds, or a seed is not in the graph or is given twice.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A method broke its contract, the accounts do not settle, or the seeds lie in each other's flow.
    /// </exception>
    public static AccountTraceResult Trace(
        TraceGraph graph,
        IAccountTracingMethod method,
        IReadOnlyList<AccountSeed> seeds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(seeds);
        if (seeds.Count == 0 || seeds.Distinct().Count() != seeds.Count)
        {
            throw new ArgumentException("A trace needs seeds, each once.", nameof(seeds));
        }

        var transfers = graph.Account.Transfers;
        var seeded = seeds.Select(seed => transfers.FirstOrDefault(seed.Is)
                ?? throw new ArgumentException($"The seed {seed.Transaction} is not a movement in the graph.",
                    nameof(seeds)))
            .ToList();
        if (seeded.FirstOrDefault(transfer => transfer.From == transfer.To) is { } own)
        {
            throw new ArgumentException(
                $"The seed {own.Transaction} moves money from an account to itself; nothing leaves to be traced.",
                nameof(seeds));
        }

        var swaps = SwapSteps.Of(graph);
        var seedAssets = seeded.Select(transfer => transfer.Amount.Asset).Distinct().ToList();
        var assets = swaps.Reachable(seedAssets);
        var tracers = assets.ToDictionary(
            asset => asset,
            asset => new AssetTracer(
                graph, method, asset, [.. seeded.Where(transfer => transfer.Amount.Asset == asset)], swaps));
        Settle(tracers, swaps, graph.Account.Transfers.Count, cancellationToken);
        var conversions = Conversions(tracers, swaps, method.Id);
        Provenance(tracers, swaps, conversions);
        var traces = assets.Select(asset => tracers[asset].Result(conversions))
            .Where((trace, index) => index < seedAssets.Count || trace.Total.Units.Sign > 0)
            .ToList();
        return new AccountTraceResult(method.Id, seeds, traces)
        {
            Swaps = [.. swaps.All.Select(step => conversions.GetValueOrDefault(step.Swap)).OfType<SwapConversion>()],
        };
    }

    /// <summary>
    /// Carries the money through every asset's accounts and over every followed swap, round after round, until
    /// nothing changes.
    /// </summary>
    /// <param name="tracers">The tracers, by asset.</param>
    /// <param name="swaps">The graph's swaps.</param>
    /// <param name="transfers">How many movements the graph has, which bounds the rounds.</param>
    /// <param name="cancellationToken">Stops the trace between rounds.</param>
    /// <exception cref="InvalidOperationException">The money does not settle.</exception>
    private static void Settle(
        Dictionary<Asset, AssetTracer> tracers, SwapSteps swaps, int transfers, CancellationToken cancellationToken)
    {
        var rounds = transfers + (2 * swaps.All.Count) + 2;

        // Only the assets whose traced money changed are carried again: an asset changes by its own accounts or by a
        // swap that feeds it.
        var dirty = tracers.Keys.ToHashSet();
        for (var round = 0; dirty.Count > 0; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (round == rounds)
            {
                throw new InvalidOperationException("The traced money did not settle between the accounts.");
            }

            var next = dirty.Where(asset => tracers[asset].CarryRound()).ToHashSet();
            foreach (var step in swaps.All.Where(step => step.Follows && dirty.Contains(step.Swap.Sold.Asset)))
            {
                if (tracers.TryGetValue(step.Swap.Bought.Asset, out var bought)
                    && FeedSwap(tracers[step.Swap.Sold.Asset], bought, step))
                {
                    next.Add(step.Swap.Bought.Asset);
                }
            }

            dirty = next;
        }
    }

    /// <summary>Puts the traced value that went into a followed swap on what came out of it.</summary>
    /// <param name="sold">The tracer of the sold asset.</param>
    /// <param name="bought">The tracer of the bought asset.</param>
    /// <param name="step">The swap.</param>
    /// <returns>Whether what came out changed.</returns>
    private static bool FeedSwap(AssetTracer sold, AssetTracer bought, SwapStep step)
    {
        var traced = sold.TracedOf(step.SoldTransfers);
        return step.BoughtTransfers.Aggregate(false, (any, transfer) => bought.SwapIn(
            transfer, SwapValue.Follow(traced, step.Swap.Sold, transfer.Amount.Units)) | any);
    }

    /// <summary>Gives what the rule did at each swap the traced money reached.</summary>
    /// <param name="tracers">The settled tracers, by asset.</param>
    /// <param name="swaps">The graph's swaps.</param>
    /// <param name="method">The method.</param>
    /// <returns>The conversions, by swap.</returns>
    private static Dictionary<TokenSwap, SwapConversion> Conversions(
        Dictionary<Asset, AssetTracer> tracers, SwapSteps swaps, TracingMethodId method)
    {
        var conversions = new Dictionary<TokenSwap, SwapConversion>();
        foreach (var step in swaps.All)
        {
            if (!tracers.TryGetValue(step.Swap.Sold.Asset, out var sold)
                || sold.TracedOf(step.SoldTransfers) is not { Sign: > 0 } traced)
            {
                continue;
            }

            var bought = step.Follows && tracers.TryGetValue(step.Swap.Bought.Asset, out var tracer)
                ? tracer.SwappedInOf(step.BoughtTransfers)
                : BigInteger.Zero;
            conversions[step.Swap] = new SwapConversion(
                step.Swap,
                new AssetAmount(step.Swap.Sold.Asset, traced),
                new AssetAmount(step.Swap.Bought.Asset, bought),
                method,
                step.Stop);
        }

        return conversions;
    }

    /// <summary>
    /// Finds which swaps each movement's traced money may rest on: what came out of a followed swap rests on it and on
    /// what went in; an account's outgoing movement rests on what its earlier incoming traced movements rest on. It is
    /// generous (FIFO may have sent other money first), so that a line is drawn as uncertain whenever a swap's value
    /// following could be behind it.
    /// </summary>
    /// <param name="tracers">The settled tracers, by asset.</param>
    /// <param name="swaps">The graph's swaps.</param>
    /// <param name="conversions">What the rule did at each swap.</param>
    private static void Provenance(
        Dictionary<Asset, AssetTracer> tracers, SwapSteps swaps, Dictionary<TokenSwap, SwapConversion> conversions)
    {
        var followed = swaps.All.Where(step => step.Follows && conversions.ContainsKey(step.Swap)).ToList();
        if (followed.Count == 0)
        {
            return;
        }

        // The sets only grow, and are bounded by the swaps, so this ends.
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var step in followed)
            {
                if (!tracers.TryGetValue(step.Swap.Bought.Asset, out var bought))
                {
                    continue;
                }

                var behind = tracers[step.Swap.Sold.Asset].RestsOnOf(step.SoldTransfers);
                behind.Add(step.Swap);
                changed = step.BoughtTransfers.Aggregate(changed, (any, transfer) =>
                    bought.AddRestsOn(transfer, behind) | any);
            }

            changed = tracers.Values.Aggregate(changed, (any, tracer) => tracer.SpreadRestsOn() | any);
        }
    }

    /// <summary>
    /// The graph's swaps, each with the movements that went in and came out and whether the user chose that the
    /// value does not follow.
    /// </summary>
    private sealed class SwapSteps
    {
        /// <summary>The swap each movement went into, followed or not.</summary>
        private readonly Dictionary<EthereumTransfer, SwapStep> _soldIn = [];

        /// <summary>The movements that came out of a followed swap.</summary>
        private readonly HashSet<EthereumTransfer> _followedOut = [];

        /// <summary>Indexes the swaps.</summary>
        /// <param name="all">The swaps, in chain order.</param>
        private SwapSteps(IReadOnlyList<SwapStep> all)
        {
            All = all;
            foreach (var step in all)
            {
                foreach (var transfer in step.SoldTransfers)
                {
                    _soldIn.TryAdd(transfer, step);
                }

                if (step.Follows)
                {
                    _followedOut.UnionWith(step.BoughtTransfers);
                }
            }
        }

        /// <summary>Gets the swaps, in chain order.</summary>
        public IReadOnlyList<SwapStep> All { get; }

        /// <summary>
        /// Reads the swaps of a graph: what went in by <see cref="SwapValue.GoesIn"/>, so that a movement the user
        /// linked to another chain is never part of a swap, and what came out.
        /// </summary>
        /// <param name="graph">The graph.</param>
        /// <returns>The swaps.</returns>
        public static SwapSteps Of(TraceGraph graph) => new(
        [
            .. graph.Account.Swaps.Select(swap => new SwapStep(
                swap,
                [
                    .. graph.Account.Transfers.Where(transfer => SwapValue.GoesIn(
                        swap, AssetTransfer.OfEthereum(transfer, null), graph.CrossChainLinks)),
                ],
                [.. graph.Account.Transfers.Where(swap.IsBought)],
                SwapValue.StopOf(swap, graph.SwapStops))),
        ]);

        /// <summary>
        /// Gives the assets the traced money can reach: the seeds' assets, and every asset a followed swap turns one
        /// of them into.
        /// </summary>
        /// <param name="seedAssets">The seeds' assets, in the order of the first seed of each.</param>
        /// <returns>The assets; the seeds' first.</returns>
        public List<Asset> Reachable(IReadOnlyList<Asset> seedAssets)
        {
            var assets = seedAssets.ToList();
            for (var grew = true; grew;)
            {
                var next = All.Where(step => step.Follows && assets.Contains(step.Swap.Sold.Asset))
                    .Select(step => step.Swap.Bought.Asset)
                    .FirstOrDefault(asset => !assets.Contains(asset));
                grew = next is not null;
                if (next is not null)
                {
                    assets.Add(next);
                }
            }

            return assets;
        }

        /// <summary>Gives the swap a movement went into, followed or not, or <see langword="null"/>.</summary>
        /// <param name="transfer">The movement.</param>
        /// <returns>The swap.</returns>
        public SwapStep? SoldIn(EthereumTransfer transfer) => _soldIn.GetValueOrDefault(transfer);

        /// <summary>Says whether a movement came out of a followed swap.</summary>
        /// <param name="transfer">The movement.</param>
        /// <returns><see langword="true"/> when it did.</returns>
        public bool IsFollowedOut(EthereumTransfer transfer) => _followedOut.Contains(transfer);
    }

    /// <summary>One swap with what went in and came out.</summary>
    /// <param name="Swap">The swap.</param>
    /// <param name="SoldTransfers">The movements the account sent into it.</param>
    /// <param name="BoughtTransfers">The movements that came out of it to the account.</param>
    /// <param name="Stop">The user's choice that the value does not follow, or <see langword="null"/>.</param>
    private sealed record SwapStep(
        TokenSwap Swap,
        IReadOnlyList<EthereumTransfer> SoldTransfers,
        IReadOnlyList<EthereumTransfer> BoughtTransfers,
        SwapStop? Stop)
    {
        /// <summary>Gets whether the traced value follows through the swap.</summary>
        public bool Follows => Stop is null;
    }

    /// <summary>Traces one asset.</summary>
    private sealed class AssetTracer
    {
        /// <summary>The graph.</summary>
        private readonly TraceGraph _graph;

        /// <summary>The method.</summary>
        private readonly IAccountTracingMethod _method;

        /// <summary>The asset.</summary>
        private readonly Asset _asset;

        /// <summary>The seed movements.</summary>
        private readonly IReadOnlyList<EthereumTransfer> _seeds;

        /// <summary>The graph's swaps.</summary>
        private readonly SwapSteps _swaps;

        /// <summary>The movements of the asset between two accounts, in chain order.</summary>
        private readonly List<EthereumTransfer> _transfers;

        /// <summary>The accounts that carry money on.</summary>
        private readonly List<ChainAddress> _carriers;

        /// <summary>The traced part of each movement.</summary>
        private readonly Dictionary<EthereumTransfer, BigInteger> _traced = [];

        /// <summary>The traced part of each movement by its sender's ledger.</summary>
        private readonly Dictionary<EthereumTransfer, BigInteger> _fromLedger = [];

        /// <summary>The traced value a followed swap puts on each movement that came out of it, before it is capped.</summary>
        private readonly Dictionary<EthereumTransfer, BigInteger> _swappedIn = [];

        /// <summary>The swaps whose value following each movement's traced money may rest on.</summary>
        private readonly Dictionary<EthereumTransfer, HashSet<TokenSwap>> _restsOn = [];

        /// <summary>
        /// The movements the user linked to another chain, with their links: their traced money continues there and
        /// never enters the receiver's ledger.
        /// </summary>
        private readonly Dictionary<EthereumTransfer, List<CrossChainLink>> _linked = [];

        /// <summary>
        /// The movements that went into a swap: their traced money follows into the new asset or ends in the swap, and
        /// never enters the receiver's ledger (the pool), so that it is never counted twice.
        /// </summary>
        private readonly HashSet<EthereumTransfer> _intoSwaps = [];

        /// <summary>The traced money each account that carries it sent to fees and keeps, by the last round.</summary>
        private readonly Dictionary<ChainAddress, AccountTaint> _taints = [];

        /// <summary>Each carrying account's ledger as the method last saw it.</summary>
        private readonly Dictionary<ChainAddress, List<(EthereumTransfer? Transfer, AccountEntry Entry)>> _ledgers = [];

        /// <summary>Creates a tracer.</summary>
        /// <param name="graph">The graph.</param>
        /// <param name="method">The method.</param>
        /// <param name="asset">The asset.</param>
        /// <param name="seeds">The seed movements, all of the asset.</param>
        /// <param name="swaps">The graph's swaps.</param>
        public AssetTracer(
            TraceGraph graph,
            IAccountTracingMethod method,
            Asset asset,
            IReadOnlyList<EthereumTransfer> seeds,
            SwapSteps swaps)
        {
            (_graph, _method, _asset, _seeds, _swaps) = (graph, method, asset, seeds, swaps);
            _transfers =
            [
                .. graph.Account.Transfers.Where(transfer =>
                    transfer.Amount.Asset == asset && transfer.From != transfer.To),
            ];
            foreach (var seed in seeds)
            {
                _traced[seed] = seed.Amount.Units;
            }

            foreach (var link in graph.CrossChainLinks)
            {
                if (link.FromTransfer is { } movement && _transfers.FirstOrDefault(movement.Is) is { } transfer)
                {
                    if (!_linked.TryGetValue(transfer, out var links))
                    {
                        links = [];
                        _linked.Add(transfer, links);
                    }

                    links.Add(link);
                }
            }

            _intoSwaps.UnionWith(swaps.All.SelectMany(step => step.SoldTransfers)
                .Where(transfer => transfer.Amount.Asset == asset && !_linked.ContainsKey(transfer)));
            _carriers = Carriers();
        }

        /// <summary>Runs the method once on every carrying account's ledger.</summary>
        /// <returns>Whether a share changed.</returns>
        public bool CarryRound() => _carriers.Aggregate(false, (changed, account) => Carry(account) | changed);

        /// <summary>Gives the traced units of some movements of the asset.</summary>
        /// <param name="transfers">The movements.</param>
        /// <returns>Their sum.</returns>
        public BigInteger TracedOf(IEnumerable<EthereumTransfer> transfers) =>
            transfers.Aggregate(BigInteger.Zero, (sum, transfer) => sum + _traced.GetValueOrDefault(transfer));

        /// <summary>Gives how much of some movements' traced money came through a swap.</summary>
        /// <param name="transfers">The movements that came out of a swap.</param>
        /// <returns>Their sum.</returns>
        public BigInteger SwappedInOf(IEnumerable<EthereumTransfer> transfers) =>
            transfers.Aggregate(BigInteger.Zero, (sum, transfer) => sum + Applied(transfer));

        /// <summary>Puts the traced value a followed swap carries on a movement that came out of it.</summary>
        /// <param name="transfer">The movement.</param>
        /// <param name="units">The traced units by the rule.</param>
        /// <returns>Whether its traced part changed.</returns>
        public bool SwapIn(EthereumTransfer transfer, BigInteger units)
        {
            if (_seeds.Contains(transfer))
            {
                // A seed is traced whole by the user's choice. Traced value that reaches it through a swap would be
                // counted twice, as when one seed lies in another's flow, so the trace is refused the same way.
                return units.Sign > 0
                    ? throw new InvalidOperationException(SeedsInFlowText)
                    : false;
            }

            _swappedIn[transfer] = units;
            return Settle(transfer);
        }

        /// <summary>Gives the swaps some movements' traced money may rest on, as a new set.</summary>
        /// <param name="transfers">The movements.</param>
        /// <returns>The swaps.</returns>
        public HashSet<TokenSwap> RestsOnOf(IEnumerable<EthereumTransfer> transfers) =>
        [
            .. transfers.Where(transfer => _traced.GetValueOrDefault(transfer) > 0)
                .SelectMany(transfer => _restsOn.GetValueOrDefault(transfer) ?? []),
        ];

        /// <summary>Notes that a movement's traced money may rest on some swaps.</summary>
        /// <param name="transfer">The movement.</param>
        /// <param name="swaps">The swaps.</param>
        /// <returns>Whether the movement's set grew.</returns>
        public bool AddRestsOn(EthereumTransfer transfer, IEnumerable<TokenSwap> swaps)
        {
            if (Applied(transfer).IsZero)
            {
                return false;
            }

            if (!_restsOn.TryGetValue(transfer, out var set))
            {
                set = [];
                _restsOn[transfer] = set;
            }

            var before = set.Count;
            set.UnionWith(swaps);
            return set.Count != before;
        }

        /// <summary>
        /// Spreads the swaps through the carrying accounts: an outgoing movement with traced money may rest on every
        /// swap an earlier incoming traced movement rests on.
        /// </summary>
        /// <returns>Whether a set grew.</returns>
        public bool SpreadRestsOn()
        {
            var changed = false;
            foreach (var ledger in _ledgers.Values)
            {
                var behind = new HashSet<TokenSwap>();
                foreach (var (transfer, entry) in ledger)
                {
                    if (transfer is null || _traced.GetValueOrDefault(transfer).IsZero)
                    {
                        continue;
                    }

                    if (entry.Direction == TransferDirection.In)
                    {
                        behind.UnionWith(_restsOn.GetValueOrDefault(transfer) ?? []);
                    }
                    else if (behind.Count > 0)
                    {
                        changed |= Grow(transfer, behind);
                    }
                }
            }

            return changed;
        }

        /// <summary>Gives the asset's trace, after the money has settled.</summary>
        /// <param name="conversions">What the rule did at each swap.</param>
        /// <returns>The trace.</returns>
        /// <exception cref="InvalidOperationException">The ends do not add up to what was traced.</exception>
        public AssetTrace Result(IReadOnlyDictionary<TokenSwap, SwapConversion> conversions)
        {
            var ends = Ends(conversions);
            var traced = _seeds.Aggregate(BigInteger.Zero, (sum, seed) => sum + seed.Amount.Units);
            var swappedIn = _transfers.Where(_swaps.IsFollowedOut)
                .Aggregate(BigInteger.Zero, (sum, transfer) => sum + Applied(transfer));
            if (ends.Aggregate(BigInteger.Zero, (sum, end) => sum + end.Amount.Amount.Units) != traced + swappedIn)
            {
                throw new InvalidOperationException(SeedsInFlowText);
            }

            return new AssetTrace(new AssetAmount(_asset, traced), Edges(conversions), ends)
            {
                SwappedIn = new AssetAmount(_asset, swappedIn),
            };
        }

        /// <summary>
        /// Gives the accounts that carry money on: those whose history the graph has read and where the trail does not
        /// end.
        /// </summary>
        /// <returns>The accounts, in a stable order.</returns>
        private List<ChainAddress> Carriers() =>
        [
            .. _graph.Account.ReadAccounts
                .Where(account => _graph.NodeOf(account) is not { Ends.Count: > 0 })
                .OrderBy(account => account.Value, StringComparer.Ordinal),
        ];

        /// <summary>Gives how much of a movement's traced money a swap put there, within its amount.</summary>
        /// <param name="transfer">The movement.</param>
        /// <returns>The units.</returns>
        private BigInteger Applied(EthereumTransfer transfer) => _seeds.Contains(transfer)
            ? BigInteger.Zero
            : _traced.GetValueOrDefault(transfer) - _fromLedger.GetValueOrDefault(transfer);

        /// <summary>
        /// Sets a movement's traced part from its sender's ledger and what a swap put there, never more than its amount.
        /// </summary>
        /// <param name="transfer">The movement.</param>
        /// <returns>Whether it changed.</returns>
        private bool Settle(EthereumTransfer transfer)
        {
            var ledger = _fromLedger.GetValueOrDefault(transfer);
            var room = transfer.Amount.Units - ledger;
            var traced = ledger + BigInteger.Min(_swappedIn.GetValueOrDefault(transfer), room);
            if (_traced.GetValueOrDefault(transfer) == traced)
            {
                return false;
            }

            _traced[transfer] = traced;
            return true;
        }

        /// <summary>Adds swaps to a movement's set.</summary>
        /// <param name="transfer">The movement.</param>
        /// <param name="swaps">The swaps.</param>
        /// <returns>Whether the set grew.</returns>
        private bool Grow(EthereumTransfer transfer, IEnumerable<TokenSwap> swaps)
        {
            if (!_restsOn.TryGetValue(transfer, out var set))
            {
                set = [];
                _restsOn[transfer] = set;
            }

            var before = set.Count;
            set.UnionWith(swaps);
            return set.Count != before;
        }

        /// <summary>Runs the method on one account's ledger and records its outgoing shares.</summary>
        /// <param name="account">The account.</param>
        /// <returns>Whether a share changed.</returns>
        private bool Carry(ChainAddress account)
        {
            var entries = new List<(EthereumTransfer? Transfer, AccountEntry Entry)>();
            foreach (var transfer in _transfers.Where(transfer => Touches(transfer, account)
                && !(transfer.To.Value == account.Value && StopsBefore(transfer))))
            {
                var incoming = transfer.To.Value == account.Value;
                var traced = incoming ? _traced.GetValueOrDefault(transfer) : BigInteger.Zero;
                entries.Add((transfer, new AccountEntry(
                    AssetTransfer.OfEthereum(transfer, null),
                    incoming ? TransferDirection.In : TransferDirection.Out,
                    new AssetAmount(_asset, traced))));
            }

            if (_asset.Kind == AssetKind.Native)
            {
                foreach (var fee in _graph.Account.Fees.Where(fee => fee.Payer.Value == account.Value))
                {
                    entries.Add((null, new AccountEntry(
                        new AssetTransfer(new ChainTransactionRef(Chain.Ethereum, fee.Transaction.Value), fee.Amount,
                            null, null),
                        TransferDirection.Fee,
                        AssetAmount.Zero(_asset))));
                }
            }

            if (entries.All(entry => entry.Entry.Traced.IsZero))
            {
                _ledgers.Remove(account);
                return Record(account, entries, new AccountTaint([], AssetAmount.Zero(_asset), AssetAmount.Zero(_asset)));
            }

            var ordered = entries.OrderBy(entry => Order(entry.Transfer, entry.Entry)).ToList();
            var taint = AccountChecks.Run(_method, new AccountLedger(
                account, _asset, AssetAmount.Zero(_asset), [.. ordered.Select(entry => entry.Entry)]));
            _ledgers[account] = ordered;
            return Record(account, ordered, taint);
        }

        /// <summary>
        /// Says whether a movement's traced money stops before it reaches the receiver's ledger: it was linked to
        /// another chain, or it went into a swap.
        /// </summary>
        /// <param name="transfer">The movement.</param>
        /// <returns><see langword="true"/> when it does.</returns>
        private bool StopsBefore(EthereumTransfer transfer) =>
            _linked.ContainsKey(transfer) || _intoSwaps.Contains(transfer);

        /// <summary>Records an account's answer and says whether an outgoing share changed.</summary>
        /// <param name="account">The account.</param>
        /// <param name="entries">The ledger's entries, in the order the method saw them.</param>
        /// <param name="taint">The method's answer.</param>
        /// <returns>Whether a share changed.</returns>
        private bool Record(
            ChainAddress account, List<(EthereumTransfer? Transfer, AccountEntry Entry)> entries, AccountTaint taint)
        {
            _taints[account] = taint;
            var changed = false;
            var outgoing = entries.Where(entry => entry.Entry.Direction == TransferDirection.Out).ToList();
            for (var index = 0; index < outgoing.Count; index++)
            {
                var transfer = outgoing[index].Transfer!;
                if (_seeds.Contains(transfer))
                {
                    // A seed is traced whole by the user's choice; money from upstream that would land in it is
                    // found by the check that the ends add up.
                    continue;
                }

                _fromLedger[transfer] = index < taint.Outgoing.Count ? taint.Outgoing[index].Units : BigInteger.Zero;
                changed |= Settle(transfer);
            }

            return changed;
        }

        /// <summary>Gives where the traced money stops.</summary>
        /// <param name="conversions">What the rule did at each swap.</param>
        /// <returns>The ends.</returns>
        private List<TraceEnd> Ends(IReadOnlyDictionary<TokenSwap, SwapConversion> conversions)
        {
            var ends = new List<TraceEnd>();
            var carrying = _carriers.ToHashSet();
            foreach (var (transfer, links) in _linked.Where(pair => _traced.GetValueOrDefault(pair.Key) > 0))
            {
                var node = _graph.NodeOf(transfer.To.ToChainAddress());
                ends.Add(new TraceEnd(
                    TrailEndReason.OtherChain,
                    Amount(_traced[transfer], [transfer.Transaction], RestsOn([transfer], conversions)),
                    null,
                    null,
                    node?.Key,
                    $"Fortsätter på {links[0].To.Chain} enligt användarens länk till {links[0].To.Id}.",
                    [],
                    links,
                    transfer.Block is null));
            }

            AddSwapEnds(ends, conversions);
            var arrived = _transfers.Where(transfer => _traced.GetValueOrDefault(transfer) > 0
                    && !StopsBefore(transfer))
                .GroupBy(transfer => transfer.To.ToChainAddress());
            foreach (var group in arrived.OrderBy(group => group.Key.Value, StringComparer.Ordinal))
            {
                if (carrying.Contains(group.Key))
                {
                    continue;
                }

                var node = _graph.NodeOf(group.Key);
                var amount = Amount(
                    group.Aggregate(BigInteger.Zero, (sum, transfer) => sum + _traced[transfer]),
                    group.Select(transfer => transfer.Transaction),
                    RestsOn(group, conversions));
                var outdated = group.Any(transfer => transfer.Block is null);
                if (node is { Ends.Count: > 0 })
                {
                    var hint = node.Ends[0];
                    ends.Add(End(hint.Reason, amount, node.Key, hint.Explanation, node.Ends, outdated));
                }
                else
                {
                    var text = _graph.Account.PartlyRead.Contains(group.Key)
                        ? "Adressens historik är bara delvis läst (gränsen nåddes eller ett uppslag var ofullständigt), "
                            + "så pengarna förs inte vidare genom den; det kan saknas rörelser däremellan."
                        : "Adressens historik är inte expanderad; expandera den för att spåra vidare.";
                    ends.Add(End(TrailEndReason.NotExpanded, amount, node?.Key, text, [], outdated));
                }
            }

            foreach (var account in _carriers)
            {
                var taint = _taints.GetValueOrDefault(account);
                var key = _graph.NodeOf(account)?.Key;
                var outdated = _graph.Account.MayBeOutdated.Contains(account);
                var behind = RestsOn(_ledgers.GetValueOrDefault(account)?.Select(entry => entry.Transfer)
                    .OfType<EthereumTransfer>() ?? [], conversions);
                if (taint is { Remaining.Units.Sign: > 0 })
                {
                    ends.Add(End(TrailEndReason.Unspent, Amount(taint.Remaining.Units, [], behind), key,
                        "Ligger kvar på adressen enligt historiken i ärendet; det kan ha flyttats sedan.", [], outdated));
                }

                if (taint is { Fees.Units.Sign: > 0 })
                {
                    ends.Add(End(TrailEndReason.MinerFee, Amount(taint.Fees.Units, [], behind), key,
                        "Gick till avgifter (gas) som adressen betalade.", [], outdated));
                }
            }

            return ends;
        }

        /// <summary>
        /// Adds where the traced money went into a swap: on in the new asset when the value follows, the end of the
        /// trail when the user chose that it does not.
        /// </summary>
        /// <param name="ends">The ends, added to.</param>
        /// <param name="conversions">What the rule did at each swap.</param>
        private void AddSwapEnds(List<TraceEnd> ends, IReadOnlyDictionary<TokenSwap, SwapConversion> conversions)
        {
            var into = _intoSwaps.Where(transfer => _traced.GetValueOrDefault(transfer) > 0)
                .GroupBy(transfer => _swaps.SoldIn(transfer)!);
            foreach (var group in into.OrderBy(group => group.Key.Swap.Transaction.Value, StringComparer.Ordinal))
            {
                var step = group.Key;
                var conversion = conversions.GetValueOrDefault(step.Swap);
                var behind = RestsOn(group, conversions);
                if (conversion is not null && !behind.Contains(conversion))
                {
                    behind = [.. behind, conversion];
                }

                var amount = Amount(
                    group.Aggregate(BigInteger.Zero, (sum, transfer) => sum + _traced[transfer]),
                    [step.Swap.Transaction],
                    behind);
                var text = step.Follows
                    ? $"Gick in i en swap och följer med som {step.Swap.Bought.Asset.Symbol} enligt "
                        + $"{SwapValue.Analysis}: samma andel av det som kom ut."
                    : "Spåret slutar i swappen, enligt användarens val att värdet inte följer med.";
                ends.Add(End(
                    step.Follows ? TrailEndReason.Swapped : TrailEndReason.SwapStopped,
                    amount,
                    _graph.NodeOf(step.Swap.Account)?.Key,
                    text,
                    [],
                    group.Any(transfer => transfer.Block is null)));
            }
        }

        /// <summary>Gives what the rule did at the swaps some movements' traced money may rest on.</summary>
        /// <param name="transfers">The movements.</param>
        /// <param name="conversions">What the rule did at each swap.</param>
        /// <returns>The conversions, in chain order of the swaps.</returns>
        private IReadOnlyList<SwapConversion> RestsOn(
            IEnumerable<EthereumTransfer> transfers, IReadOnlyDictionary<TokenSwap, SwapConversion> conversions)
        {
            var swaps = RestsOnOf(transfers);
            return swaps.Count == 0
                ? []
                : [.. _swaps.All.Where(step => swaps.Contains(step.Swap))
                    .Select(step => conversions.GetValueOrDefault(step.Swap)).OfType<SwapConversion>()];
        }

        /// <summary>Makes an end.</summary>
        /// <param name="reason">Why it stops.</param>
        /// <param name="amount">How much.</param>
        /// <param name="nodeKey">The node, if it is in the graph.</param>
        /// <param name="explanation">Why, in Swedish.</param>
        /// <param name="basis">The hints it rests on.</param>
        /// <param name="outdated">Whether it may have changed.</param>
        /// <returns>The end.</returns>
        private static TraceEnd End(
            TrailEndReason reason,
            TracedAmount amount,
            string? nodeKey,
            string explanation,
            IReadOnlyList<TrailEndHint> basis,
            bool outdated) =>
            new(reason, amount, null, null, nodeKey, explanation, basis, [], outdated);

        /// <summary>Gives the traced money per edge of the graph.</summary>
        /// <param name="conversions">What the rule did at each swap.</param>
        /// <returns>The edges that carry some.</returns>
        private List<TracedEdge> Edges(IReadOnlyDictionary<TokenSwap, SwapConversion> conversions)
        {
            var edges = new List<TracedEdge>();
            foreach (var edge in _graph.Edges.Where(edge => edge.Amount.Asset == _asset))
            {
                var carried = _transfers.Where(transfer => _traced.GetValueOrDefault(transfer) > 0
                        && edge.Transfers.Any(on => Same(on, transfer)))
                    .ToList();
                if (carried.Count == 0)
                {
                    continue;
                }

                var units = carried.Aggregate(BigInteger.Zero, (sum, transfer) => sum + _traced[transfer]);
                var outdated = carried.Any(transfer => transfer.Block is null)
                    || _graph.Account.MayBeOutdated.Contains(carried[0].From.ToChainAddress());
                var amount = Amount(units, carried.Select(t => t.Transaction), RestsOn(carried, conversions));
                edges.Add(new TracedEdge(edge.Key, amount, outdated));
            }

            return edges;
        }

        /// <summary>Makes a traced amount of the asset.</summary>
        /// <param name="units">How much.</param>
        /// <param name="through">The transactions it was counted through.</param>
        /// <param name="swaps">The swaps it may rest on.</param>
        /// <returns>The amount.</returns>
        private TracedAmount Amount(
            BigInteger units, IEnumerable<EthereumTxHash> through, IReadOnlyList<SwapConversion> swaps) =>
            new(new AssetAmount(_asset, units), _method.Id, [])
            {
                ThroughOther =
                [
                    .. through.Distinct().OrderBy(hash => hash.Value, StringComparer.Ordinal)
                        .Select(hash => new ChainTransactionRef(Chain.Ethereum, hash.Value)),
                ],
                Swaps = swaps,
            };

        /// <summary>Says whether an edge's transfer is a movement.</summary>
        /// <param name="transfer">The edge's transfer.</param>
        /// <param name="movement">The movement.</param>
        /// <returns>Whether it is.</returns>
        private static bool Same(AssetTransfer transfer, EthereumTransfer movement) =>
            transfer.Movement == movement.Kind && transfer.Position == movement.Position
            && transfer.Transaction.Id == movement.Transaction.Value;

        /// <summary>Says whether a movement comes to or leaves an account.</summary>
        /// <param name="transfer">The movement.</param>
        /// <param name="account">The account.</param>
        /// <returns>Whether it does.</returns>
        private static bool Touches(EthereumTransfer transfer, ChainAddress account) =>
            transfer.From.Value == account.Value || transfer.To.Value == account.Value;

        /// <summary>
        /// Gives the order of an entry in a ledger: chain order, and in one transaction the fee after its movements.
        /// </summary>
        /// <param name="transfer">The movement, or <see langword="null"/> for a fee.</param>
        /// <param name="entry">The entry.</param>
        /// <returns>The key.</returns>
        private (long Block, long Index, string Transaction, long Position, int Kind) Order(
            EthereumTransfer? transfer, AccountEntry entry)
        {
            if (transfer is not null)
            {
                return AccountGraph.ChainOrder(transfer);
            }

            var fee = _graph.Account.Fees.First(fee => fee.Transaction.Value == entry.Transfer.Transaction.Id);
            return (fee.Block?.Height ?? long.MaxValue, fee.Block?.TransactionIndex ?? long.MaxValue,
                fee.Transaction.Value, long.MaxValue, int.MaxValue);
        }
    }
}
