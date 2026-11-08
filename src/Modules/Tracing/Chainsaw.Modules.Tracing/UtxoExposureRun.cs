using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// Reads exposure on a UTXO chain coin by coin, with the UTXO method registered for the chain, so that it agrees with
/// the graph's trace about where every coin went. The actors are the sums of their coins:
/// <list type="bullet">
/// <item>Paid out: the coins the subject paid to others (and its coins spent into a transaction drawn as a node) are
/// the money. A coin of an actor that is followed goes on through the transaction that spends it, and the method says
/// which outputs and how much of the fee it reaches; a coin at a service, the subject or an actor not followed stops
/// there.</item>
/// <item>Got: the coins the subject got are the money. A coin of an actor that is followed goes back through the
/// transaction that made it: the method, run once per input traced in full, says how much of the coin came from each
/// input, and the coin's money is shared out in that proportion.</item>
/// </list>
/// The transactions are read in block order (backwards when got), again when more money reaches them.
/// </summary>
internal sealed class UtxoExposureRun
{
    /// <summary>The request.</summary>
    private readonly ExposureRequest _request;

    /// <summary>The method.</summary>
    private readonly IUtxoTracingMethod _method;

    /// <summary>The hops, the services and the result.</summary>
    private readonly ExposureTally _tally;

    /// <summary>The lines each coin moved on: where it was paid, and where it was spent into a transaction node.</summary>
    private readonly Dictionary<Outpoint, List<GraphEdge>> _lines = [];

    /// <summary>
    /// The owners of coins whose making transaction the graph may not hold: the subject's, and inputs read back from
    /// their own address.
    /// </summary>
    private readonly Dictionary<Outpoint, string> _owners = [];

    /// <summary>The coins the money starts in.</summary>
    private readonly HashSet<Outpoint> _seeds = [];

    /// <summary>How much of the money is in each coin (paid out: which units of it).</summary>
    private readonly Dictionary<Outpoint, TaintSet> _taint = [];

    /// <summary>How much of the money is in each coin when got.</summary>
    private readonly Dictionary<Outpoint, BigInteger> _units = [];

    /// <summary>The transactions the money in each coin went through.</summary>
    private readonly Dictionary<Outpoint, HashSet<ChainTransactionRef>> _through = [];

    /// <summary>The coins whose money goes on through a transaction the run reads.</summary>
    private readonly HashSet<Outpoint> _passed = [];

    /// <summary>
    /// The coins whose money stops at a transaction drawn as a node that is a service (a CoinJoin): the node, by coin.
    /// </summary>
    private readonly Dictionary<Outpoint, string> _stopAt = [];

    /// <summary>The fee each transaction read took of the money.</summary>
    private readonly Dictionary<Txid, long> _fees = [];

    /// <summary>The transactions waiting to be read, in block order (or backwards).</summary>
    private readonly PriorityQueue<Txid, (long Height, string Id)> _queue = new();

    /// <summary>The transactions in the queue.</summary>
    private readonly HashSet<Txid> _pending = [];

    /// <summary>Prepares a reading.</summary>
    /// <param name="request">The request.</param>
    /// <param name="method">The method.</param>
    public UtxoExposureRun(ExposureRequest request, IUtxoTracingMethod method)
    {
        _request = request;
        _method = method;
        _tally = new ExposureTally(request);
        foreach (var edge in request.Graph.Edges.Where(edge => edge.Kind == GraphEdgeKind.Transfer))
        {
            foreach (var transfer in edge.Transfers.Where(t => t.Amount.Asset == request.Asset && t.Coin is not null))
            {
                ListOf(transfer.Coin!).Add(edge);
            }
        }

        foreach (var known in request.Graph.Transactions.Values)
        {
            _tally.When(ChainTransactionRef.Of(known.Transaction.Txid), known.Transaction.Block?.Time);
        }
    }

    /// <summary>Gets the graph.</summary>
    private TraceGraph Graph => _request.Graph;

