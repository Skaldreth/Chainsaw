using System.Numerics;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>One transfer between two actors, as an entry in both actors' ledgers of its asset.</summary>
/// <param name="Edge">The line it is on.</param>
/// <param name="Transfer">The transfer, a fact from the chain.</param>
/// <param name="Index">Its place on the line, so that a transaction node's ledger keeps the transaction's order.</param>
internal sealed record ExposureEntry(GraphEdge Edge, AssetTransfer Transfer, int Index)
{
    /// <summary>Gets the amount in the asset's smallest unit.</summary>
    public BigInteger Units => Transfer.Amount.Units;

    /// <summary>Gets the transfer's asset.</summary>
    public Asset Asset => Transfer.Amount.Asset;
}

/// <summary>
/// Reads exposure on an account chain: every actor is a wallet whose transfers of each asset stand in time order, the
/// incoming before the outgoing within the same moment (and a transaction node's in the transaction's order). The
/// account method (FIFO or haircut) says, actor by actor and asset by asset, how much of the subject's money goes on
/// with each transfer:
/// <list type="bullet">
/// <item>Paid out: the subject's outgoing transfers of the asset are the money; an actor on the way carries what came
/// in onto what goes out.</item>
/// <item>Got: the subject's incoming transfers of the asset are the money; at an actor on the way the method is run
/// once for each incoming transfer, which says how much of what the actor sent on came from it.</item>
/// </list>
/// Through a swap the money follows its value into the other asset by the same rule as the graph's trace
/// (<see cref="SwapValue"/>), or stops there when the user chose that it does not (<see cref="ExposureRest.TrailEnds"/>).
/// Each transfer carries the money in its own units and, beside it, its <em>value</em> in the subject's asset, which is
/// what is counted: across a swap the value that went in is the value that came out. An actor is read again when more
/// of the money reaches it later, at most <see cref="MaxRuns"/> times; what arrives after that is counted as still on
/// its way.
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

    /// <summary>Each actor's incoming transfers, of every asset.</summary>
    private readonly Dictionary<string, List<ExposureEntry>> _in = new(StringComparer.Ordinal);

    /// <summary>Each actor's outgoing transfers, of every asset.</summary>
    private readonly Dictionary<string, List<ExposureEntry>> _out = new(StringComparer.Ordinal);

    /// <summary>How much of the subject's money is on each transfer, in the transfer's own units.</summary>
    private readonly Dictionary<ExposureEntry, BigInteger> _money = [];

    /// <summary>What the money on each transfer is worth in the subject's asset: what is counted.</summary>
    private readonly Dictionary<ExposureEntry, BigInteger> _value = [];

    /// <summary>The transactions the money on each transfer went through.</summary>
    private readonly Dictionary<ExposureEntry, HashSet<ChainTransactionRef>> _through = [];

    /// <summary>How many times each actor was read.</summary>
    private readonly Dictionary<string, int> _runs = new(StringComparer.Ordinal);

    /// <summary>What of the money's value had arrived at each actor when it was last read.</summary>
    private readonly Dictionary<string, BigInteger> _arrivedAtRead = new(StringComparer.Ordinal);

    /// <summary>What of the money's value stayed with each actor when it was last read.</summary>
    private readonly Dictionary<string, BigInteger> _left = new(StringComparer.Ordinal);

    /// <summary>The actors waiting to be read, the nearest first.</summary>
    private readonly PriorityQueue<string, int> _queue = new();

    /// <summary>
    /// The user's links to other chains, by the transfer they leave from: the line on to the other chain's
    /// transaction as an entry, where the money on the transfer goes instead of into the receiving wallet, as in the
    /// graph's trace.
    /// </summary>
    private readonly Dictionary<AssetTransfer, ExposureEntry> _continues = [];

    /// <summary>The swaps on the graph's lines, with the transfers that went in and came out.</summary>
    private readonly List<SwapSides> _swaps = [];

    /// <summary>The swap each transfer that went into one belongs to.</summary>
    private readonly Dictionary<ExposureEntry, SwapSides> _soldIn = [];

    /// <summary>The swap each transfer that came out of one belongs to.</summary>
    private readonly Dictionary<ExposureEntry, SwapSides> _boughtOut = [];

    /// <summary>The actors in the queue, so that one is queued once however much reaches it before it is read.</summary>
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);

    /// <summary>The actor being read, or <see langword="null"/>.</summary>
    private string? _reading;

    /// <summary>Whether a swap of the actor being read carried money into another of its assets in this pass.</summary>
    private bool _selfSwapped;

    /// <summary>How many times the subject's own wallet was read in the assets its swaps turned the money into.</summary>
    private int _subjectRuns;

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
            foreach (var transfer in edge.Transfers.Where(t => t.Amount.Asset == edge.Amount.Asset))
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
            foreach (var transfer in edge.Transfers.Where(t => t.Amount.Asset.Chain == request.Asset.Chain))
            {
                var entry = new ExposureEntry(edge, transfer, index++);
                _continues[transfer] = entry;
                ListOf(_in, edge.To).Add(entry);
            }
        }

        FindSwaps();
    }

    /// <summary>Gets whether the money is read out (paid) rather than in (got).</summary>
    private bool IsOut => _tally.IsOut;

    /// <summary>Reads the exposure.</summary>
    /// <returns>The result.</returns>
    public ExposureResult Run()
    {
        var subject = _request.Subject;
        var total = BigInteger.Zero;
        foreach (var entry in Carrying(subject).Where(entry => entry.Asset == _request.Asset))
        {
            Put(entry, entry.Units, entry.Units, [entry.Transfer.Transaction]);
            total += entry.Units;
            if (!Convert(entry))
            {
                Reach(Far(entry), subject);
            }

            ContinueElsewhere(entry);
        }

        while (_queue.TryDequeue(out var actor, out _))
        {
            Read(actor);
        }

        Count();
        return _tally.Result(total, _method.Id);
    }

    /// <summary>
    /// Says whether a line carries transfers the analysis follows: a transfer between two actors on the subject's
    /// chain, of any asset, since a swap can carry the money's value into another.
    /// </summary>
    /// <param name="edge">The line.</param>
    /// <returns><see langword="true"/> when it is followed.</returns>
    private bool IsFollowed(GraphEdge edge) =>
        edge.Kind == GraphEdgeKind.Transfer && edge.Amount.Asset.Chain == _request.Asset.Chain && edge.From != edge.To;

    /// <summary>
    /// Finds the swaps on the graph's lines (<see cref="AccountGraph.Swaps"/>): the transfers the account that swapped
    /// sent into each, and those that came out of it to the account, in its transaction.
    /// </summary>
    private void FindSwaps()
    {
        var graph = _request.Graph;
        foreach (var swap in graph.Account.Swaps)
        {
            if (graph.NodeOf(swap.Account)?.Key is not { } account)
            {
                continue;
            }

            var inSwap = (ExposureEntry entry) => entry.Transfer.Transaction.Id == swap.Transaction.Value;
            var goesIn = (ExposureEntry entry) => SwapValue.GoesIn(swap, entry.Transfer, graph.CrossChainLinks);
            var found = new SwapSides(
                swap,
                [.. Of(_out, account).Where(goesIn)],
                [.. Of(_in, account).Where(entry => inSwap(entry) && entry.Asset == swap.Bought.Asset)],
                SwapValue.StopOf(swap, graph.SwapStops) is null);
            if (found.Sold.Count == 0 || found.Bought.Count == 0)
            {
                continue;
            }

            _swaps.Add(found);
            found.Sold.ForEach(entry => _soldIn[entry] = found);
            found.Bought.ForEach(entry => _boughtOut[entry] = found);
        }
    }

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
    /// to another chain, whose money goes on there, nor those that went into a swap), out when read in (but not those
    /// that came out of a swap, whose money came from what went in).
    /// </summary>
    /// <param name="actor">The actor.</param>
    /// <returns>The transfers.</returns>
    private IEnumerable<ExposureEntry> Arriving(string actor) => IsOut
        ? Of(_in, actor).Where(EntersWallet)
        : Of(_out, actor).Where(entry => !_boughtOut.ContainsKey(entry));

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
        Put(onward, Money(entry), Value(entry), _through.GetValueOrDefault(entry, [entry.Transfer.Transaction]));
        _tally.Reach(onward.Edge.To, onward.Edge.From);
    }

    /// <summary>
    /// Carries the money on a transfer through its swap, when it went into one (read out) or came out of one (read
    /// in): when the value follows, the same share of the other side carries it, and the account that swapped is read
    /// again; when the user chose that it does not, the money stops in the swap.
    /// </summary>
    /// <param name="entry">The transfer that just got money.</param>
    /// <returns><see langword="true"/> when the transfer is a swap's, so that the money does not reach the pool.</returns>
    private bool Convert(ExposureEntry entry)
    {
        var swap = IsOut ? _soldIn.GetValueOrDefault(entry) : _boughtOut.GetValueOrDefault(entry);
        if (swap is null)
        {
            return false;
        }

        if (!swap.Follows)
        {
            return true;
        }

        var (from, to, whole) = IsOut
            ? (swap.Sold, swap.Bought, swap.Swap.Sold)
            : (swap.Bought, swap.Sold, swap.Swap.Bought);
        var money = from.Aggregate(BigInteger.Zero, (sum, side) => sum + Money(side));
        var value = from.Aggregate(BigInteger.Zero, (sum, side) => sum + Value(side));
        _tally.Swapped(swap.Swap, money, whole.Units);
        var through = from.SelectMany(side => _through.GetValueOrDefault(side, [])).ToHashSet();
        var otherWhole = to.Aggregate(BigInteger.Zero, (sum, side) => sum + side.Units);
        var shared = BigInteger.Zero;
        for (var index = 0; index < to.Count; index++)
        {
            var side = to[index];
            // The value is shared out by the transfers' amounts, the last one taking what rounding leaves, so that
            // the value that went in is exactly the value that came out.
            var part = index == to.Count - 1
                ? value - shared
                : (otherWhole.IsZero ? BigInteger.Zero : value * side.Units / otherWhole);
            shared += part;
            Put(side, SwapValue.Follow(money, whole, side.Units), part, [.. through, side.Transfer.Transaction]);
        }

        var account = _tally.Graph.NodeOf(swap.Swap.Account)!.Key;
        if (account == _reading)
        {
            _selfSwapped = true;
        }
        else
        {
            Requeue(account);
        }

        return true;
    }

    /// <summary>Puts the money on a transfer.</summary>
    /// <param name="entry">The transfer.</param>
    /// <param name="money">How much, in its own units.</param>
    /// <param name="value">What it is worth in the subject's asset.</param>
    /// <param name="through">The transactions it went through.</param>
    private void Put(ExposureEntry entry, BigInteger money, BigInteger value, HashSet<ChainTransactionRef> through)
    {
        _money[entry] = money;
        _value[entry] = value;
        _through[entry] = through;
    }

    /// <summary>Gives the transfers an actor passes the money on through, away from the subject.</summary>
    /// <param name="actor">The actor.</param>
    /// <returns>The transfers.</returns>
    private IReadOnlyList<ExposureEntry> Carrying(string actor) => IsOut ? Of(_out, actor) : Of(_in, actor);

    /// <summary>Gives a transfer's end away from the subject.</summary>
    /// <param name="entry">The transfer.</param>
    /// <returns>The node.</returns>
    private string Far(ExposureEntry entry) => IsOut ? entry.Edge.To : entry.Edge.From;

    /// <summary>Gives how much of the money is on a transfer, in its own units.</summary>
    /// <param name="entry">The transfer.</param>
    /// <returns>The units.</returns>
    private BigInteger Money(ExposureEntry entry) => _money.GetValueOrDefault(entry);

    /// <summary>Gives what the money on a transfer is worth in the subject's asset.</summary>
    /// <param name="entry">The transfer.</param>
    /// <returns>The units of the subject's asset.</returns>
    private BigInteger Value(ExposureEntry entry) => _value.GetValueOrDefault(entry);

    /// <summary>Gives the value of the money that has arrived at an actor.</summary>
    /// <param name="actor">The actor.</param>
    /// <returns>The units of the subject's asset.</returns>
    private BigInteger Arrived(string actor) =>
        Arriving(actor).Aggregate(BigInteger.Zero, (sum, entry) => sum + Value(entry));

    /// <summary>Notes that the money reached an actor, and queues it when it is followed and may still be read.</summary>
    /// <param name="actor">The actor reached.</param>
    /// <param name="from">The actor it came from.</param>
    private void Reach(string actor, string from)
    {
        _tally.Reach(actor, from);
        Requeue(actor);
    }

    /// <summary>Queues an actor to be read, when it is followed and may still be read.</summary>
    /// <param name="actor">The actor.</param>
    private void Requeue(string actor)
    {
        if (actor == _request.Subject)
        {
            ReadSubject();
            return;
        }

        if (_tally.Follows(actor) && _runs.GetValueOrDefault(actor) < MaxRuns && _pending.Add(actor))
        {
            _queue.Enqueue(actor, _tally.Hops[actor]);
        }
    }

    /// <summary>
    /// Reads an actor: carries the money on through its wallet, asset by asset, and reaches the actors it went to.
    /// </summary>
    /// <param name="actor">The actor.</param>
    private void Read(string actor)
    {
        _pending.Remove(actor);
        _runs[actor] = _runs.GetValueOrDefault(actor) + 1;
        ReadAssets(actor, [.. Of(_in, actor).Concat(Of(_out, actor)).Select(entry => entry.Asset).Distinct()]);
        var arrived = Arrived(actor);
        _arrivedAtRead[actor] = arrived;
        _left[actor] = arrived - Passing(actor).Aggregate(BigInteger.Zero, (sum, entry) => sum + Value(entry));
    }

    /// <summary>
    /// Reads the subject's own wallet in the assets its swaps turned the money into: its transfers of the subject's
    /// asset are the money itself, but what it swapped them for goes on like any wallet's, at most
    /// <see cref="MaxRuns"/> times.
    /// </summary>
    private void ReadSubject()
    {
        if (_subjectRuns >= MaxRuns)
        {
            return;
        }

        _subjectRuns++;
        var subject = _request.Subject;
        ReadAssets(subject, [.. Of(_in, subject).Concat(Of(_out, subject)).Select(entry => entry.Asset)
            .Where(asset => asset != _request.Asset).Distinct()]);
    }

    /// <summary>
    /// Carries the money through an actor's wallet of some assets. A swap of the actor's own carries money from one of
    /// its assets into another, so the assets are read again until nothing more comes over, at most once per asset,
    /// so that what a swap brings in is passed on in this reading and not left behind.
    /// </summary>
    /// <param name="actor">The actor.</param>
    /// <param name="assets">The assets.</param>
    private void ReadAssets(string actor, IReadOnlyList<Asset> assets)
    {
        var (reading, selfSwapped) = (_reading, _selfSwapped);
        _reading = actor;
        for (var pass = 0; pass <= assets.Count; pass++)
        {
            _selfSwapped = false;
            foreach (var asset in assets)
            {
                CarryAsset(actor, asset);
            }

            if (!_selfSwapped)
            {
                break;
            }
        }

        (_reading, _selfSwapped) = (reading, selfSwapped);
    }

    /// <summary>Gives the transfers an actor passes the money on through, of every asset.</summary>
    /// <param name="actor">The actor.</param>
    /// <returns>The transfers.</returns>
    private IEnumerable<ExposureEntry> Passing(string actor) => IsOut
        ? Of(_out, actor).Where(entry => !_boughtOut.ContainsKey(entry))
        : Of(_in, actor).Where(EntersWallet);

    /// <summary>Carries the money through an actor's wallet of one asset and reaches the actors it went to.</summary>
    /// <param name="actor">The actor.</param>
    /// <param name="asset">The asset.</param>
    private void CarryAsset(string actor, Asset asset)
    {
        foreach (var (entry, money, value, through) in IsOut ? CarryOut(actor, asset) : CarryIn(actor, asset))
        {
            if (money == Money(entry) && value == Value(entry))
            {
                _through[entry] = through;
                continue;
            }

            Put(entry, money, value, through);
            if (money.Sign > 0 && !Convert(entry))
            {
                Reach(Far(entry), actor);
                ContinueElsewhere(entry);
            }
        }
    }

    /// <summary>
    /// Read out: the method carries the money that came in with the actor's incoming transfers of an asset onto its
    /// outgoing ones. An outgoing transfer went through the transactions of the incoming money that came before it, and
    /// is worth the incoming money's value in proportion.
    /// </summary>
    /// <param name="actor">The actor.</param>
    /// <param name="asset">The asset.</param>
    /// <returns>The money and value on each outgoing transfer, and what it went through.</returns>
    private List<(ExposureEntry Entry, BigInteger Money, BigInteger Value, HashSet<ChainTransactionRef> Through)>
        CarryOut(string actor, Asset asset)
    {
        var (ledger, moves) = Ledger(actor, asset, Money);
        var taint = AccountChecks.Run(_method, ledger);
        var incoming = moves.Where(move => move.Direction == TransferDirection.In).Select(move => move.Entry).ToList();
        var money = incoming.Aggregate(BigInteger.Zero, (sum, entry) => sum + Money(entry));
        var value = incoming.Aggregate(BigInteger.Zero, (sum, entry) => sum + Value(entry));
        var outgoing = moves.Where(move => move.Direction == TransferDirection.Out).Select(move => move.Entry).ToList();
        var result = new List<(ExposureEntry, BigInteger, BigInteger, HashSet<ChainTransactionRef>)>();
        foreach (var (entry, share) in outgoing.Zip(taint.Outgoing))
        {
            var before = moves.TakeWhile(move => move.Entry != entry)
                .Where(move => move.Direction == TransferDirection.In && Money(move.Entry).Sign > 0)
                .SelectMany(move => _through.GetValueOrDefault(move.Entry, []));
            var worth = money.IsZero ? BigInteger.Zero : share.Units * value / money;
            result.Add((entry, share.Units, worth, [.. before, entry.Transfer.Transaction]));
        }

        return result;
    }

    /// <summary>
    /// Read in: the method is run once per incoming transfer of an asset, traced in full, and says how much of each
    /// outgoing transfer came from it; the money and value on an outgoing transfer are shared out in that proportion.
    /// </summary>
    /// <param name="actor">The actor.</param>
    /// <param name="asset">The asset.</param>
    /// <returns>The money and value on each incoming transfer, and what it went through towards the subject.</returns>
    private List<(ExposureEntry Entry, BigInteger Money, BigInteger Value, HashSet<ChainTransactionRef> Through)>
        CarryIn(string actor, Asset asset)
    {
        var result = new List<(ExposureEntry, BigInteger, BigInteger, HashSet<ChainTransactionRef>)>();
        foreach (var source in Of(_in, actor).Where(entry => entry.Asset == asset && EntersWallet(entry)))
        {
            var (ledger, moves) = Ledger(actor, asset, entry => entry == source ? entry.Units : BigInteger.Zero);
            var taint = AccountChecks.Run(_method, ledger);
            var outgoing = moves.Where(move => move.Direction == TransferDirection.Out).Select(move => move.Entry);
            var money = BigInteger.Zero;
            var value = BigInteger.Zero;
            var through = new HashSet<ChainTransactionRef> { source.Transfer.Transaction };
            foreach (var (entry, share) in outgoing.Zip(taint.Outgoing))
            {
                if (entry.Units.IsZero || share.Units.IsZero || Money(entry).IsZero)
                {
                    continue;
                }

                money += Money(entry) * share.Units / entry.Units;
                value += Value(entry) * share.Units / entry.Units;
                through.UnionWith(_through.GetValueOrDefault(entry, []));
            }

            result.Add((source, money, value, through));
        }

        return result;
    }

    /// <summary>
    /// Writes an actor's wallet of one asset as the account model's ledger: its transfers in time order, unconfirmed
    /// last, the incoming before the outgoing within the same moment, then in the transaction's order.
    /// </summary>
    /// <param name="actor">The actor.</param>
    /// <param name="asset">The asset.</param>
    /// <param name="traced">How much of an incoming transfer is traced.</param>
    /// <returns>The ledger, and its moves in the ledger's order.</returns>
    private (AccountLedger Ledger, List<(ExposureEntry Entry, TransferDirection Direction)> Moves) Ledger(
        string actor, Asset asset, Func<ExposureEntry, BigInteger> traced)
    {
        var moves = Of(_in, actor).Where(entry => entry.Asset == asset && EntersWallet(entry))
            .Select(entry => (Entry: entry, Direction: TransferDirection.In))
            .Concat(Of(_out, actor).Where(entry => entry.Asset == asset && !_boughtOut.ContainsKey(entry))
                .Select(entry => (Entry: entry, Direction: TransferDirection.Out)))
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
    /// Says whether an incoming transfer enters the receiving wallet: not one the user linked to another chain, whose
    /// money goes on there, and not one that went into a swap, whose money follows into the other asset or ends in the
    /// swap and never enters the pool's wallet, as in the graph's trace.
    /// </summary>
    /// <param name="entry">The transfer.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private bool EntersWallet(ExposureEntry entry) => !IsLinked(entry) && !_soldIn.ContainsKey(entry);

    /// <summary>
    /// Counts where the money's value stopped: at the services and the subject what arrived; at an actor read what
    /// stayed when it was last read and what came after (still on its way); at an actor not followed what arrived; and
    /// in a swap the user chose not to follow, what went into it.
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

            var arriving = Arriving(actor).ToList();
            if (actor == _request.Subject)
            {
                // What the subject swapped its money for is its own wallet's, read like any other; only the money
                // that comes back in the subject's own asset has returned.
                var swapped = arriving.Where(entry => entry.Asset != _request.Asset).ToList();
                var passed = Passing(actor).Where(entry => entry.Asset != _request.Asset)
                    .Aggregate(BigInteger.Zero, (sum, entry) => sum + Value(entry));
                _tally.Rest(stays, swapped.Aggregate(BigInteger.Zero, (sum, entry) => sum + Value(entry)) - passed);
                arriving = [.. arriving.Except(swapped)];
            }

            var through = arriving.Where(entry => Money(entry).Sign > 0)
                .SelectMany(entry => _through.GetValueOrDefault(entry, []));
            _tally.Arrive(actor, arriving.Aggregate(BigInteger.Zero, (sum, entry) => sum + Value(entry)), through);
        }

        foreach (var swap in _swaps.Where(swap => !swap.Follows))
        {
            var stopped = IsOut ? swap.Sold : swap.Bought;
            _tally.Rest(ExposureRest.TrailEnds, stopped.Aggregate(BigInteger.Zero, (sum, side) => sum + Value(side)));
        }

        foreach (var (entry, value) in _value)
        {
            _tally.Flow(entry.Edge, value);
        }
    }

    /// <summary>A swap on the graph's lines, with the transfers that went in and came out of it.</summary>
    /// <param name="Swap">The swap.</param>
    /// <param name="Sold">The transfers the account sent into it.</param>
    /// <param name="Bought">The transfers that came out of it to the account.</param>
    /// <param name="Follows">Whether the value follows through it (the user has not chosen otherwise).</param>
    private sealed record SwapSides(
        TokenSwap Swap, List<ExposureEntry> Sold, List<ExposureEntry> Bought, bool Follows);
}
