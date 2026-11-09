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

    /// <summary>
    /// Got: how much of the money read back reached one of the subject's own coins the money starts in, and so came
    /// from the subject itself; kept apart from the coin's own money, which goes on back.
    /// </summary>
    private readonly Dictionary<Outpoint, BigInteger> _returned = [];

    /// <summary>The transactions the money in each coin went through.</summary>
    private readonly Dictionary<Outpoint, HashSet<ChainTransactionRef>> _through = [];

    /// <summary>The coins whose money goes on through a transaction the run reads.</summary>
    private readonly HashSet<Outpoint> _passed = [];

    /// <summary>
    /// The coins whose money stops at a transaction drawn as a node that is a service (a CoinJoin): the node, by coin.
    /// </summary>
    private readonly Dictionary<Outpoint, string> _stopAt = [];

    /// <summary>
    /// The coins whose money stops by a rule of the graph's trace (<see cref="SpendRules"/>) with an actor that is
    /// followed: why, by coin.
    /// </summary>
    private readonly Dictionary<Outpoint, ExposureRest> _ended = [];

    /// <summary>
    /// Got: the money each transaction read back owes to inputs the user linked to another chain, which by the trace
    /// went on there and are no source of it; set anew each time the transaction is read.
    /// </summary>
    private readonly Dictionary<Txid, BigInteger> _onOtherChain = [];

    /// <summary>The fee each transaction read took of the money.</summary>
    private readonly Dictionary<Txid, long> _fees = [];

    /// <summary>Paid out: the money that went into a shielded pool (Zcash) by transaction, where the trail ends.</summary>
    private readonly Dictionary<Txid, BigInteger> _pooledOut = [];

    /// <summary>
    /// Got: the money in a transaction's outputs that came out of a shielded pool, by transaction, where the trail
    /// ends going back.
    /// </summary>
    private readonly Dictionary<Txid, BigInteger> _pooledIn = [];

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
        foreach (var edge in request.Graph.Edges.Where(
            edge => edge.Kind is GraphEdgeKind.Transfer or GraphEdgeKind.ContinuesOnOtherChain))
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

    /// <summary>Gives the input in the graph that spends a coin, or <see langword="null"/>.</summary>
    /// <param name="coin">The coin.</param>
    /// <returns>The transaction's id and the input's index.</returns>
    private BitcoinSpend? Spender(Outpoint coin) =>
        SpendRules.SpentBy(Graph, coin) is { } by && Graph.Transactions.ContainsKey(by.Txid) ? by : null;

    /// <summary>
    /// Says whether a coin the money starts in was made by a transaction the source no longer knows, and stops it there
    /// as the graph's trace does (<see cref="SpendRules.MadeByGone"/>): it is left out when a replacer is in the graph,
    /// whose own coins carry the money, and otherwise stops where the trail ends, or as not adding up when the answers
    /// do not settle that it is gone.
    /// </summary>
    /// <param name="coin">The coin.</param>
    /// <param name="counts">Whether the coin is counted at all.</param>
    /// <returns><see langword="true"/> when the coin's transaction is gone.</returns>
    private bool StopsAtGone(Outpoint coin, out bool counts)
    {
        counts = true;
        if (SpendRules.MadeByGone(Graph, coin) is not { } made)
        {
            return false;
        }

        counts = made.Counts;
        _ended[coin] = made.Gone.IsSettled ? ExposureRest.TrailEnds : ExposureRest.Conflicting;
        return true;
    }

    /// <summary>
    /// Paid out: sends a coin the user linked to another chain there, where it stops, whoever its actor is (a service
    /// or one beyond the depth too), since the graph's trace looks at the link first.
    /// </summary>
    /// <param name="coin">The coin.</param>
    /// <param name="owner">Its actor.</param>
    /// <returns><see langword="true"/> when the coin is linked.</returns>
    private bool LinkedOn(Outpoint coin, string owner)
    {
        if (SpendRules.Continuations(Graph, coin) is not [var link, ..])
        {
            return false;
        }

        var node = NodeKeys.OtherChain(link.To);
        if (Graph.Node(node) is not null)
        {
            _tally.Reach(node, owner);
            _stopAt[coin] = node;
        }
        else
        {
            _ended[coin] = ExposureRest.OtherChain;
        }

        return true;
    }

    /// <summary>
    /// Paid out: sends a followed actor's coin on as the graph's trace does (<see cref="SpendRules"/>): to the other
    /// chain it is linked to, where it stops; not at all when the spend disagrees with its transaction or the trail
    /// ends there; otherwise through the transaction that spends it.
    /// </summary>
    /// <param name="coin">The coin.</param>
    /// <param name="owner">Its actor.</param>
    private void GoOn(Outpoint coin, string owner)
    {
        if (Spender(coin) is not { } by || Output(coin) is not { } output)
        {
            return;
        }

        if (SpendRules.IsConflicting(Graph.Transactions[by.Txid].Transaction, by.InputIndex, coin, output)
            || Graph.Gone.TryGetValue(by.Txid, out var gone) && !gone.IsSettled)
        {
            _ended[coin] = ExposureRest.Conflicting;
        }
        else if (SpendRules.EndsAt(Graph, by.Txid).Count > 0 && ServiceNodeOf(by.Txid) is null)
        {
            _ended[coin] = ExposureRest.TrailEnds;
        }
        else
        {
            GoThrough(coin, owner, by.Txid);
        }
    }

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
                var gone = StopsAtGone(coin, out var counts);
                if (!counts || !_seeds.Add(coin))
                {
                    continue;
                }

                _owners[coin] = owner;
                _taint[coin] = TaintSet.FirstPart((long)transfer.Amount.Units);
                _through[coin] = [transfer.Transaction];
                total += transfer.Amount.Units;
                if (!gone)
                {
                    Arrived(coin, Subject, edge);
                }
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
                var gone = StopsAtGone(coin, out var counts);
                if (!counts || !_seeds.Add(coin))
                {
                    continue;
                }

                _owners[coin] = Subject;
                _units[coin] = transfer.Amount.Units;
                _through[coin] = [transfer.Transaction];
                total += transfer.Amount.Units;
                if (!gone)
                {
                    SettleIn(coin);
                }
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
        if (LinkedOn(coin, owner))
        {
            return;
        }

        var goesOn = _seeds.Contains(coin) ? owner == Subject || _tally.Follows(owner) : _tally.Follows(owner);
        if (goesOn)
        {
            GoOn(coin, owner);
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

        if (MethodChecks.HasPoolInput(transaction))
        {
            // What comes out of a shielded pool is never the subject's money: the pool is the flow's last input.
            inputs.Add(TaintSet.None);
        }

        var taint = _method.Propagate(MethodChecks.Flow(transaction), inputs);
        _fees[txid] = taint.Fee;
        _pooledOut[txid] = MethodChecks.HasPoolOutput(transaction) ? taint.Outputs[^1].Units : BigInteger.Zero;
        for (var index = 0; index < transaction.Outputs.Count; index++)
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

        if (SpendRules.EndsAt(Graph, txid).Count > 0 && ServiceNodeOf(txid) is null)
        {
            // The graph's trace stops at the transaction; the money in its outputs is not followed back through it.
            EndBack(txid, ExposureRest.TrailEnds);
            return;
        }

        var flow = MethodChecks.Flow(transaction);
        // One share per input of the flow: a shielded pool's is its last, and its part is where the trail ends.
        var shares = flow.Inputs.Select((_, index) => _method.Propagate(flow, [
            .. flow.Inputs.Select((amount, other) => other == index ? TaintSet.FirstPart(amount) : TaintSet.None),
        ]).Outputs.Select(set => set.Units).ToList()).ToList();
        var fromPool = BigInteger.Zero;
        var linked = transaction.Inputs.Select(IsLinkedInput).ToList();
        var onOtherChain = BigInteger.Zero;
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
            if (split.Length > transaction.Inputs.Count)
            {
                fromPool += split[^1];
            }

            for (var input = 0; input < transaction.Inputs.Count; input++)
            {
                if (linked[input])
                {
                    // The linked input's share: by the trace its money went on to another chain, not into this.
                    onOtherChain += split[input];
                    continue;
                }

                parts[input] += split[input];
                if (!split[input].IsZero)
                {
                    through[input].UnionWith(_through[coin]);
                }
            }
        }

        _onOtherChain[txid] = onOtherChain;
        _pooledIn[txid] = fromPool;
        for (var index = 0; index < transaction.Inputs.Count; index++)
        {
            var input = transaction.Inputs[index];
            var coin = new Outpoint(input.PreviousTxid!, input.PreviousIndex!.Value);
            if (parts[index] == Back(coin))
            {
                continue;
            }

            if (_seeds.Contains(coin))
            {
                _returned[coin] = parts[index];
                continue;
            }

            _units[coin] = parts[index];
            _through[coin] = through[index];
            if (SpendsElsewhere(coin, txid, index))
            {
                // The graph says the coin went another way than this input, as the trace would stop at it.
                _ended[coin] = ExposureRest.Conflicting;
                continue;
            }

            SettleIn(coin, input.Address is { } address ? Graph.NodeOf(address)?.Key : null, txid);
        }
    }

    /// <summary>
    /// Got: says whether what the graph says about a coin's spend disagrees with the input that spends it here: it
    /// names another transaction or input, or the input does not agree with the coin. A coin whose spend the graph
    /// does not know is read back as the transaction says.
    /// </summary>
    /// <param name="coin">The coin.</param>
    /// <param name="txid">The transaction whose input spends it.</param>
    /// <param name="index">The input.</param>
    /// <returns><see langword="true"/> when they disagree.</returns>
    private bool SpendsElsewhere(Outpoint coin, Txid txid, int index)
    {
        // As the graph's trace counts the spend: a followed replacer's input in place of a gone transaction's.
        if (SpendRules.SpentBy(Graph, coin) is not { } by)
        {
            return false;
        }

        return by.Txid != txid || by.InputIndex != index
            || (Output(coin) is { } output
                && SpendRules.IsConflicting(Graph.Transactions[txid].Transaction, index, coin, output));
    }

    /// <summary>
    /// Got: says whether an input spends a coin the user linked to another chain; by the trace its money went on there,
    /// so it is no source of the transaction's outputs, as a linked transfer is none of a wallet's on an account chain.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <returns><see langword="true"/> when it is linked.</returns>
    private bool IsLinkedInput(BitcoinInput input) =>
        !input.IsCoinbase
        && SpendRules.Continuations(Graph, new Outpoint(input.PreviousTxid!, input.PreviousIndex!.Value)).Count > 0;

    /// <summary>Got: stops the money in a transaction's followed outputs there, for a reason.</summary>
    /// <param name="txid">The transaction.</param>
    /// <param name="why">Why the money stops.</param>
    private void EndBack(Txid txid, ExposureRest why)
    {
        var transaction = Graph.Transactions[txid].Transaction;
        foreach (var coin in transaction.Outputs.Select(output => new Outpoint(txid, output.Index)))
        {
            if (_passed.Remove(coin))
            {
                _ended[coin] = why;
            }
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
        if (!_seeds.Contains(coin) && StopsAtGone(coin, out _))
        {
            // Made by a transaction that is not in the chain: it is not read back through it.
            return;
        }

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
                var linkedOn = line.Kind == GraphEdgeKind.ContinuesOnOtherChain
                    && _stopAt.GetValueOrDefault(coin) == line.To;
                if (line.To == owner || spentIntoNode || linkedOn)
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

        TallyPooled(_pooledOut);
    }

    /// <summary>Counts the money that went into or came out of a shielded pool as where the trail ends.</summary>
    /// <param name="pooled">The money by transaction.</param>
    private void TallyPooled(Dictionary<Txid, BigInteger> pooled)
    {
        foreach (var units in pooled.Values.Where(units => !units.IsZero))
        {
            _tally.Rest(ExposureRest.TrailEnds, units);
        }
    }

    /// <summary>
    /// Got: counts where every coin with money stopped, and the money per line: a coin's money moved on its line from
    /// the input's actor to the output's.
    /// </summary>
    private void CountIn()
    {
        foreach (var units in _onOtherChain.Values)
        {
            _tally.Rest(ExposureRest.OtherChain, units);
        }

        TallyPooled(_pooledIn);

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

            if (_stopAt.ContainsKey(coin) || _ended.GetValueOrDefault(coin) == ExposureRest.TrailEnds)
            {
                // The money came on the coin's line to its actor, from a transaction it is not followed back through.
                var owner = Owner(coin);
                foreach (var line in _lines.GetValueOrDefault(coin, []).Where(edge => edge.To == owner))
                {
                    _tally.Flow(line, units);
                }
            }

            var minted = Graph.Transactions.TryGetValue(coin.Txid, out var known) && known.Transaction.IsCoinbase;
            Stop(coin, Owner(coin), units, minted ? ExposureRest.BeforeHistory : ExposureRest.NotExpanded);
        }

        foreach (var units in _returned.Values)
        {
            _tally.Rest(ExposureRest.Returned, units);
        }
    }

    /// <summary>
    /// Got: gives the money read back into a coin from the transaction it was spent in: for one of the subject's own
    /// coins the money starts in, what came from the subject itself; for any other coin, its money.
    /// </summary>
    /// <param name="coin">The coin.</param>
    /// <returns>The units.</returns>
    private BigInteger Back(Outpoint coin) =>
        _seeds.Contains(coin) ? _returned.GetValueOrDefault(coin) : _units.GetValueOrDefault(coin);

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
            var part = Back(previous);
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
        .Select(input => Back(new Outpoint(input.PreviousTxid!, input.PreviousIndex!.Value)))
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

        if (_ended.TryGetValue(coin, out var why))
        {
            _tally.Rest(why, units);
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