    /// <summary>Gets the subject.</summary>
    private string Subject => _request.Subject;

    /// <summary>Reads the exposure.</summary>
    /// <returns>The result.</returns>
    public ExposureResult Run()
    {
        var total = _tally.IsOut ? StartOut() : StartIn();
        while (_queue.TryDequeue(out var txid, out _))
        {
            _pending.Remove(txid);
            if (_tally.IsOut)
            {
                ReadOut(txid);
            }
            else
            {
                ReadIn(txid);
            }
        }

        if (_tally.IsOut)
        {
            CountOut();
        }
        else
        {
            CountIn();
        }

        return _tally.Result(total, _method.Id);
    }

    /// <summary>Gives the list of lines of a coin, making it when it is not there.</summary>
    /// <param name="coin">The coin.</param>
    /// <returns>The list.</returns>
    private List<GraphEdge> ListOf(Outpoint coin)
    {
        if (!_lines.TryGetValue(coin, out var list))
        {
            list = [];
            _lines[coin] = list;
        }

        return list;
    }

    /// <summary>Says whether a node is a transaction drawn as a node of its own.</summary>
    /// <param name="key">The node.</param>
    /// <returns><see langword="true"/> for one.</returns>
    private bool IsTransactionNode(string key) => Graph.Node(key)?.Kind == GraphNodeKind.Transaction;

    /// <summary>Gives the actor a coin belongs to: its address's node, or for one of the subject's the line's.</summary>
    /// <param name="coin">The coin.</param>
    /// <returns>The node, or <see langword="null"/> when its script has no address in the graph.</returns>
    private string? Owner(Outpoint coin)
    {
        if (_owners.TryGetValue(coin, out var seeded))
        {
            return seeded;
        }

        return Output(coin)?.Address is { } address ? Graph.NodeOf(address)?.Key : null;
    }

    /// <summary>Gives a coin's output, when the graph holds the transaction that made it.</summary>
    /// <param name="coin">The coin.</param>
    /// <returns>The output, or <see langword="null"/>.</returns>
    private BitcoinOutput? Output(Outpoint coin) =>
        Graph.Transactions.TryGetValue(coin.Txid, out var known)
            ? known.Transaction.Outputs.FirstOrDefault(output => output.Index == coin.Index)
            : null;

    /// <summary>Gives the transaction in the graph that spends a coin, or <see langword="null"/>.</summary>
    /// <param name="coin">The coin.</param>
    /// <returns>The transaction's id.</returns>
    private Txid? Spender(Outpoint coin) =>
        Graph.Spends.TryGetValue(coin, out var spend) && spend.SpentBy is { } by
        && Graph.Transactions.ContainsKey(by.Txid)
            ? by.Txid
            : null;

    /// <summary>
    /// Gives the node of a transaction drawn as a node of its own when it is a service (a CoinJoin), where the money
    /// stops as the graph's trace ends there.
    /// </summary>
    /// <param name="txid">The transaction.</param>
    /// <returns>The node's key, or <see langword="null"/>.</returns>
    private string? ServiceNodeOf(Txid txid)
    {
        var key = NodeKeys.Transaction(txid);
        return _tally.IsService(key) && IsTransactionNode(key) ? key : null;
    }

    /// <summary>
    /// Sends a coin's money on through a transaction, or stops it at the transaction's node when that is a service.
    /// </summary>
    /// <param name="coin">The coin.</param>
    /// <param name="owner">Its actor.</param>
    /// <param name="txid">The transaction it goes through: the one that spends it, or when got the one that made it.</param>
    private void GoThrough(Outpoint coin, string owner, Txid txid)
    {
        if (ServiceNodeOf(txid) is { } node)
        {
            _tally.Reach(node, owner);
            _stopAt[coin] = node;
            _passed.Remove(coin);
            return;
        }

        _passed.Add(coin);
        Queue(txid);
    }

