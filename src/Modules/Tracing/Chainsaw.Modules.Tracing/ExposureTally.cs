using System.Numerics;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// What a reading of exposure has in common for both models: how many hops each actor lies from the subject and the
/// way there, which actors are followed, and the gathering of the result. A run tells it, at its end, where the money
/// stopped: at which services and through which transactions, on which lines, and what reached none and why. The
/// services and the rest must add up to the total exactly; a run that does otherwise is refused.
/// </summary>
internal sealed class ExposureTally
{
    /// <summary>The request.</summary>
    private readonly ExposureRequest _request;

    /// <summary>The actors with transactions an expansion held back.</summary>
    private readonly HashSet<string> _heldBack;

    /// <summary>The way back: the actor each was first reached from on its fewest hops.</summary>
    private readonly Dictionary<string, string> _parent = new(StringComparer.Ordinal);

    /// <summary>How much of the money reached each service.</summary>
    private readonly Dictionary<string, BigInteger> _reached = new(StringComparer.Ordinal);

    /// <summary>The transactions the money that reached each service went through.</summary>
    private readonly Dictionary<string, HashSet<ChainTransactionRef>> _through = new(StringComparer.Ordinal);

    /// <summary>When each transaction was, for the order of a service's transactions.</summary>
    private readonly Dictionary<ChainTransactionRef, DateTimeOffset?> _times = [];

    /// <summary>The money per line.</summary>
    private readonly Dictionary<GraphEdge, BigInteger> _flows = [];

    /// <summary>The subject's share of each swap its money's value followed through, as <em>Varför?</em> writes it.</summary>
    private readonly Dictionary<TokenSwap, string> _swapShares = [];

    /// <summary>What reached no service, per reason.</summary>
    private readonly Dictionary<ExposureRest, BigInteger> _rest = [];

    /// <summary>Starts a tally for a request.</summary>
    /// <param name="request">The request.</param>
    public ExposureTally(ExposureRequest request)
    {
        _request = request;
        _heldBack = request.Graph.HeldBack.Select(clue => clue.Anchor).ToHashSet(StringComparer.Ordinal);
        Hops[request.Subject] = 0;
    }

