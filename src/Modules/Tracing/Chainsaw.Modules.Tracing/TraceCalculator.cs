using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// Traces money through a graph's coins (the UTXO model) by one method. Every coin that holds traced money either
/// goes on into a transaction in the graph or ends, with a reason; every transaction is counted once, after every
/// transaction it spends from; and the ends add up to what was traced, which is checked before anything is returned.
/// The method counts in the chain's smallest unit; the result counts in the chain's coin as an
/// <see cref="AssetAmount"/>.
/// </summary>
internal sealed class TraceCalculator
{
    /// <summary>The graph.</summary>
    private readonly TraceGraph _graph;

    /// <summary>The method.</summary>
    private readonly IUtxoTracingMethod _method;

    /// <summary>The asset the coins are counted in: the graph's chain's coin.</summary>
    private readonly Asset _asset = Chain.Bitcoin.NativeAsset();

    /// <summary>What happens to each coin that has been looked at, by coin.</summary>
    private readonly Dictionary<Outpoint, Step> _steps = [];

    /// <summary>The coins that hold traced money, in the order they were reached.</summary>
    private readonly Dictionary<Outpoint, Holding> _holdings = [];

    /// <summary>Stops the trace between steps.</summary>
    private readonly CancellationToken _cancellationToken;

    /// <summary>Creates a calculator.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="method">The method.</param>
    /// <param name="cancellationToken">Stops the trace between steps.</param>
    private TraceCalculator(TraceGraph graph, IUtxoTracingMethod method, CancellationToken cancellationToken) =>
        (_graph, _method, _cancellationToken) = (graph, method, cancellationToken);

    /// <summary>Traces money through a graph.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="method">The method.</param>
    /// <param name="seeds">The coins the money starts in.</param>
    /// <param name="cancellationToken">Stops the trace between steps; a large graph can take a while.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentException">A seed is not valid (see <see cref="ITracingService.Trace"/>).</exception>
    /// <exception cref="InvalidOperationException">The method created or lost money.</exception>
    /// <exception cref="OperationCanceledException">The trace was stopped.</exception>
    public static TraceResult Trace(
        TraceGraph graph, IUtxoTracingMethod method, IReadOnlyList<TraceSeed> seeds,
        CancellationToken cancellationToken = default) =>
        new TraceCalculator(graph, method, cancellationToken).Run(seeds);

    /// <summary>Runs the trace.</summary>
    /// <param name="seeds">The seeds.</param>
    /// <returns>The result.</returns>
    private TraceResult Run(IReadOnlyList<TraceSeed> seeds)
    {
        var traced = CheckSeeds(seeds);
        var reached = Reach(seeds.Select(seed => seed.Coin));
        if (seeds.FirstOrDefault(seed => reached.Contains(seed.Coin.Txid)) is { } downstream)
        {
            throw new ArgumentException(
                $"The seed {downstream.Coin} lies downstream of another seed; it would be traced twice.",
                nameof(seeds));
        }

        foreach (var seed in seeds)
        {
            var output = Output(seed.Coin);
            _holdings.Add(seed.Coin, new Holding(
                TaintSet.FirstPart(seed.Amount is { } part ? (long)part.Units : output.Amount.Value),
                new SortedSet<string>(StringComparer.Ordinal),
                MayChange(seed.Coin.Txid),
                new SortedSet<string>(StringComparer.Ordinal)));
        }

        var ends = new List<TraceEnd>();
        var counted = new List<TracedTransaction>();
        foreach (var txid in InOrder(reached))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (Count(txid, ends) is { } transaction)
            {
                counted.Add(transaction);
            }
        }

        ends.AddRange(_holdings.Where(pair => _steps[pair.Key].End is not null)
            .Select(pair => EndOf(pair.Key, pair.Value)));
        var ended = ends.Aggregate(AssetAmount.Zero(_asset), (sum, end) => sum + end.Amount.Amount);
        if (ended != traced)
        {
            throw new InvalidOperationException(
                $"{_method.Id} ended {ended} of {traced} traced; the trace is refused.");
        }