    /// <summary>Queues a transaction, once, in block order (backwards when got).</summary>
    /// <param name="txid">The transaction.</param>
    private void Queue(Txid txid)
    {
        if (!_pending.Add(txid))
        {
            return;
        }

        var height = Graph.Transactions[txid].Transaction.Block?.Height ?? long.MaxValue;
        _queue.Enqueue(txid, _tally.IsOut ? (height, txid.Value) : (-height, Reverse(txid.Value)));
    }

    /// <summary>Turns an id so that the reverse order sorts first, for reading backwards.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The id with every hex digit turned.</returns>
    private static string Reverse(string id) => new([.. id.Select(digit => (char)('f' + '0' - digit))]);

    /// <summary>
    /// Paid out: the money is the coins on the subject's outgoing lines, each paid to another actor or spent by the
    /// subject into a transaction node.
    /// </summary>
    /// <returns>The total.</returns>
    private BigInteger StartOut()
    {
        var total = BigInteger.Zero;
        foreach (var edge in Graph.Edges.Where(edge => edge.From == Subject && edge.To != Subject))
        {
            foreach (var transfer in edge.Transfers.Where(t => t.Amount.Asset == _request.Asset && t.Coin is not null))
            {
                var coin = transfer.Coin!;
                var owner = IsTransactionNode(edge.To) ? Subject : edge.To;
                if (!_seeds.Add(coin))
                {
                    continue;
                }

                _owners[coin] = owner;
                _taint[coin] = TaintSet.FirstPart((long)transfer.Amount.Units);
                _through[coin] = [transfer.Transaction];
                total += transfer.Amount.Units;
                Arrived(coin, Subject, edge);
            }
        }

        return total;
    }

    /// <summary>Got: the money is the coins on the subject's incoming lines.</summary>
    /// <returns>The total.</returns>
    private BigInteger StartIn()
    {
        var total = BigInteger.Zero;
        foreach (var edge in Graph.Edges.Where(edge => edge.To == Subject && edge.From != Subject))
        {
            foreach (var transfer in edge.Transfers.Where(t => t.Amount.Asset == _request.Asset && t.Coin is not null))
            {
                var coin = transfer.Coin!;
                if (!_seeds.Add(coin))
                {
                    continue;
                }

                _owners[coin] = Subject;
                _units[coin] = transfer.Amount.Units;
                _through[coin] = [transfer.Transaction];
                total += transfer.Amount.Units;
                SettleIn(coin);
            }
        }

        return total;
    }

    /// <summary>
    /// Notes that money reached a coin's actor from another, through a transaction node when the coin's line comes
    /// from one, and queues the transaction that spends it when its actor is followed.
    /// </summary>
    /// <param name="coin">The coin.</param>
    /// <param name="from">The actor the money came from.</param>
    /// <param name="line">The line it came on, or <see langword="null"/>.</param>
    private void Arrived(Outpoint coin, string from, GraphEdge? line)
    {
        var owner = Owner(coin);
        if (owner is null)
        {
            return;
        }

        if (line is not null && line.From != from && IsTransactionNode(line.From))
        {
            _tally.Reach(line.From, from);
            from = line.From;
        }

        _tally.Reach(owner, from);
        var goesOn = _seeds.Contains(coin) ? owner == Subject || _tally.Follows(owner) : _tally.Follows(owner);
        if (goesOn && Spender(coin) is { } spender)
        {
            GoThrough(coin, owner, spender);
        }
    }