    /// <summary>Gets how many hops from the subject each actor the money reached lies.</summary>
    public Dictionary<string, int> Hops { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets whether the money is read out (paid) rather than in (got).</summary>
    public bool IsOut => _request.Direction == ExposureDirection.Out;

    /// <summary>Gets the subject.</summary>
    public string Subject => _request.Subject;

    /// <summary>Gets the graph.</summary>
    public TraceGraph Graph => _request.Graph;

    /// <summary>
    /// Notes that the money reached an actor from another: its hops, when fewer than before, with the way. A
    /// transaction drawn as a node of its own (a CoinJoin) adds no hop, since it is not an actor.
    /// </summary>
    /// <param name="actor">The actor reached.</param>
    /// <param name="from">The actor it came from.</param>
    public void Reach(string actor, string from)
    {
        if (actor == from || actor == Subject || !Hops.TryGetValue(from, out var near))
        {
            return;
        }

        var hops = near + (Graph.Node(actor)?.Kind == GraphNodeKind.Transaction ? 0 : 1);
        if (!Hops.TryGetValue(actor, out var known) || hops < known)
        {
            Hops[actor] = hops;
            _parent[actor] = from;
        }
    }

    /// <summary>Says whether a node is a service, where money stops and is counted.</summary>
    /// <param name="key">The node.</param>
    /// <returns><see langword="true"/> for a service.</returns>
    public bool IsService(string key) => key != Subject && _request.Services.Contains(key);

    /// <summary>
    /// Says whether the money is followed on from an actor: not the subject, not a service, an actor (an address, a
    /// cluster or a transaction drawn as a node) and nearer than the depth.
    /// </summary>
    /// <param name="key">The node.</param>
    /// <returns><see langword="true"/> when it is followed.</returns>
    public bool Follows(string key) =>
        key != Subject && !IsService(key)
        && Graph.Node(key)?.Kind is GraphNodeKind.Address or GraphNodeKind.Cluster or GraphNodeKind.Transaction
        && Hops.TryGetValue(key, out var hops) && hops < _request.Depth;

    /// <summary>Says whether the graph holds an actor's history in full: expanded, and nothing held back.</summary>
    /// <param name="key">The actor.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool IsFullyRead(string key) =>
        Graph.Node(key) is { } node
        && (node.Kind == GraphNodeKind.Transaction || node.Expansion == NodeExpansion.Expanded)
        && !_heldBack.Contains(key);

    /// <summary>Says where money that stays with an actor on the way ends, by whether its history is read in full.</summary>
    /// <param name="key">The actor.</param>
    /// <param name="whenRead">The reason when it is read in full.</param>
    /// <returns>The reason.</returns>
    public ExposureRest StaysWith(string key, ExposureRest whenRead) =>
        IsFullyRead(key) ? whenRead : ExposureRest.NotExpanded;

    /// <summary>Counts money that arrived at a node and stopped there: a service, the subject, or another end.</summary>
    /// <param name="key">The node.</param>
    /// <param name="units">How much.</param>
    /// <param name="through">The transactions it went through, for a service.</param>
    public void Arrive(string? key, BigInteger units, IEnumerable<ChainTransactionRef> through)
    {
        if (units.IsZero)
        {
            return;
        }

        if (key is not null && IsService(key))
        {
            _reached[key] = _reached.GetValueOrDefault(key) + units;
            if (!_through.TryGetValue(key, out var set))
            {
                set = [];
                _through[key] = set;
            }

            set.UnionWith(through);
            return;
        }

        Rest(key == Subject ? ExposureRest.Returned : ExposureRest.NotReached, units);
    }

    /// <summary>Counts money that reached no service for a reason.</summary>
    /// <param name="reason">The reason.</param>
    /// <param name="units">How much.</param>
    public void Rest(ExposureRest reason, BigInteger units)
    {
        if (!units.IsZero)
        {
            _rest[reason] = _rest.GetValueOrDefault(reason) + units;
        }
    }

    /// <summary>Counts money that moved along a line.</summary>
    /// <param name="edge">The line.</param>
    /// <param name="units">How much.</param>
    public void Flow(GraphEdge edge, BigInteger units)
    {
        if (!units.IsZero)
        {
            _flows[edge] = _flows.GetValueOrDefault(edge) + units;
        }
    }

    /// <summary>
    /// Notes how much of what went into a swap (read out) or came out of it (read in) was the subject's money, so that
    /// <em>Varför?</em> can give the share in numbers; a later reading replaces an earlier.
    /// </summary>
    /// <param name="swap">The swap.</param>
    /// <param name="money">The subject's money on that side, in its units.</param>
    /// <param name="whole">All of that side, in its units.</param>
    public void Swapped(TokenSwap swap, BigInteger money, BigInteger whole) =>
        _swapShares[swap] = SwapValue.Percent(money, whole);

    /// <summary>Notes when a transaction was, so that a service's transactions stand in time order.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="time">Its block's time, or <see langword="null"/> while unconfirmed.</param>
    public void When(ChainTransactionRef transaction, DateTimeOffset? time) => _times.TryAdd(transaction, time);

    /// <summary>
    /// Gathers the result. The services and the rest must add up to the total; otherwise the run created or lost money
    /// and is refused.
    /// </summary>
    /// <param name="total">All the subject paid out or got.</param>
    /// <param name="method">The method and its version.</param>
    /// <returns>The result.</returns>
    /// <exception cref="InvalidOperationException">The amounts do not add up to the total.</exception>
    public ExposureResult Result(BigInteger total, TracingMethodId method)
    {
        var counted = _reached.Values.Concat(_rest.Values).Aggregate(BigInteger.Zero, (sum, units) => sum + units);
        if (counted != total)
        {
            throw new InvalidOperationException(
                $"The exposure counted {counted} of {total} units; it created or lost money and is refused.");
        }

        var asset = _request.Asset;
        var reached = _reached.Where(pair => pair.Value.Sign > 0)
            .Select(pair => ReachOf(pair.Key, new AssetAmount(asset, pair.Value)))
            .OrderByDescending(reach => reach.Amount.Units)
            .ThenBy(reach => reach.NodeKey, StringComparer.Ordinal)
            .ToList();
        var flows = _flows.Where(pair => pair.Value.Sign > 0)
            .Select(pair => new ExposureFlow(
                pair.Key.Key, Near(pair.Key), Far(pair.Key), new AssetAmount(asset, pair.Value)))
            .OrderBy(flow => flow.EdgeKey, StringComparer.Ordinal)
            .ToList();
        var rest = _rest.Where(pair => pair.Value.Sign > 0)
            .ToDictionary(pair => pair.Key, pair => new AssetAmount(asset, pair.Value));
        return new ExposureResult(
            Subject, _request.Direction, _request.Depth, method, new AssetAmount(asset, total), reached, rest, flows,
            new Dictionary<string, int>(Hops, StringComparer.Ordinal));
    }

    /// <summary>Gives a line's end towards the subject.</summary>
    /// <param name="edge">The line.</param>
    /// <returns>The node.</returns>
    private string Near(GraphEdge edge) => IsOut ? edge.From : edge.To;

    /// <summary>Gives a line's end away from the subject.</summary>
    /// <param name="edge">The line.</param>
    /// <returns>The node.</returns>
    private string Far(GraphEdge edge) => IsOut ? edge.To : edge.From;

    /// <summary>
    /// Makes a service's row: its hops, the way from the subject with its lines, and the transactions the money that
    /// reached it went through, in time order.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <param name="amount">How much of the money reached it.</param>
    /// <returns>The row.</returns>
    private ExposureReach ReachOf(string service, AssetAmount amount)
    {
        var path = new List<string> { service };
        for (var key = service; _parent.TryGetValue(key, out var parent); key = parent)
        {
            path.Add(parent);
        }

        path.Reverse();
        var edges = path.Zip(path.Skip(1))
            .SelectMany(step => _flows.Keys.Where(edge => Near(edge) == step.First && Far(edge) == step.Second))
            .Select(edge => edge.Key)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var transactions = _through.GetValueOrDefault(service, [])
            .OrderBy(tx => _times.GetValueOrDefault(tx) ?? DateTimeOffset.MaxValue)
            .ThenBy(tx => tx.Id, StringComparer.Ordinal)
            .ToList();
        var crossed = transactions.Where(tx => tx.Chain == Chain.Ethereum).Select(tx => tx.Id)
            .ToHashSet(StringComparer.Ordinal);
        return new ExposureReach(service, amount, Hops[service], path, edges, transactions)
        {
            // The swaps whose value carried the money on: by an account on the way, in a transaction the money went
            // through, and followed.
            Swaps =
            [
                .. Graph.Account.Swaps.Where(swap => crossed.Contains(swap.Transaction.Value)
                        && Graph.NodeOf(swap.Account)?.Key is { } swapper && path.Contains(swapper)
                        && SwapValue.StopOf(swap, Graph.SwapStops) is null && _swapShares.ContainsKey(swap))
                    .Select(swap => new ExposureSwap(swap, _swapShares[swap])),
            ],
        };
    }
}
