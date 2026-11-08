using System.Numerics;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>One transfer of the followed asset between two actors, as an entry in both actors' ledgers.</summary>
/// <param name="Edge">The line it is on.</param>
/// <param name="Transfer">The transfer, a fact from the chain.</param>
/// <param name="Index">Its place on the line, so that a transaction node's ledger keeps the transaction's order.</param>
internal sealed record ExposureEntry(GraphEdge Edge, AssetTransfer Transfer, int Index)
{
    /// <summary>Gets the amount in the asset's smallest unit.</summary>
    public BigInteger Units => Transfer.Amount.Units;
}

/// <summary>
/// Reads exposure on an account chain: every actor is a wallet whose transfers of one asset stand in time order, the
/// incoming before the outgoing within the same moment (and a transaction node's in the transaction's order). The
/// account method (FIFO or haircut) says, actor by actor, how much of the subject's money goes on with each transfer:
/// <list type="bullet">
/// <item>Paid out: the subject's outgoing transfers are the money; an actor on the way carries what came in onto what
/// goes out.</item>
/// <item>Got: the subject's incoming transfers are the money; at an actor on the way the method is run once for each
/// incoming transfer, which says how much of what the actor sent on came from it.</item>
/// </list>
/// An actor is read again when more of the money reaches it later, at most <see cref="MaxRuns"/> times; what arrives
/// after that is counted as still on its way.
/// </summary>
internal sealed class AccountExposureRun
{
    /// <summary>How many times an actor is read; what reaches it after the last reading is still on its way.</summary>
    internal const int MaxRuns = 8;

    /// <summary>The request.</summary>
    private readonly ExposureRequest _request;

    /// <summary>The method.</summary>
    private readonly IAccountTracingMethod _method;

    /// <summary>The hops, the services and the result.</summary>
    private readonly ExposureTally _tally;

    /// <summary>Each actor's incoming transfers.</summary>
    private readonly Dictionary<string, List<ExposureEntry>> _in = new(StringComparer.Ordinal);

    /// <summary>Each actor's outgoing transfers.</summary>
    private readonly Dictionary<string, List<ExposureEntry>> _out = new(StringComparer.Ordinal);

    /// <summary>How much of the subject's money is on each transfer.</summary>
    private readonly Dictionary<ExposureEntry, BigInteger> _money = [];

    /// <summary>The transactions the money on each transfer went through.</summary>
    private readonly Dictionary<ExposureEntry, HashSet<ChainTransactionRef>> _through = [];

    /// <summary>How many times each actor was read.</summary>
    private readonly Dictionary<string, int> _runs = new(StringComparer.Ordinal);

    /// <summary>What of the money had arrived at each actor when it was last read.</summary>
    private readonly Dictionary<string, BigInteger> _arrivedAtRead = new(StringComparer.Ordinal);

    /// <summary>What of the money stayed with each actor when it was last read.</summary>
    private readonly Dictionary<string, BigInteger> _left = new(StringComparer.Ordinal);

    /// <summary>The actors waiting to be read, the nearest first.</summary>
    private readonly PriorityQueue<string, int> _queue = new();

    /// <summary>
    /// The user's links to other chains, by the transfer they leave from: the line on to the other chain's
    /// transaction as an entry, where the money on the transfer goes instead of into the receiving wallet, as in the
    /// graph's trace.
    /// </summary>
    private readonly Dictionary<AssetTransfer, ExposureEntry> _continues = [];