    /// <summary>
    /// Paid out: carries the money in a transaction's followed inputs to its outputs and fee by the method, and on to
    /// the coins' actors.
    /// </summary>
    /// <param name="txid">The transaction.</param>
    private void ReadOut(Txid txid)
    {
        var transaction = Graph.Transactions[txid].Transaction;
        var inputs = new List<TaintSet>();
        var through = new HashSet<ChainTransactionRef> { ChainTransactionRef.Of(txid) };
        string? from = null;
        foreach (var input in transaction.Inputs)
        {
            var coin = input.IsCoinbase ? null : new Outpoint(input.PreviousTxid!, input.PreviousIndex!.Value);
            var traced = coin is not null && _passed.Contains(coin) ? _taint[coin] : TaintSet.None;
            inputs.Add(traced);
            if (!traced.IsEmpty)
            {
                through.UnionWith(_through[coin!]);
                from ??= Owner(coin!);
            }
        }

        var taint = _method.Propagate(MethodChecks.Flow(transaction), inputs);
        _fees[txid] = taint.Fee;
        for (var index = 0; index < taint.Outputs.Count; index++)
        {
            var coin = new Outpoint(txid, transaction.Outputs[index].Index);
            if (taint.Outputs[index].IsEmpty || taint.Outputs[index].Equals(_taint.GetValueOrDefault(coin)))
            {
                continue;
            }

            _taint[coin] = taint.Outputs[index];
            _through[coin] = through;
            var line = _lines.GetValueOrDefault(coin)?.FirstOrDefault(edge => edge.To == Owner(coin));
            Arrived(coin, from ?? Subject, line);
        }
    }

    /// <summary>
    /// Got: shares the money in a transaction's followed outputs out over its inputs, each by how much of the output
    /// the method says came from it, and goes back to the inputs' actors.
    /// </summary>
    /// <param name="txid">The transaction.</param>
    private void ReadIn(Txid txid)
    {
        var transaction = Graph.Transactions[txid].Transaction;
        if (transaction.IsCoinbase)
        {
            return;
        }

        var flow = MethodChecks.Flow(transaction);
        var shares = transaction.Inputs.Select((_, index) => _method.Propagate(flow, [
            .. flow.Inputs.Select((amount, other) => other == index ? TaintSet.FirstPart(amount) : TaintSet.None),
        ]).Outputs.Select(set => set.Units).ToList()).ToList();
        var parts = new BigInteger[transaction.Inputs.Count];
        var through = transaction.Inputs.Select(_ => new HashSet<ChainTransactionRef> { ChainTransactionRef.Of(txid) })
            .ToList();
        for (var output = 0; output < transaction.Outputs.Count; output++)
        {
            var coin = new Outpoint(txid, transaction.Outputs[output].Index);
            if (!_passed.Contains(coin) || _units.GetValueOrDefault(coin).IsZero)
            {
                continue;
            }

            var split = Split(_units[coin], [.. shares.Select(share => share[output])]);
            for (var input = 0; input < split.Length; input++)
            {
                parts[input] += split[input];
                if (!split[input].IsZero)
                {
                    through[input].UnionWith(_through[coin]);
                }
            }
        }

        for (var index = 0; index < transaction.Inputs.Count; index++)
        {
            var input = transaction.Inputs[index];
            var coin = new Outpoint(input.PreviousTxid!, input.PreviousIndex!.Value);
            if (parts[index] == _units.GetValueOrDefault(coin))
            {
                continue;
            }

            _units[coin] = parts[index];
            _through[coin] = through[index];
            SettleIn(coin, input.Address is { } address ? Graph.NodeOf(address)?.Key : null, txid);
        }
    }

    /// <summary>
    /// Shares whole units out in proportion to weights, the remainders to the largest fractions, so that nothing is
    /// created or lost.
    /// </summary>
    /// <param name="units">The units.</param>
    /// <param name="weights">The weights.</param>
    /// <returns>Each weight's part.</returns>
    internal static BigInteger[] Split(BigInteger units, IReadOnlyList<long> weights)
    {
        var parts = new BigInteger[weights.Count];
        var whole = weights.Aggregate(BigInteger.Zero, (sum, weight) => sum + weight);
        if (whole.IsZero)
        {
            return parts;
        }

        var remainders = new BigInteger[weights.Count];
        for (var index = 0; index < weights.Count; index++)
        {
            parts[index] = BigInteger.DivRem(units * weights[index], whole, out remainders[index]);
        }

        var left = units - parts.Aggregate(BigInteger.Zero, (sum, part) => sum + part);
        foreach (var index in Enumerable.Range(0, weights.Count).OrderByDescending(i => remainders[i]).ThenBy(i => i))
        {
            if (left.IsZero)
            {
                break;
            }

            parts[index] += 1;
            left -= 1;
        }

        return parts;
    }