        return new TraceResult(
            _method.Id,
            seeds,
            traced,
            [.. _holdings.Select(pair => Coin(pair.Key, pair.Value))],
            counted,
            Edges(),
            ends);
    }

    /// <summary>Checks the seeds and gives how much they trace.</summary>
    /// <param name="seeds">The seeds.</param>
    /// <returns>The sum.</returns>
    /// <exception cref="ArgumentException">A seed is not valid.</exception>
    private AssetAmount CheckSeeds(IReadOnlyList<TraceSeed> seeds)
    {
        if (seeds is null || seeds.Count == 0 || seeds.Any(seed => seed?.Coin is null))
        {
            throw new ArgumentException("A trace needs at least one seed.", nameof(seeds));
        }

        if (seeds.Select(seed => seed.Coin).Distinct().Count() != seeds.Count)
        {
            throw new ArgumentException("A coin is given as a seed twice.", nameof(seeds));
        }

        var sum = AssetAmount.Zero(_asset);
        foreach (var seed in seeds)
        {
            if (!_graph.Transactions.TryGetValue(seed.Coin.Txid, out var entry)
                || seed.Coin.Index >= entry.Transaction.Outputs.Count)
            {
                throw new ArgumentException($"The seed {seed.Coin} is not in the graph.", nameof(seeds));
            }

            if (seed.Amount is { } given && given == default)
            {
                throw new ArgumentException($"The seed {seed.Coin} has an amount without an asset.", nameof(seeds));
            }

            var whole = entry.Transaction.Outputs[seed.Coin.Index].Amount.ToAmount();
            var amount = seed.Amount ?? whole;
            if (amount.Asset != _asset || amount.Units.Sign <= 0 || amount > whole)
            {
                throw new ArgumentException(
                    $"The seed {seed.Coin} traces {amount} of a coin of {whole}.", nameof(seeds));
            }

            sum += amount;
        }

        return sum;
    }

    /// <summary>
    /// Follows the coins through the graph without amounts and gives every transaction the money can reach. Whether a
    /// coin goes on or ends depends only on the graph, so this is the set the amounts are then counted through.
    /// </summary>
    /// <param name="coins">The coins to start from.</param>
    /// <returns>The transactions reached.</returns>
    private HashSet<Txid> Reach(IEnumerable<Outpoint> coins)
    {
        var reached = new HashSet<Txid>();
        var pending = new Stack<Outpoint>(coins);
        while (pending.TryPop(out var coin))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (_steps.ContainsKey(coin))
            {
                continue;
            }

            var step = Resolve(coin);
            _steps.Add(coin, step);
            if (step.Spender is { } spender && reached.Add(spender))
            {
                var transaction = _graph.Transactions[spender].Transaction;
                foreach (var output in transaction.Outputs)
                {
                    pending.Push(new Outpoint(spender, output.Index));
                }
            }
        }

        return reached;
    }

    /// <summary>
    /// Decides what happens to a coin: which transaction in the graph it goes on into, or why it ends.
    /// </summary>
    /// <param name="coin">The coin.</param>
    /// <returns>The step.</returns>
    private Step Resolve(Outpoint coin)
    {
        if (_graph.Gone.TryGetValue(coin.Txid, out var made))
        {
            // A coin of a transaction that is not in the chain any more holds no money to follow (a seed there).
            return Step.Ends(GoneEnding(made));
        }

        var output = Output(coin);
        var links = SpendRules.Continuations(_graph, coin);
        if (links.Count > 0)
        {
            var targets = string.Join(", ", links.Select(link => link.To.ToString()));
            return Step.Ends(new Ending(
                TrailEndReason.OtherChain, null, $"Fortsätter på annan kedja, knutet för hand till {targets}.", [])
            {
                Continuations = links,
            });
        }

        var node = NodeOf(coin, output);
        if (node is not null && node.Ends.Count > 0)
        {
            return Step.Ends(new Ending(node.Ends[0].Reason, null, node.Ends[0].Explanation, node.Ends));
        }

        if (output.ScriptType == BitcoinScriptType.OpReturn)
        {
            return Step.Ends(new Ending(
                TrailEndReason.Unspendable, null, "Utgången är OP_RETURN och kan aldrig spenderas.", []));
        }

        var spend = _graph.Spends.GetValueOrDefault(coin) ?? new CoinSpend(SpendState.Unknown, null, false);
        return spend.State switch
        {
            SpendState.Unspent => Step.Ends(new Ending(
                TrailEndReason.Unspent,
                null,
                spend.MayBeOutdated
                    ? "Myntet var ospenderat när ärendet senast frågade; det kan ha flyttats sedan."
                    : "Myntet var ospenderat när ärendet frågade.",
                [])
            {
                MayBeOutdated = spend.MayBeOutdated,
            }),
            SpendState.Spent => Spent(coin, output, spend),
            _ => Step.Ends(new Ending(
                TrailEndReason.NotExpanded,
                null,
                "Vart myntet gick är inte hämtat ännu; expandera för att följa det.",
                [])),
        };
    }

    /// <summary>Decides what happens to a coin that is spent.</summary>
    /// <param name="coin">The coin.</param>
    /// <param name="output">Its output.</param>
    /// <param name="spend">The spend.</param>
    /// <returns>The step.</returns>
    private Step Spent(Outpoint coin, BitcoinOutput output, CoinSpend spend)
    {
        if (_graph.Gone.TryGetValue(spend.SpentBy!.Txid, out var gone))
        {
            // Through the replacer only by the user's choice (a judgement); otherwise the trail ends at the gone one.
            return SpendRules.Replaced(_graph, coin, spend.SpentBy) is { } by
                ? Spent(coin, output, spend with { SpentBy = by }) with { Replaced = gone.Txid }
                : Step.Ends(GoneEnding(gone) with { MayBeOutdated = spend.MayBeOutdated });
        }

        var spender = spend.SpentBy!.Txid;
        if (!_graph.Transactions.TryGetValue(spender, out var entry))
        {
            return Step.Ends(new Ending(
                TrailEndReason.NotExpanded,
                spender,
                $"Myntet spenderas i tx {spender} som inte är hämtad ännu; expandera för att följa det.",
                [])
            {
                MayBeOutdated = spend.MayBeOutdated,
            });
        }

        // Everything that rests on this spend may change when the spend or the spender is not settled.
        var mayChange = spend.MayBeOutdated || MayChange(spender);
        if (SpendRules.IsConflicting(entry.Transaction, spend.SpentBy.InputIndex, coin, output))
        {
            return Step.Ends(new Ending(
                TrailEndReason.Conflicting,
                spender,
                $"Uppgiften om att tx {spender} spenderar myntet går inte ihop med tx:en; spåret följs inte.",
                [])
            {
                MayBeOutdated = mayChange,
            });
        }

        if (SpendRules.EndsAt(_graph, spender) is { Count: > 0 } hints)
        {
            return Step.Ends(new Ending(hints[0].Reason, spender, hints[0].Explanation, hints)
            {
                MayBeOutdated = mayChange,
            });
        }

        return new Step(spender, null, mayChange);
    }

    /// <summary>
    /// Gives how the money stops at a transaction the source no longer knows (sketch GR9a): the end of the trail when
    /// the answers settle it, or else a stop that is no end, since they do not add up.
    /// </summary>
    /// <param name="gone">The transaction.</param>
    /// <returns>The ending.</returns>
    private static Ending GoneEnding(GoneTransaction gone) => gone.Hint is { } hint
        ? new(TrailEndReason.TransactionGone, gone.Txid, hint.Explanation, [hint])
        : new(TrailEndReason.GoneUncertain, gone.Txid, gone.Explain(), []);

    /// <summary>
    /// Orders the reached transactions so that each comes after every reached transaction it spends from.
    /// </summary>
    /// <param name="reached">The reached transactions.</param>
    /// <returns>The order.</returns>
    private List<Txid> InOrder(HashSet<Txid> reached)
    {
        var waitingOn = reached.ToDictionary(
            txid => txid,
            txid => _graph.Transactions[txid].Transaction.Inputs
                .Where(input => input.PreviousTxid is { } previous && reached.Contains(previous))
                .Select(input => input.PreviousTxid!)
                .ToHashSet());
        var followers = waitingOn
            .SelectMany(pair => pair.Value.Select(previous => (Previous: previous, Next: pair.Key)))
            .ToLookup(pair => pair.Previous, pair => pair.Next);
        var ready = new PriorityQueue<Txid, (long Height, string Txid)>(
            Comparer<(long Height, string Txid)>.Create((a, b) => a.Height != b.Height
                ? a.Height.CompareTo(b.Height)
                : string.CompareOrdinal(a.Txid, b.Txid)));
        foreach (var txid in waitingOn.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key))
        {
            ready.Enqueue(txid, Priority(txid));
        }

        var order = new List<Txid>(reached.Count);
        while (ready.TryDequeue(out var next, out _))
        {
            order.Add(next);
            foreach (var follower in followers[next])
            {
                var waiting = waitingOn[follower];
                if (waiting.Remove(next) && waiting.Count == 0)
                {
                    ready.Enqueue(follower, Priority(follower));
                }
            }
        }

        return order.Count == reached.Count
            ? order
            : throw new InvalidOperationException("The graph's transactions spend each other in a ring.");
    }

    /// <summary>Gives a transaction's place among those ready to be counted: by block, then by txid.</summary>
    /// <param name="txid">The transaction.</param>
    /// <returns>The priority; an unconfirmed transaction comes last.</returns>
    private (long Height, string Txid) Priority(Txid txid) =>
        (_graph.Transactions[txid].Transaction.Block?.Height ?? long.MaxValue, txid.Value);

    /// <summary>
    /// Counts the traced money through one transaction, adds its fee as an end, and gives its outputs their share.
    /// </summary>
    /// <param name="txid">The transaction.</param>
    /// <param name="ends">Where the fee's end is added.</param>
    /// <returns>How the money went through, or <see langword="null"/> when no traced money went in.</returns>
    private TracedTransaction? Count(Txid txid, List<TraceEnd> ends)
    {
        var transaction = _graph.Transactions[txid].Transaction;
        var inputs = new List<TaintSet>(transaction.Inputs.Count);
        var through = new SortedSet<string>(StringComparer.Ordinal) { txid.Value };
        var replaced = new SortedSet<string>(StringComparer.Ordinal);
        var mayChange = false;
        foreach (var input in transaction.Inputs)
        {
            var coin = new Outpoint(input.PreviousTxid!, input.PreviousIndex!.Value);
            var goesOn = _holdings.TryGetValue(coin, out var holding) && _steps[coin].Spender == txid;
            inputs.Add(goesOn ? holding!.Taint : TaintSet.None);
            if (goesOn)
            {
                through.UnionWith(holding!.Through);
                replaced.UnionWith(holding.Replaced);
                if (_steps[coin].Replaced is { } gone)
                {
                    replaced.Add(gone.Value);
                }

                mayChange |= holding.MayBeOutdated || _steps[coin].MayBeOutdated;
            }
        }

        var traced = inputs.Aggregate(0L, (sum, set) => checked(sum + set.Units));
        if (traced == 0)
        {
            return null;
        }

        var taint = _method.Propagate(MethodChecks.Flow(transaction), inputs);
        CheckConservation(transaction, traced, taint);
        if (taint.Fee != 0)
        {
            ends.Add(new TraceEnd(
                TrailEndReason.MinerFee,
                Amount(taint.Fee, through, replaced),
                null,
                txid,
                null,
                $"Avgiften i tx {txid} gick till mineraren.",
                [],
                [],
                mayChange));
        }

        for (var i = 0; i < taint.Outputs.Count; i++)
        {
            if (!taint.Outputs[i].IsEmpty)
            {
                _holdings.Add(new Outpoint(txid, i), new Holding(taint.Outputs[i], through, mayChange, replaced));
            }
        }

        return new TracedTransaction(
            txid,
            Units(traced),
            [.. taint.Outputs.Select(set => Units(set.Units))],
            Units(taint.Fee),
            mayChange || MayChange(txid));
    }

    /// <summary>Checks that a method neither created nor lost money in a transaction.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="traced">How much traced money went in.</param>
    /// <param name="taint">What the method gave.</param>
    /// <exception cref="InvalidOperationException">It did.</exception>
    private void CheckConservation(BitcoinTransaction transaction, long traced, TransactionTaint taint)
    {
        var fits = taint.Outputs.Count == transaction.Outputs.Count
            && taint.Outputs.Select((set, i) => set is not null && set.End <= transaction.Outputs[i].Amount.Value)
                .All(fits => fits)
            && taint.Fee >= 0
            && taint.Fee <= transaction.Fee.Value;
        if (!fits)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"{_method.Id} gave amounts that do not fit the outputs of {transaction.Txid}; refused."));
        }

        var given = taint.Outputs.Aggregate(taint.Fee, (sum, set) => checked(sum + set.Units));
        if (given != traced)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"{_method.Id} gave {Units(given)} out of {Units(traced)} traced into {transaction.Txid}; refused."));
        }
    }

    /// <summary>Gives the end of a coin that holds traced money and does not go on.</summary>
    /// <param name="coin">The coin.</param>
    /// <param name="holding">What it holds.</param>
    /// <returns>The end.</returns>
    private TraceEnd EndOf(Outpoint coin, Holding holding)
    {
        var ending = _steps[coin].End!;
        return new TraceEnd(
            ending.Reason,
            Amount(holding.Taint.Units, holding.Through, holding.Replaced),
            coin,
            ending.Transaction,
            EndNode(coin, ending),
            ending.Explanation,
            ending.Basis,
            ending.Continuations,
            ending.MayBeOutdated || holding.MayBeOutdated);
    }

    /// <summary>
    /// Gives the node an end stops at: the transaction's node when the trail really ends at a transaction drawn as a
    /// node of its own (for example a CoinJoin), otherwise the node that holds the coin.
    /// </summary>
    /// <param name="coin">The coin.</param>
    /// <param name="ending">How it ends.</param>
    /// <returns>The node's key, or <see langword="null"/>.</returns>
    private string? EndNode(Outpoint coin, Ending ending)
    {
        if (ending.Transaction is { } txid && ending.Reason.EndsTrail()
            && _graph.Node(NodeKeys.Transaction(txid)) is { } node)
        {
            return node.Key;
        }

        return NodeOf(coin, Output(coin))?.Key;
    }

    /// <summary>
    /// Says whether a transaction in the graph may change: it is not in a block yet, or the case's answer for it may
    /// be outdated.
    /// </summary>
    /// <param name="txid">The transaction.</param>
    /// <returns>Whether it may.</returns>
    private bool MayChange(Txid txid)
    {
        var entry = _graph.Transactions[txid];
        return entry.Transaction.Block is null || entry.Status == LookupStatus.FromCaseMayBeOutdated;
    }

    /// <summary>Gives a coin that holds traced money, as the result shows it.</summary>
    /// <param name="coin">The coin.</param>
    /// <param name="holding">What it holds.</param>
    /// <returns>The traced coin.</returns>
    private TracedCoin Coin(Outpoint coin, Holding holding)
    {
        var output = Output(coin);
        return new TracedCoin(
            coin,
            output.Address,
            NodeOf(coin, output)?.Key ?? NodeKeys.Coin(coin),
            holding.Taint,
            Amount(holding.Taint.Units, holding.Through, holding.Replaced),
            holding.MayBeOutdated);
    }

    /// <summary>
    /// Gives the traced money per edge: the coins paid along it, and the coins paid into a transaction node that go
    /// on into that transaction.
    /// </summary>
    /// <returns>The edges with traced money.</returns>
    private List<TracedEdge> Edges()
    {
        var edges = new List<TracedEdge>();
        foreach (var edge in _graph.Edges)
        {
            long sum = 0;
            var through = new SortedSet<string>(StringComparer.Ordinal);
            var replaced = new SortedSet<string>(StringComparer.Ordinal);
            var mayChange = false;
            foreach (var transfer in edge.Transfers)
            {
                if (transfer.Coin is not { } coin)
                {
                    continue;
                }

                var paidOut = coin.Txid.Value == transfer.Transaction.Id;
                if (_holdings.TryGetValue(coin, out var holding)
                    && (paidOut || IntoTransaction(_steps[coin], transfer.Transaction.Id)))
                {
                    sum = checked(sum + holding.Taint.Units);
                    through.UnionWith(holding.Through);
                    replaced.UnionWith(holding.Replaced);
                    if (!paidOut && _steps[coin].Replaced is { } gone)
                    {
                        replaced.Add(gone.Value);
                    }

                    mayChange |= holding.MayBeOutdated || _steps[coin].MayBeOutdated
                        || _steps[coin].End?.MayBeOutdated == true;
                }
            }

            if (sum != 0)
            {
                edges.Add(new TracedEdge(edge.Key, Amount(sum, through, replaced), mayChange));
            }
        }

        return edges;
    }

    /// <summary>
    /// Says whether a coin's money goes into a transaction: on into it, or ending at it (for example a CoinJoin), so
    /// that the line into a mixer carries the amount that ends there.
    /// </summary>
    /// <param name="step">What happens to the coin.</param>
    /// <param name="txid">The transaction's id.</param>
    /// <returns>Whether it does.</returns>
    private static bool IntoTransaction(Step step, string txid) =>
        step.Spender?.Value == txid
        || step.End is { Transaction: { } at } end && at.Value == txid && end.Reason.EndsTrail();

    /// <summary>Gives the node a coin belongs to.</summary>
    /// <param name="coin">The coin.</param>
    /// <param name="output">Its output.</param>
    /// <returns>The node, or <see langword="null"/>.</returns>
    private GraphNode? NodeOf(Outpoint coin, BitcoinOutput output) =>
        output.Address is { } address ? _graph.NodeOf(address) : _graph.Node(NodeKeys.Coin(coin));

    /// <summary>Gives the output a coin is.</summary>
    /// <param name="coin">The coin; its transaction is in the graph.</param>
    /// <returns>The output.</returns>
    private BitcoinOutput Output(Outpoint coin) => _graph.Transactions[coin.Txid].Transaction.Outputs[coin.Index];

    /// <summary>Gives an amount as a conclusion of this trace.</summary>
    /// <param name="units">The amount in the chain's smallest unit.</param>
    /// <param name="through">The transactions it was counted through.</param>
    /// <param name="replaced">The gone transactions whose replacer it was followed through by the user's choice.
    /// </param>
    /// <returns>The traced amount.</returns>
    private TracedAmount Amount(long units, IEnumerable<string> through, IEnumerable<string> replaced) =>
        new(Units(units), _method.Id, [.. through.Order(StringComparer.Ordinal).Select(Txid.Parse)])
        {
            Replaced = [.. replaced.Order(StringComparer.Ordinal).Select(Txid.Parse)],
        };

    /// <summary>Gives an amount in the chain's smallest unit as an amount of its coin.</summary>
    /// <param name="units">The amount.</param>
    /// <returns>The amount of <see cref="_asset"/>.</returns>
    private AssetAmount Units(long units) => new(_asset, units);

    /// <summary>What happens to a coin: the transaction it goes on into, or how it ends.</summary>
    /// <param name="Spender">The transaction in the graph it goes on into, or <see langword="null"/>.</param>
    /// <param name="End">How it ends, or <see langword="null"/>.</param>
    /// <param name="MayBeOutdated">
    /// Whether going on into <paramref name="Spender"/> may change: the spend or the transaction is not settled.
    /// </param>
    private sealed record Step(Txid? Spender, Ending? End, bool MayBeOutdated = false)
    {
        /// <summary>
        /// Gets the gone transaction whose replacer <see cref="Spender"/> is, when the coin goes on through a replacer
        /// by the user's choice (a judgement); otherwise <see langword="null"/>.
        /// </summary>
        public Txid? Replaced { get; init; }

        /// <summary>Gives a step that ends.</summary>
        /// <param name="ending">How.</param>
        /// <returns>The step.</returns>
        public static Step Ends(Ending ending) => new(null, ending);
    }

    /// <summary>How a coin ends, before the amount is known.</summary>
    /// <param name="Reason">Why.</param>
    /// <param name="Transaction">The transaction it ends at, or <see langword="null"/>.</param>
    /// <param name="Explanation">Why, in plain Swedish.</param>
    /// <param name="Basis">The hints it rests on.</param>
    private sealed record Ending(TrailEndReason Reason, Txid? Transaction, string Explanation,
        IReadOnlyList<TrailEndHint> Basis)
    {
        /// <summary>Gets the user's links to another chain.</summary>
        public IReadOnlyList<CrossChainLink> Continuations { get; init; } = [];

        /// <summary>Gets whether what it rests on may have changed.</summary>
        public bool MayBeOutdated { get; init; }
    }

    /// <summary>The traced money a coin holds and the transactions it was counted through.</summary>
    /// <param name="Taint">Which satoshi.</param>
    /// <param name="Through">The transactions, by txid.</param>
    /// <param name="MayBeOutdated">Whether a step on the way to it may change.</param>
    /// <param name="Replaced">The gone transactions whose replacer it came through, by txid.</param>
    private sealed record Holding(
        TaintSet Taint, IReadOnlySet<string> Through, bool MayBeOutdated, IReadOnlySet<string> Replaced);
}