    /// <summary>The actors in the queue, so that one is queued once however much reaches it before it is read.</summary>
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);

    /// <summary>Prepares a reading.</summary>
    /// <param name="request">The request.</param>
    /// <param name="method">The method.</param>
    public AccountExposureRun(ExposureRequest request, IAccountTracingMethod method)
    {
        _request = request;
        _method = method;
        _tally = new ExposureTally(request);
        foreach (var edge in request.Graph.Edges.Where(IsFollowed))
        {
            var index = 0;
            foreach (var transfer in edge.Transfers.Where(t => t.Amount.Asset == request.Asset))
            {
                var entry = new ExposureEntry(edge, transfer, index++);
                ListOf(_out, edge.From).Add(entry);
                ListOf(_in, edge.To).Add(entry);
                _tally.When(transfer.Transaction, transfer.Time);
            }
        }

        foreach (var edge in request.Graph.Edges.Where(edge => edge.Kind == GraphEdgeKind.ContinuesOnOtherChain))
        {
            var index = 0;
            foreach (var transfer in edge.Transfers.Where(t => t.Amount.Asset == request.Asset))
            {
                var entry = new ExposureEntry(edge, transfer, index++);
                _continues[transfer] = entry;
                ListOf(_in, edge.To).Add(entry);
            }
        }
    }

    /// <summary>Gets whether the money is read out (paid) rather than in (got).</summary>
    private bool IsOut => _tally.IsOut;

    /// <summary>Reads the exposure.</summary>
    /// <returns>The result.</returns>
    public ExposureResult Run()
    {
        var subject = _request.Subject;
        var total = BigInteger.Zero;
        foreach (var entry in Carrying(subject))
        {
            _money[entry] = entry.Units;
            _through[entry] = [entry.Transfer.Transaction];
            total += entry.Units;
            Reach(Far(entry), subject);
            ContinueElsewhere(entry);
        }

        while (_queue.TryDequeue(out var actor, out _))
        {
            Read(actor);
        }

        Count();
        return _tally.Result(total, _method.Id);
    }

    /// <summary>Says whether a line carries transfers the analysis follows: a transfer between two actors.</summary>
    /// <param name="edge">The line.</param>
    /// <returns><see langword="true"/> when it is followed.</returns>
    private bool IsFollowed(GraphEdge edge) =>
        edge.Kind == GraphEdgeKind.Transfer && edge.Amount.Asset == _request.Asset && edge.From != edge.To;

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
    /// Gives the transfers through which the money reaches an actor: in when read out (but not those the user linked
    /// to another chain, whose money goes on there), out when read in.
    /// </summary>
    /// <param name="actor">The actor.</param>
    /// <returns>The transfers.</returns>
    private IEnumerable<ExposureEntry> Arriving(string actor) =>
        IsOut ? Of(_in, actor).Where(entry => !IsLinked(entry)) : Of(_out, actor);

    /// <summary>
    /// Says whether a transfer between two actors is linked by the user to another chain, so that its money continues
    /// there and not in the receiving wallet (<see cref="SpendRules"/> on a UTXO chain says the same of a coin).
    /// </summary>
    /// <param name="entry">The transfer.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private bool IsLinked(ExposureEntry entry) =>
        entry.Edge.Kind == GraphEdgeKind.Transfer && _continues.ContainsKey(entry.Transfer);

    /// <summary>
    /// Read out: sends the money on a transfer the user linked to another chain on to the other chain's transaction,
    /// where it stops, as the graph's trace does.
    /// </summary>
    /// <param name="entry">The transfer that just got money.</param>
    private void ContinueElsewhere(ExposureEntry entry)
    {
        if (!IsOut || !IsLinked(entry))
        {
            return;
        }

        var onward = _continues[entry.Transfer];
        _money[onward] = Money(entry);
        _through[onward] = _through.GetValueOrDefault(entry, [entry.Transfer.Transaction]);
        _tally.Reach(onward.Edge.To, onward.Edge.From);
    }

    /// <summary>Gives the transfers an actor passes the money on through, away from the subject.</summary>
    /// <param name="actor">The actor.</param>
    /// <returns>The transfers.</returns>
    private IReadOnlyList<ExposureEntry> Carrying(string actor) => IsOut ? Of(_out, actor) : Of(_in, actor);

    /// <summary>Gives a transfer's end away from the subject.</summary>
    /// <param name="entry">The transfer.</param>
    /// <returns>The node.</returns>
    private string Far(ExposureEntry entry) => IsOut ? entry.Edge.To : entry.Edge.From;

    /// <summary>Gives how much of the money is on a transfer.</summary>
    /// <param name="entry">The transfer.</param>
    /// <returns>The units.</returns>
    private BigInteger Money(ExposureEntry entry) => _money.GetValueOrDefault(entry);

    /// <summary>Gives the money that has arrived at an actor.</summary>
    /// <param name="actor">The actor.</param>
    /// <returns>The units.</returns>
    private BigInteger Arrived(string actor) =>
        Arriving(actor).Aggregate(BigInteger.Zero, (sum, entry) => sum + Money(entry));

    /// <summary>Notes that the money reached an actor, and queues it when it is followed and may still be read.</summary>
    /// <param name="actor">The actor reached.</param>
    /// <param name="from">The actor it came from.</param>
    private void Reach(string actor, string from)
    {
        _tally.Reach(actor, from);
        if (_tally.Follows(actor) && _runs.GetValueOrDefault(actor) < MaxRuns && _pending.Add(actor))
        {
            _queue.Enqueue(actor, _tally.Hops[actor]);
        }
    }

    /// <summary>Reads an actor: carries the money on through its wallet and reaches the actors it went to.</summary>
    /// <param name="actor">The actor.</param>
    private void Read(string actor)
    {
        _pending.Remove(actor);
        _runs[actor] = _runs.GetValueOrDefault(actor) + 1;
        var arrived = Arrived(actor);
        var passed = BigInteger.Zero;
        foreach (var (entry, money, through) in IsOut ? CarryOut(actor) : CarryIn(actor))
        {
            passed += money;
            _through[entry] = through;
            if (money == Money(entry))
            {
                continue;
            }

            _money[entry] = money;
            if (money.Sign > 0)
            {
                Reach(Far(entry), actor);
                ContinueElsewhere(entry);
            }
        }

        _arrivedAtRead[actor] = arrived;
        _left[actor] = arrived - passed;
    }

    /// <summary>
    /// Read out: the method carries the money that came in with the actor's incoming transfers onto its outgoing ones.
    /// An outgoing transfer went through the transactions of the incoming money that came before it.
    /// </summary>
    /// <param name="actor">The actor.</param>
    /// <returns>The money on each outgoing transfer, and what it went through.</returns>
    private List<(ExposureEntry Entry, BigInteger Money, HashSet<ChainTransactionRef> Through)> CarryOut(string actor)
    {
        var (ledger, moves) = Ledger(actor, Money);
        var taint = AccountChecks.Run(_method, ledger);
        var outgoing = moves.Where(move => move.Direction == TransferDirection.Out).Select(move => move.Entry).ToList();
        var result = new List<(ExposureEntry, BigInteger, HashSet<ChainTransactionRef>)>();
        foreach (var (entry, share) in outgoing.Zip(taint.Outgoing))
        {
            var before = moves.TakeWhile(move => move.Entry != entry)
                .Where(move => move.Direction == TransferDirection.In && Money(move.Entry).Sign > 0)
                .SelectMany(move => _through.GetValueOrDefault(move.Entry, []));
            result.Add((entry, share.Units, [.. before, entry.Transfer.Transaction]));
        }

        return result;
    }

    /// <summary>
    /// Read in: the method is run once per incoming transfer, traced in full, and says how much of each outgoing
    /// transfer came from it; the money on an outgoing transfer is shared out in that proportion.
    /// </summary>
    /// <param name="actor">The actor.</param>
    /// <returns>The money on each incoming transfer, and what it went through towards the subject.</returns>
    private List<(ExposureEntry Entry, BigInteger Money, HashSet<ChainTransactionRef> Through)> CarryIn(string actor)
    {
        var result = new List<(ExposureEntry, BigInteger, HashSet<ChainTransactionRef>)>();
        foreach (var source in Of(_in, actor))
        {
            var (ledger, moves) = Ledger(actor, entry => entry == source ? entry.Units : BigInteger.Zero);
            var taint = AccountChecks.Run(_method, ledger);
            var outgoing = moves.Where(move => move.Direction == TransferDirection.Out).Select(move => move.Entry);
            var money = BigInteger.Zero;
            var through = new HashSet<ChainTransactionRef> { source.Transfer.Transaction };
            foreach (var (entry, share) in outgoing.Zip(taint.Outgoing))
            {
                if (entry.Units.IsZero || share.Units.IsZero || Money(entry).IsZero)
                {
                    continue;
                }

                money += Money(entry) * share.Units / entry.Units;
                through.UnionWith(_through.GetValueOrDefault(entry, []));
            }

            result.Add((source, money, through));
        }

        return result;
    }

    /// <summary>
    /// Writes an actor's wallet as the account model's ledger: its transfers in time order, unconfirmed last, the
    /// incoming before the outgoing within the same moment, then in the transaction's order.
    /// </summary>
    /// <param name="actor">The actor.</param>
    /// <param name="traced">How much of an incoming transfer is traced.</param>
    /// <returns>The ledger, and its moves in the ledger's order.</returns>
    private (AccountLedger Ledger, List<(ExposureEntry Entry, TransferDirection Direction)> Moves) Ledger(
        string actor, Func<ExposureEntry, BigInteger> traced)
    {
        var asset = _request.Asset;
        var moves = Of(_in, actor).Where(entry => !IsLinked(entry))
            .Select(entry => (Entry: entry, Direction: TransferDirection.In))
            .Concat(Of(_out, actor).Select(entry => (Entry: entry, Direction: TransferDirection.Out)))
            .OrderBy(move => move.Entry.Transfer.Time ?? DateTimeOffset.MaxValue)
            .ThenBy(move => move.Direction)
            .ThenBy(move => move.Entry.Transfer.Transaction.Id, StringComparer.Ordinal)
            .ThenBy(move => move.Entry.Transfer.Position)
            .ThenBy(move => move.Entry.Transfer.Coin?.Index ?? move.Entry.Index)
            .ToList();
        var entries = moves.Select(move => new AccountEntry(
                move.Entry.Transfer,
                move.Direction,
                new AssetAmount(asset, move.Direction == TransferDirection.In ? traced(move.Entry) : BigInteger.Zero)))
            .ToList();
        var ledger = new AccountLedger(new ChainAddress(asset.Chain, actor), asset, AssetAmount.Zero(asset), entries);
        return (ledger, moves);
    }

    /// <summary>
    /// Counts where the money stopped: at the services and the subject what arrived; at an actor read what stayed when
    /// it was last read and what came after (still on its way); at an actor not followed what arrived.
    /// </summary>
    private void Count()
    {
        var stays = IsOut ? ExposureRest.Remains : ExposureRest.BeforeHistory;
        foreach (var actor in _tally.Hops.Keys.ToList())
        {
            var arrived = Arrived(actor);
            if (_runs.ContainsKey(actor))
            {
                _tally.Rest(_tally.StaysWith(actor, stays), _left[actor]);
                _tally.Rest(ExposureRest.StillMoving, arrived - _arrivedAtRead[actor]);
                continue;
            }

            var through = Arriving(actor).Where(entry => Money(entry).Sign > 0)
                .SelectMany(entry => _through.GetValueOrDefault(entry, []));
            _tally.Arrive(actor, arrived, through);
        }

        foreach (var (entry, money) in _money)
        {
            _tally.Flow(entry.Edge, money);
        }
    }
}