    /// <summary>
    /// Got: notes that the money in a coin came from its actor, and queues the transaction that made it when the actor
    /// is followed.
    /// </summary>
    /// <param name="coin">The coin.</param>
    /// <param name="owner">Its actor, or <see langword="null"/> to look it up.</param>
    /// <param name="spender">The transaction it was spent in, or <see langword="null"/> for one of the subject's.</param>
    private void SettleIn(Outpoint coin, string? owner = null, Txid? spender = null)
    {
        owner ??= Owner(coin);
        if (owner is null)
        {
            return;
        }

        _owners.TryAdd(coin, owner);

        if (spender is not null)
        {
            var line = _lines.GetValueOrDefault(coin)?.FirstOrDefault(edge => edge.From == owner);
            ReachBack(owner, spender, line);
        }

        var goesOn = _seeds.Contains(coin) || _tally.Follows(owner);
        if (goesOn && Graph.Transactions.TryGetValue(coin.Txid, out var known) && !known.Transaction.IsCoinbase)
        {
            GoThrough(coin, owner, coin.Txid);
        }
    }

    /// <summary>
    /// Got: notes the hops of an input's actor from the actor of the outputs it paid, through a transaction node when the
    /// transaction is drawn as one.
    /// </summary>
    /// <param name="owner">The input's actor.</param>
    /// <param name="spender">The transaction.</param>
    /// <param name="line">A line of the coin, or <see langword="null"/>.</param>
    private void ReachBack(string owner, Txid spender, GraphEdge? line)
    {
        var from = NearOf(spender) ?? Subject;
        if (line is not null && IsTransactionNode(line.To))
        {
            _tally.Reach(line.To, from);
            from = line.To;
        }

        _tally.Reach(owner, from);
    }

    /// <summary>Gives the actor of a transaction's outputs that carried the money towards the subject.</summary>
    /// <param name="txid">The transaction.</param>
    /// <returns>The actor nearest the subject, or <see langword="null"/>.</returns>
    private string? NearOf(Txid txid)
    {
        var transaction = Graph.Transactions[txid].Transaction;
        return transaction.Outputs
            .Select(output => new Outpoint(txid, output.Index))
            .Where(coin => _passed.Contains(coin))
            .Select(Owner)
            .OfType<string>()
            .Where(owner => _tally.Hops.ContainsKey(owner))
            .MinBy(owner => _tally.Hops[owner]);
    }

    /// <summary>
    /// Paid out: counts where every coin with money stopped, the fees, and the money per line; a coin that went on
    /// through a transaction read is counted in that transaction's outputs.
    /// </summary>
    private void CountOut()
    {
        foreach (var (coin, taint) in _taint)
        {
            if (taint.IsEmpty)
            {
                continue;
            }

            var owner = Owner(coin);
            foreach (var line in _lines.GetValueOrDefault(coin, []))
            {
                var spentIntoNode = IsTransactionNode(line.To) && (_passed.Contains(coin) || _stopAt.ContainsKey(coin));
                if (line.To == owner || spentIntoNode)
                {
                    _tally.Flow(line, taint.Units);
                }
            }

            if (!_passed.Contains(coin))
            {
                var unspent = Graph.Spends.TryGetValue(coin, out var spend) && spend.State == SpendState.Unspent;
                Stop(coin, owner, taint.Units, unspent ? ExposureRest.Remains : ExposureRest.NotExpanded);
            }
        }

        foreach (var fee in _fees.Values)
        {
            _tally.Rest(ExposureRest.Fees, fee);
        }
    }

