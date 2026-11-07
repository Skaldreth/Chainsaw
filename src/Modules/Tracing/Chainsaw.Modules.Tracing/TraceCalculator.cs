using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// Traces money through a graph by one method. Every coin that holds traced money either goes on into a transaction
/// in the graph or ends, with a reason; every transaction is counted once, after every transaction it spends from;
/// and the ends add up to what was traced, which is checked before anything is returned.
/// </summary>
internal sealed class TraceCalculator
{
    /// <summary>The graph.</summary>
    private readonly TraceGraph _graph;

    /// <summary>The method.</summary>
    private readonly ITracingMethod _method;

    /// <summary>What happens to each coin that has been looked at, by coin.</summary>
    private readonly Dictionary<Outpoint, Step> _steps = [];

    /// <summary>The coins that hold traced money, in the order they were reached.</summary>
    private readonly Dictionary<Outpoint, Holding> _holdings = [];

    /// <summary>Creates a calculator.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="method">The method.</param>
    private TraceCalculator(TraceGraph graph, ITracingMethod method) => (_graph, _method) = (graph, method);

    /// <summary>Traces money through a graph.</summary>
    /// <param name="graph">The graph.</param>
    /// <param name="method">The method.</param>
    /// <param name="seeds">The coins the money starts in.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentException">A seed is not valid (see <see cref="ITracingService.Trace"/>).</exception>
    /// <exception cref="InvalidOperationException">The method created or lost money.</exception>
    public static TraceResult Trace(TraceGraph graph, ITracingMethod method, IReadOnlyList<TraceSeed> seeds) =>
        new TraceCalculator(graph, method).Run(seeds);

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
                TaintSet.FirstPart(seed.Amount ?? output.Amount),
                new SortedSet<string>(StringComparer.Ordinal),
                MayChange(seed.Coin.Txid)));
        }

        var ends = new List<TraceEnd>();
        var counted = new List<TracedTransaction>();
        foreach (var txid in InOrder(reached))
        {
            if (Count(txid, ends) is { } transaction)
            {
                counted.Add(transaction);
            }
        }

        ends.AddRange(_holdings.Where(pair => _steps[pair.Key].End is not null)
            .Select(pair => EndOf(pair.Key, pair.Value)));
        var ended = ends.Aggregate(Satoshi.Zero, (sum, end) => sum + end.Amount.Amount);
        if (ended != traced)
        {
            throw new InvalidOperationException(
                $"{_method.Id} ended {ended} BTC of {traced} BTC traced; the trace is refused.");
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
    private Satoshi CheckSeeds(IReadOnlyList<TraceSeed> seeds)
    {
        if (seeds is null || seeds.Count == 0 || seeds.Any(seed => seed?.Coin is null))
        {
            throw new ArgumentException("A trace needs at least one seed.", nameof(seeds));
        }

        if (seeds.Select(seed => seed.Coin).Distinct().Count() != seeds.Count)
        {
            throw new ArgumentException("A coin is given as a seed twice.", nameof(seeds));
        }

        var sum = Satoshi.Zero;
        foreach (var seed in seeds)
        {
            if (!_graph.Transactions.TryGetValue(seed.Coin.Txid, out var entry)
                || seed.Coin.Index >= entry.Transaction.Outputs.Count)
            {
                throw new ArgumentException($"The seed {seed.Coin} is not in the graph.", nameof(seeds));
            }

            var whole = entry.Transaction.Outputs[seed.Coin.Index].Amount;
            var amount = seed.Amount ?? whole;
            if (amount.Value <= 0 || amount.CompareTo(whole) > 0)
            {
                throw new ArgumentException(
                    $"The seed {seed.Coin} traces {amount} BTC of a coin of {whole} BTC.", nameof(seeds));
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
        var output = Output(coin);
        var links = _graph.CrossChainLinks.Where(link => link.From == coin).ToList();
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

        var inputs = entry.Transaction.Inputs;
        var index = spend.SpentBy.InputIndex;
        if (index >= inputs.Count || inputs[index].PreviousTxid != coin.Txid
            || inputs[index].PreviousIndex != coin.Index || inputs[index].Amount != output.Amount)
        {
            return Step.Ends(new Ending(
                TrailEndReason.Conflicting,
                spender,
                $"Uppgiften om att tx {spender} spenderar myntet går inte ihop med tx:en; spåret följs inte.",
                []));
        }

        if (_graph.TransactionEnds.GetValueOrDefault(spender) is { Count: > 0 } hints)
        {
            return Step.Ends(new Ending(hints[0].Reason, spender, hints[0].Explanation, hints));
        }

        return new Step(spender, null, spend.MayBeOutdated || MayChange(spender));
    }

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
        var mayChange = false;
        foreach (var input in transaction.Inputs)
        {
            var coin = new Outpoint(input.PreviousTxid!, input.PreviousIndex!.Value);
            var goesOn = _holdings.TryGetValue(coin, out var holding) && _steps[coin].Spender == txid;
            inputs.Add(goesOn ? holding!.Taint : TaintSet.None);
            if (goesOn)
            {
                through.UnionWith(holding!.Through);
                mayChange |= holding.MayBeOutdated || _steps[coin].MayBeOutdated;
            }
        }

        var traced = inputs.Aggregate(Satoshi.Zero, (sum, set) => sum + set.Amount);
        if (traced == Satoshi.Zero)
        {
            return null;
        }

        var taint = _method.Propagate(transaction, inputs);
        CheckConservation(transaction, traced, taint);
        if (taint.Fee != Satoshi.Zero)
        {
            ends.Add(new TraceEnd(
                TrailEndReason.MinerFee,
                Amount(taint.Fee, through),
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
                _holdings.Add(new Outpoint(txid, i), new Holding(taint.Outputs[i], through, mayChange));
            }
        }

        return new TracedTransaction(txid, traced, [.. taint.Outputs.Select(set => set.Amount)], taint.Fee);
    }

    /// <summary>Checks that a method neither created nor lost money in a transaction.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="traced">How much traced money went in.</param>
    /// <param name="taint">What the method gave.</param>
    /// <exception cref="InvalidOperationException">It did.</exception>
    private void CheckConservation(BitcoinTransaction transaction, Satoshi traced, TransactionTaint taint)
    {
        var fits = taint.Outputs.Count == transaction.Outputs.Count
            && taint.Outputs.Select((set, i) => set.End <= transaction.Outputs[i].Amount.Value).All(fits => fits)
            && taint.Fee.Value >= 0
            && taint.Fee.CompareTo(transaction.Fee) <= 0;
        var given = taint.Outputs.Aggregate(taint.Fee, (sum, set) => sum + set.Amount);
        if (!fits || given != traced)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"{_method.Id} gave {given} BTC out of {traced} BTC traced into {transaction.Txid}; refused."));
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
            Amount(holding.Taint.Amount, holding.Through),
            coin,
            ending.Transaction,
            NodeOf(coin, Output(coin))?.Key,
            ending.Explanation,
            ending.Basis,
            ending.Continuations,
            ending.MayBeOutdated || holding.MayBeOutdated);
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
            Amount(holding.Taint.Amount, holding.Through),
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
            var sum = Satoshi.Zero;
            var through = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var transfer in edge.Transfers)
            {
                var paidOut = transfer.Coin.Txid == transfer.Txid;
                if (_holdings.TryGetValue(transfer.Coin, out var holding)
                    && (paidOut || IntoTransaction(_steps[transfer.Coin], transfer.Txid)))
                {
                    sum += holding.Taint.Amount;
                    through.UnionWith(holding.Through);
                }
            }

            if (sum != Satoshi.Zero)
            {
                edges.Add(new TracedEdge(edge.Key, Amount(sum, through)));
            }
        }

        return edges;
    }

    /// <summary>
    /// Says whether a coin's money goes into a transaction: on into it, or ending at it (for example a CoinJoin), so
    /// that the line into a mixer carries the amount that ends there.
    /// </summary>
    /// <param name="step">What happens to the coin.</param>
    /// <param name="txid">The transaction.</param>
    /// <returns>Whether it does.</returns>
    private static bool IntoTransaction(Step step, Txid txid) =>
        step.Spender == txid || step.End is { Transaction: { } at } end && at == txid && end.Reason.EndsTrail();

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
    /// <param name="amount">The amount.</param>
    /// <param name="through">The transactions it was counted through.</param>
    /// <returns>The traced amount.</returns>
    private TracedAmount Amount(Satoshi amount, IEnumerable<string> through) =>
        new(amount, _method.Id, [.. through.Order(StringComparer.Ordinal).Select(Txid.Parse)]);

    /// <summary>What happens to a coin: the transaction it goes on into, or how it ends.</summary>
    /// <param name="Spender">The transaction in the graph it goes on into, or <see langword="null"/>.</param>
    /// <param name="End">How it ends, or <see langword="null"/>.</param>
    /// <param name="MayBeOutdated">
    /// Whether going on into <paramref name="Spender"/> may change: the spend or the transaction is not settled.
    /// </param>
    private sealed record Step(Txid? Spender, Ending? End, bool MayBeOutdated = false)
    {
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
    private sealed record Holding(TaintSet Taint, IReadOnlySet<string> Through, bool MayBeOutdated);
}
