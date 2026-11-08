using System.Numerics;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// The analysis <em>exposure v1</em> (sketch EP1b). Every actor is read as a wallet: its transfers of one asset in time
/// order, the incoming before the outgoing within the same moment. The registered account method of the chosen kind
/// (FIFO or haircut) says, actor by actor, how much of the subject's money goes on with each transfer:
/// <list type="bullet">
/// <item>Paid out: the subject's outgoing transfers are the money; at each actor on the way the method carries what
/// came in from the subject's money onto what goes out.</item>
/// <item>Got: the subject's incoming transfers are the money; at each actor on the way the method is run once for each
/// incoming transfer, which says how much of what the actor sent on came from it.</item>
/// </list>
/// The money stops at a service (counted), at an actor beyond the depth or one that is not an actor (not reached), back
/// at the subject (returned), and stays with an actor (out) or came before the graph's history (in). Every amount is a
/// conclusion; the method's checks refuse one that creates or loses money.
/// </summary>
internal sealed class ExposureAnalysis : IExposureAnalysis
{
    /// <summary>The account methods, by kind; the newest version of each.</summary>
    private readonly Dictionary<TracingMethodKind, IAccountTracingMethod> _methods;

    /// <summary>Creates the analysis with the registered methods.</summary>
    /// <param name="methods">The tracing methods; the account model's are used.</param>
    public ExposureAnalysis(IEnumerable<ITracingMethod> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        _methods = methods.OfType<IAccountTracingMethod>()
            .GroupBy(method => method.Id.Kind)
            .ToDictionary(group => group.Key, group => group.MaxBy(method => method.Id.Version)!);
    }

    /// <inheritdoc/>
    public ExposureResult Analyse(ExposureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_methods.TryGetValue(request.Method, out var method))
        {
            throw new InvalidOperationException($"No account method {request.Method} is registered.");
        }

        return new ExposureRun(request, method).Run();
    }
}

/// <summary>One transfer of the followed asset between two actors, as an entry in both actors' ledgers.</summary>
/// <param name="Edge">The line it is on.</param>
/// <param name="Transfer">The transfer, a fact from the chain.</param>
internal sealed record ExposureEntry(GraphEdge Edge, AssetTransfer Transfer)
{
    /// <summary>Gets the sender's node.</summary>
    public string From => Edge.From;

    /// <summary>Gets the receiver's node.</summary>
    public string To => Edge.To;

    /// <summary>Gets the amount in the asset's smallest unit.</summary>
    public BigInteger Units => Transfer.Amount.Units;
}

/// <summary>
/// One reading of a subject's exposure: the ledgers, what of the money is on each transfer, and the actors read in
/// order of their hops. Not shared; made for one request.
/// </summary>
internal sealed class ExposureRun
{
    /// <summary>
    /// How many times an actor is read again when more of the money comes to it later, for example in a circle; the
    /// little that may still arrive after that is counted where it stays.
    /// </summary>
    private const int MaxRuns = 8;

    /// <summary>The request.</summary>
    private readonly ExposureRequest _request;

    /// <summary>The method.</summary>
    private readonly IAccountTracingMethod _method;

    /// <summary>Each actor's incoming transfers.</summary>
    private readonly Dictionary<string, List<ExposureEntry>> _in = new(StringComparer.Ordinal);

    /// <summary>Each actor's outgoing transfers.</summary>
    private readonly Dictionary<string, List<ExposureEntry>> _out = new(StringComparer.Ordinal);

    /// <summary>How much of the subject's money is on each transfer.</summary>
    private readonly Dictionary<ExposureEntry, BigInteger> _money = [];

    /// <summary>How many hops from the subject each actor the money reached lies.</summary>
    private readonly Dictionary<string, int> _hops = new(StringComparer.Ordinal);

    /// <summary>The actor each was first reached from on its fewest hops, for the way back.</summary>
    private readonly Dictionary<string, string> _parent = new(StringComparer.Ordinal);