    /// <summary>
    /// Got: counts where every coin with money stopped, and the money per line: a coin's money moved on its line from
    /// the input's actor to the output's.
    /// </summary>
    private void CountIn()
    {
        foreach (var (coin, units) in _units)
        {
            if (units.IsZero)
            {
                continue;
            }

            if (_passed.Contains(coin))
            {
                FlowsBack(coin);
                continue;
            }

            if (_stopAt.ContainsKey(coin))
            {
                var owner = Owner(coin);
                foreach (var line in _lines.GetValueOrDefault(coin, []).Where(edge => edge.To == owner))
                {
                    _tally.Flow(line, units);
                }
            }

            var minted = Graph.Transactions.TryGetValue(coin.Txid, out var known) && known.Transaction.IsCoinbase;
            Stop(coin, Owner(coin), units, minted ? ExposureRest.BeforeHistory : ExposureRest.NotExpanded);
        }
    }

    /// <summary>
    /// Got: counts a coin that went back through its making transaction on the lines that paid it: from the inputs'
    /// actors to its own, or through the transaction node.
    /// </summary>
    /// <param name="coin">The coin.</param>
    private void FlowsBack(Outpoint coin)
    {
        var transaction = Graph.Transactions[coin.Txid].Transaction;
        foreach (var input in transaction.Inputs)
        {
            var previous = new Outpoint(input.PreviousTxid!, input.PreviousIndex!.Value);
            var part = _units.GetValueOrDefault(previous);
            if (part.IsZero)
            {
                continue;
            }

            foreach (var line in _lines.GetValueOrDefault(previous, []).Where(edge => IsTransactionNode(edge.To)))
            {
                _tally.Flow(line, part);
            }
        }

        var owner = Owner(coin);
        var lines = _lines.GetValueOrDefault(coin, []).Where(edge => edge.To == owner).ToList();
        foreach (var line in lines)
        {
            var from = IsTransactionNode(line.From)
                ? _units.GetValueOrDefault(coin)
                : FromActor(transaction, line.From);
            _tally.Flow(line, BigInteger.Min(from, _units.GetValueOrDefault(coin)));
        }
    }

    /// <summary>Gives the money that came from one actor's inputs of a transaction.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="actor">The actor.</param>
    /// <returns>The units.</returns>
    private BigInteger FromActor(BitcoinTransaction transaction, string actor) => transaction.Inputs
        .Where(input => input.Address is { } address && Graph.NodeOf(address)?.Key == actor)
        .Select(input => _units.GetValueOrDefault(new Outpoint(input.PreviousTxid!, input.PreviousIndex!.Value)))
        .Aggregate(BigInteger.Zero, (sum, units) => sum + units);

    /// <summary>
    /// Counts a coin's money where it stopped: at a service, back at the subject, at an actor not followed or without
    /// an address, or with a followed actor whose way on (or back) the graph does not hold.
    /// </summary>
    /// <param name="coin">The coin.</param>
    /// <param name="owner">Its actor, or <see langword="null"/>.</param>
    /// <param name="units">How much.</param>
    /// <param name="stays">Where money that stays with a followed actor read in full ends.</param>
    private void Stop(Outpoint coin, string? owner, BigInteger units, ExposureRest stays)
    {
        var through = _through.GetValueOrDefault(coin, []);
        if (_stopAt.TryGetValue(coin, out var node))
        {
            _tally.Arrive(node, units, through);
            return;
        }

        if (_seeds.Contains(coin) && owner == Subject)
        {
            // One of the subject's own coins whose way on or back the graph does not hold.
            _tally.Rest(ExposureRest.NotExpanded, units);
            return;
        }

        if (owner is null || owner == Subject || _tally.IsService(owner) || !_tally.Follows(owner))
        {
            _tally.Arrive(owner, units, through);
            return;
        }

        _tally.Rest(_tally.StaysWith(owner, stays), units);
    }
}