    /// <summary>How many times each actor was read.</summary>
    private readonly Dictionary<string, int> _runs = new(StringComparer.Ordinal);

    /// <summary>What of the money stayed at each actor read: in its wallet, or before the graph's history.</summary>
    private readonly Dictionary<string, BigInteger> _left = new(StringComparer.Ordinal);

    /// <summary>The actors waiting to be read, the nearest first.</summary>
    private readonly PriorityQueue<string, int> _queue = new();

    /// <summary>The actors in the queue, so that one is queued once however much reaches it before it is read.</summary>
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);

    /// <summary>Prepares a reading.</summary>
    /// <param name="request">The request.</param>
    /// <param name="method">The method.</param>
    public ExposureRun(ExposureRequest request, IAccountTracingMethod method)
    {
        _request = request;
        _method = method;
        foreach (var edge in request.Graph.Edges.Where(IsFollowed))
        {
            foreach (var transfer in edge.Transfers.Where(t => t.Amount.Asset == request.Asset))
            {
                var entry = new ExposureEntry(edge, transfer);
                ListOf(_out, edge.From).Add(entry);
                ListOf(_in, edge.To).Add(entry);
            }
        }
    }

    /// <summary>Gets whether the money is read out (paid) rather than in (got).</summary>
    private bool IsOut => _request.Direction == ExposureDirection.Out;

    /// <summary>Reads the exposure.</summary>
    /// <returns>The result.</returns>
    public ExposureResult Run()
    {
        var subject = _request.Subject;
        _hops[subject] = 0;
        var total = BigInteger.Zero;
        foreach (var entry in Arriving(subject, fromSubject: true))
        {
            _money[entry] = entry.Units;
            total += entry.Units;
            Reach(Far(entry), subject);
        }

        while (_queue.TryDequeue(out var actor, out _))
        {
            Read(actor);
        }

        return Result(total);
    }

    /// <summary>Says whether a line carries transfers the analysis follows: a transfer between two actors.</summary>
    /// <param name="edge">The line.</param>
    /// <returns><see langword="true"/> when it is followed.</returns>
    private bool IsFollowed(GraphEdge edge) =>
        edge.Kind == GraphEdgeKind.Transfer && edge.Amount.Asset == _request.Asset
        && !string.Equals(edge.From, edge.To, StringComparison.Ordinal);

    /// <summary>Gives the list of a key, making it when it is not there.</summary>
    /// <param name="lists">The lists.</param>
    /// <param name="key">The key.</param>
    /// <returns>The list.</returns>
    private static List<ExposureEntry> ListOf(Dictionary<string, List<ExposureEntry>> lists, string key)
    {
        if (!lists.TryGetValue(key, out var list))
        {
            list = [];
            lists[key] = list;
        }

        return list;
    }

    /// <summary>Gives the transfers of an actor, or none.</summary>
    /// <param name="lists">The lists.</param>
    /// <param name="key">The actor.</param>
    /// <returns>The transfers.</returns>
    private static IReadOnlyList<ExposureEntry> Of(Dictionary<string, List<ExposureEntry>> lists, string key) =>
        lists.TryGetValue(key, out var list) ? list : [];

    /// <summary>
    /// Gives the transfers through which the money reaches an actor: its incoming when read out, its outgoing when
    /// read in. For the subject, with <paramref name="fromSubject"/>, the transfers that are the money itself.
    /// </summary>
    /// <param name="actor">The actor.</param>
    /// <param name="fromSubject">Whether the subject's own money is asked for.</param>
    /// <returns>The transfers.</returns>
    private IReadOnlyList<ExposureEntry> Arriving(string actor, bool fromSubject = false) =>
        IsOut != fromSubject ? Of(_in, actor) : Of(_out, actor);

    /// <summary>Gives the transfers an actor passes the money on through: away from the subject.</summary>
    /// <param name="actor">The actor.</param>
    /// <returns>The transfers.</returns>
    private IReadOnlyList<ExposureEntry> Carrying(string actor) => IsOut ? Of(_out, actor) : Of(_in, actor);

    /// <summary>Gives a transfer's end away from the subject.</summary>
    /// <param name="entry">The transfer.</param>
    /// <returns>The node.</returns>
    private string Far(ExposureEntry entry) => IsOut ? entry.To : entry.From;

    /// <summary>Gives a transfer's end towards the subject.</summary>
    /// <param name="entry">The transfer.</param>
    /// <returns>The node.</returns>
    private string Near(ExposureEntry entry) => IsOut ? entry.From : entry.To;

    /// <summary>
    /// Notes that the money reached an actor from another: its hops (fewer than before wins, with the way), and a
    /// reading when it is one the analysis follows.
    /// </summary>
    /// <param name="actor">The actor reached.</param>
    /// <param name="from">The actor it came from.</param>
    private void Reach(string actor, string from)
    {
        var hops = _hops[from] + Step(actor);
        if (!_hops.TryGetValue(actor, out var known) || hops < known)
        {
            _hops[actor] = hops;
            _parent[actor] = from;
        }

        if (Follows(actor) && _runs.GetValueOrDefault(actor) < MaxRuns && _pending.Add(actor))
        {
            _queue.Enqueue(actor, _hops[actor]);
        }
    }

    /// <summary>
    /// Gives how many hops a node adds: none for a transaction drawn as a node of its own (a CoinJoin), since it is not
    /// an actor; one for an actor.
    /// </summary>
    /// <param name="key">The node.</param>
    /// <returns>0 or 1.</returns>
    private int Step(string key) => _request.Graph.Node(key)?.Kind == GraphNodeKind.Transaction ? 0 : 1;

    /// <summary>
    /// Says whether the money is followed on from an actor: not the subject, not a service, an actor (an address, a
    /// cluster or a transaction drawn as a node) and nearer than the depth.
    /// </summary>
    /// <param name="key">The node.</param>
    /// <returns><see langword="true"/> when it is followed.</returns>
    private bool Follows(string key) =>
        !string.Equals(key, _request.Subject, StringComparison.Ordinal)
        && !_request.Services.Contains(key)
        && _request.Graph.Node(key)?.Kind is GraphNodeKind.Address or GraphNodeKind.Cluster or GraphNodeKind.Transaction
        && _hops[key] < _request.Depth;

    /// <summary>Reads an actor: carries the money on through its wallet and reaches the actors it went to.</summary>
    /// <param name="actor">The actor.</param>
    private void Read(string actor)
    {
        _pending.Remove(actor);
        if (_runs.GetValueOrDefault(actor) >= MaxRuns)
        {
            return;
        }

        _runs[actor] = _runs.GetValueOrDefault(actor) + 1;
        var carried = IsOut ? CarryOut(actor) : CarryIn(actor);
        var arrived = Arriving(actor).Aggregate(BigInteger.Zero, (sum, entry) => sum + Money(entry));
        var passed = BigInteger.Zero;
        foreach (var (entry, money) in carried)
        {
            passed += money;
            if (money == Money(entry))
            {
                continue;
            }

            _money[entry] = money;
            if (money.Sign > 0)
            {
                Reach(Far(entry), actor);
            }
        }

        _left[actor] = BigInteger.Max(BigInteger.Zero, arrived - passed);
    }

    /// <summary>Gives how much of the money is on a transfer.</summary>
    /// <param name="entry">The transfer.</param>
    /// <returns>The units.</returns>
    private BigInteger Money(ExposureEntry entry) => _money.GetValueOrDefault(entry);

    /// <summary>
    /// Read out: the method carries the money that came in with the actor's incoming transfers onto its outgoing ones.
    /// </summary>
    /// <param name="actor">The actor.</param>
    /// <returns>The money on each outgoing transfer.</returns>
    private List<(ExposureEntry Entry, BigInteger Money)> CarryOut(string actor)
    {
        var (ledger, outgoing) = Ledger(actor, entry => Money(entry));
        var taint = AccountChecks.Run(_method, ledger);
        return [.. outgoing.Zip(taint.Outgoing, (entry, share) => (entry, share.Units))];
    }

    /// <summary>
    /// Read in: the method is run once per incoming transfer, traced in full, and says how much of each outgoing
    /// transfer came from it; the money on an outgoing transfer is shared out in that proportion.
    /// </summary>
    /// <param name="actor">The actor.</param>
    /// <returns>The money on each incoming transfer, which the actor passed towards the subject.</returns>
    private List<(ExposureEntry Entry, BigInteger Money)> CarryIn(string actor)
    {
        var result = new List<(ExposureEntry, BigInteger)>();
        foreach (var source in Of(_in, actor))
        {
            var (ledger, outgoing) = Ledger(actor, entry => entry == source ? entry.Units : BigInteger.Zero);
            var taint = AccountChecks.Run(_method, ledger);
            var money = BigInteger.Zero;
            foreach (var (entry, share) in outgoing.Zip(taint.Outgoing))
            {
                if (!entry.Units.IsZero)
                {
                    money += Money(entry) * share.Units / entry.Units;
                }
            }

            result.Add((source, money));
        }

        return result;
    }

    /// <summary>
    /// Writes an actor's wallet as the account model's ledger: its transfers in time order, unconfirmed last, the
    /// incoming before the outgoing within the same moment, with the traced part of each incoming one.
    /// </summary>
    /// <param name="actor">The actor.</param>
    /// <param name="traced">How much of an incoming transfer is traced.</param>
    /// <returns>The ledger, and its outgoing transfers in the ledger's order.</returns>
    private (AccountLedger Ledger, List<ExposureEntry> Outgoing) Ledger(
        string actor, Func<ExposureEntry, BigInteger> traced)
    {
        var asset = _request.Asset;
        var moves = Of(_in, actor).Select(entry => (Entry: entry, Direction: TransferDirection.In))
            .Concat(Of(_out, actor).Select(entry => (Entry: entry, Direction: TransferDirection.Out)))
            .OrderBy(move => move.Entry.Transfer.Time ?? DateTimeOffset.MaxValue)
            .ThenBy(move => move.Direction)
            .ThenBy(move => move.Entry.Transfer.Transaction.Id, StringComparer.Ordinal)
            .ThenBy(move => move.Entry.Transfer.Position)
            .ToList();
        var entries = moves.Select(move => new AccountEntry(
                move.Entry.Transfer,
                move.Direction,
                new AssetAmount(asset, move.Direction == TransferDirection.In ? traced(move.Entry) : BigInteger.Zero)))
            .ToList();
        var ledger = new AccountLedger(
            new ChainAddress(_request.Asset.Chain, actor), asset, AssetAmount.Zero(asset), entries);
        return (ledger, [.. moves.Where(move => move.Direction == TransferDirection.Out).Select(move => move.Entry)]);
    }

    /// <summary>Gathers the result: the services reached, the rest per reason, the money per line and the hops.</summary>
    /// <param name="total">All the subject paid out or got.</param>
    /// <returns>The result.</returns>
    private ExposureResult Result(BigInteger total)
    {
        var asset = _request.Asset;
        var subject = _request.Subject;
        var arrived = _hops.Keys.Where(key => key != subject)
            .ToDictionary(key => key, key => Arriving(key).SumOf(Money), StringComparer.Ordinal);
        var reached = arrived
            .Where(pair => _request.Services.Contains(pair.Key) && pair.Value.Sign > 0)
            .Select(pair => ReachOf(pair.Key, new AssetAmount(asset, pair.Value)))
            .OrderByDescending(reach => reach.Amount.Units)
            .ThenBy(reach => reach.NodeKey, StringComparer.Ordinal)
            .ToList();
        var rest = Rest(total, reached, arrived);
        var flows = _money.Where(pair => pair.Value.Sign > 0)
            .GroupBy(pair => pair.Key.Edge)
            .Select(group => new ExposureFlow(
                group.Key.Key,
                Near(group.First().Key),
                Far(group.First().Key),
                new AssetAmount(asset, group.Aggregate(BigInteger.Zero, (sum, pair) => sum + pair.Value))))
            .OrderBy(flow => flow.EdgeKey, StringComparer.Ordinal)
            .ToList();
        return new ExposureResult(
            subject,
            _request.Direction,
            _request.Depth,
            _method.Id,
            new AssetAmount(asset, total),
            reached,
            rest,
            flows,
            new Dictionary<string, int>(_hops, StringComparer.Ordinal));
    }

    /// <summary>
    /// Counts what reached no service, per reason. What a reading cap may leave uncounted is put with what stays
    /// (out) or came before the history (in), so that the services and the rest add up to the total.
    /// </summary>
    /// <param name="total">All the subject paid out or got.</param>
    /// <param name="reached">The services.</param>
    /// <param name="arrived">The money that arrived at each node the money reached.</param>
    /// <returns>The rest per reason; the reasons without any left out.</returns>
    private Dictionary<ExposureRest, AssetAmount> Rest(
        BigInteger total, IReadOnlyList<ExposureReach> reached, Dictionary<string, BigInteger> arrived)
    {
        var stays = IsOut ? ExposureRest.Remains : ExposureRest.BeforeHistory;
        var parts = new Dictionary<ExposureRest, BigInteger>
        {
            [stays] = _left.Values.Aggregate(BigInteger.Zero, (sum, left) => sum + left),
            [ExposureRest.NotReached] = arrived
                .Where(pair => !_request.Services.Contains(pair.Key) && !_runs.ContainsKey(pair.Key))
                .Aggregate(BigInteger.Zero, (sum, pair) => sum + pair.Value),
            [ExposureRest.Returned] = Arriving(_request.Subject).SumOf(Money),
        };
        var counted = reached.Aggregate(BigInteger.Zero, (sum, reach) => sum + reach.Amount.Units)
            + parts.Values.Aggregate(BigInteger.Zero, (sum, part) => sum + part);
        parts[stays] = BigInteger.Max(BigInteger.Zero, parts[stays] + total - counted);
        return parts.Where(pair => pair.Value.Sign > 0)
            .ToDictionary(pair => pair.Key, pair => new AssetAmount(_request.Asset, pair.Value));
    }

    /// <summary>Makes a service's row: its hops, the way from the subject and the transactions on it.</summary>
    /// <param name="service">The service.</param>
    /// <param name="amount">How much of the money arrived.</param>
    /// <returns>The row.</returns>
    private ExposureReach ReachOf(string service, AssetAmount amount)
    {
        var path = new List<string> { service };
        for (var key = service; _parent.TryGetValue(key, out var parent); key = parent)
        {
            path.Add(parent);
        }

        path.Reverse();
        var edges = new List<string>();
        var transactions = new List<ChainTransactionRef>();
        foreach (var (near, far) in path.Zip(path.Skip(1)))
        {
            var step = _money.Where(pair => pair.Value.Sign > 0 && Near(pair.Key) == near && Far(pair.Key) == far)
                .Select(pair => pair.Key)
                .OrderBy(entry => entry.Transfer.Time ?? DateTimeOffset.MaxValue)
                .ToList();
            edges.AddRange(step.Select(entry => entry.Edge.Key).Distinct(StringComparer.Ordinal));
            transactions.AddRange(step.Select(entry => entry.Transfer.Transaction).Distinct());
        }

        return new ExposureReach(service, amount, _hops[service], path, edges, transactions);
    }
}

/// <summary>Sums the money on transfers.</summary>
internal static class ExposureSums
{
    /// <summary>Sums whole numbers given by a selector.</summary>
    /// <param name="entries">The transfers.</param>
    /// <param name="money">How much is on each.</param>
    /// <returns>The sum.</returns>
    public static BigInteger SumOf(this IEnumerable<ExposureEntry> entries, Func<ExposureEntry, BigInteger> money) =>
        entries.Aggregate(BigInteger.Zero, (sum, entry) => sum + money(entry));
}
