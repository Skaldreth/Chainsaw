using System.Globalization;
using System.Runtime.CompilerServices;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks.Contracts;

/// <summary>
/// The graph's shape as a pattern analysis reads it (<see cref="IGraphShapeAnalysis"/>): the actors, what sort the
/// labels say each is, and the lines of money between them with amounts, blocks, the coins they spend and pay, and the
/// clustering's conclusions. It is read only from what the case holds (<see cref="CheckInput"/>); the actors are the
/// clustering's owners and the labels' services, so the shape is a judgement built on facts (principle 2), and every
/// line names its transaction, block and coins so that it can be checked against the chain (principle 1).
/// </summary>
/// <remarks>
/// A transaction with one payer gives a line from the payer to each other owner it paid; change back to the payer is
/// no line but is told by <see cref="ShapeFlow.ChangeBack"/>. A transaction with several payers (for example a
/// CoinJoin) is an actor of its own (<see cref="ActorSort.Transaction"/>) with a line in from each payer and a line out
/// to each payee, all marked <see cref="ShapeFlow.IsShared"/>, since it is not known whose money went where; it never
/// gives a line from every payer to every payee.
/// </remarks>
public sealed class GraphShape
{
    /// <summary>The shapes already read, so that several analyses over the same input read it once.</summary>
    private static readonly ConditionalWeakTable<CheckInput, GraphShape> Read = [];

    /// <summary>The lines out of each actor, in time order.</summary>
    private readonly int[][] _out;

    /// <summary>The lines into each actor, in time order.</summary>
    private readonly int[][] _in;

    /// <summary>The line that paid each coin, by the coin.</summary>
    private readonly Dictionary<ShapeCoin, int> _payerOf = [];

    /// <summary>The lines (with one payer) that spend each coin, in time order.</summary>
    private readonly Dictionary<ShapeCoin, List<int>> _spentBy = [];

    /// <summary>Creates a shape from its actors and lines.</summary>
    /// <param name="actors">The actors; each one's <see cref="ShapeActor.Index"/> is its place in the list.</param>
    /// <param name="flows">
    /// The lines in time order; each one's <see cref="ShapeFlow.Index"/> is its place, and its ends name actors.
    /// </param>
    /// <exception cref="ArgumentException">An index does not match its place, or a line names no actor.</exception>
    public GraphShape(IReadOnlyList<ShapeActor> actors, IReadOnlyList<ShapeFlow> flows)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(flows);
        if (actors.Where((actor, index) => actor.Index != index).Any()
            || flows.Where((flow, index) => flow.Index != index).Any())
        {
            throw new ArgumentException("Every actor and line has its place in the list as its index.");
        }

        if (flows.Any(flow => (uint)flow.From >= (uint)actors.Count || (uint)flow.To >= (uint)actors.Count))
        {
            throw new ArgumentException("Every line goes between two of the actors.", nameof(flows));
        }

        Actors = actors;
        Flows = flows;
        _out = Index(actors.Count, flows, flow => flow.From);
        _in = Index(actors.Count, flows, flow => flow.To);
        foreach (var flow in flows.Where(flow => !flow.IsShared))
        {
            foreach (var coin in flow.Paid)
            {
                _payerOf.TryAdd(coin, flow.Index);
            }

            foreach (var coin in flow.Spent)
            {
                if (!_spentBy.TryGetValue(coin, out var spenders))
                {
                    _spentBy[coin] = spenders = [];
                }

                spenders.Add(flow.Index);
            }
        }
    }

    /// <summary>Gets the actors.</summary>
    public IReadOnlyList<ShapeActor> Actors { get; }

    /// <summary>
    /// Gets the lines in time order: by block height, and within a block a transaction that spends another's output
    /// after it, then by txid (the source does not give the position in the block); unconfirmed last.
    /// </summary>
    public IReadOnlyList<ShapeFlow> Flows { get; }

    /// <summary>Gives the lines out of an actor, in time order.</summary>
    /// <param name="actor">The actor's index.</param>
    /// <returns>The lines' indexes.</returns>
    public IReadOnlyList<int> Out(int actor) => _out[actor];

    /// <summary>Gives the lines into an actor, in time order.</summary>
    /// <param name="actor">The actor's index.</param>
    /// <returns>The lines' indexes.</returns>
    public IReadOnlyList<int> In(int actor) => _in[actor];

    /// <summary>
    /// Whether a line spends a coin that another line paid: the money of <paramref name="next"/> is, coin for coin,
    /// at least partly the money of <paramref name="before"/> (fact, read from the inputs).
    /// </summary>
    /// <param name="before">The earlier line.</param>
    /// <param name="next">The later line.</param>
    /// <returns>Whether they are linked by a coin.</returns>
    public static bool Spends(ShapeFlow before, ShapeFlow next)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(next);
        return next.Spent.Any(before.Paid.Contains);
    }

    /// <summary>
    /// Gives the lines (with one payer) that spend a coin, in time order: one transaction spends it, but it may pay
    /// several actors.
    /// </summary>
    /// <param name="coin">The coin.</param>
    /// <returns>The lines' indexes; empty when no line in the shape spends it.</returns>
    public IReadOnlyList<int> SpentBy(ShapeCoin coin) => _spentBy.TryGetValue(coin, out var lines) ? lines : [];

    /// <summary>Gives the line (with one payer) that paid a coin, when the shape has it.</summary>
    /// <param name="coin">The coin.</param>
    /// <returns>The line's index, or <see langword="null"/>.</returns>
    public int? PayerOf(ShapeCoin coin) => _payerOf.TryGetValue(coin, out var index) ? index : null;

    /// <summary>
    /// Reads the shape of what the case holds, once per input: the labels' mixers and services are actors of their
    /// own (with every address the clustering links to them), every other address is its owner's, and the start
    /// points and victims are <see cref="ActorSort.Protected"/>.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <returns>The shape.</returns>
    public static GraphShape Of(CheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Read.GetValue(input, Build);
    }

    /// <summary>Builds the shape of an input (see <see cref="Of"/>).</summary>
    /// <param name="input">The input.</param>
    /// <returns>The shape.</returns>
    private static GraphShape Build(CheckInput input)
    {
        var actors = new ActorTable(input);
        var flows = new List<ShapeFlow>();
        foreach (var transaction in InTimeOrder(input.Transactions))
        {
            AddFlows(transaction, actors, input, flows);
        }

        return new GraphShape(actors.Finish(input.Protected), flows);
    }

    /// <summary>
    /// Orders the transactions by block height, and within a block so that a transaction that spends another's output
    /// comes after it (ties by txid); unconfirmed last, in the same way. Coinbases are left out.
    /// </summary>
    /// <param name="transactions">The transactions.</param>
    /// <returns>The transactions in order.</returns>
    private static IEnumerable<BitcoinTransaction> InTimeOrder(IReadOnlyList<BitcoinTransaction> transactions)
    {
        var blocks = transactions
            .Where(transaction => !transaction.IsCoinbase)
            .GroupBy(transaction => transaction.Block?.Height ?? long.MaxValue)
            .OrderBy(group => group.Key);
        foreach (var block in blocks)
        {
            foreach (var transaction in SpendersAfter([.. block.DistinctBy(each => each.Txid)]))
            {
                yield return transaction;
            }
        }
    }

    /// <summary>Orders a block's transactions so that each comes after those whose outputs it spends.</summary>
    /// <param name="block">The block's transactions.</param>
    /// <returns>The transactions in order; ties by txid.</returns>
    private static List<BitcoinTransaction> SpendersAfter(List<BitcoinTransaction> block)
    {
        var byId = block.ToDictionary(transaction => transaction.Txid);
        var waitsFor = block.ToDictionary(
            transaction => transaction.Txid,
            transaction => transaction.Inputs.Select(spent => spent.PreviousTxid)
                .OfType<Txid>()
                .Where(previous => previous != transaction.Txid && byId.ContainsKey(previous))
                .ToHashSet());
        var ready = new SortedSet<string>(
            waitsFor.Where(entry => entry.Value.Count == 0).Select(entry => entry.Key.Value), StringComparer.Ordinal);
        var ordered = new List<BitcoinTransaction>(block.Count);
        var placed = new HashSet<Txid>();
        while (ready.Count > 0)
        {
            var next = byId[Txid.Parse(ready.Min!)];
            ready.Remove(ready.Min!);
            ordered.Add(next);
            placed.Add(next.Txid);
            foreach (var (txid, waits) in waitsFor)
            {
                if (waits.Remove(next.Txid) && waits.Count == 0)
                {
                    ready.Add(txid.Value);
                }
            }
        }

        // A cycle cannot happen on a chain; should the data hold one, what is left keeps the txid order.
        ordered.AddRange(block.Where(transaction => !placed.Contains(transaction.Txid))
            .OrderBy(transaction => transaction.Txid.Value, StringComparer.Ordinal));
        return ordered;
    }

    /// <summary>
    /// Draws the lines of one transaction: from its payer to each other owner paid, or, with several payers, in from
    /// each to the transaction's own actor and out from it to each payee.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="actors">The actors met so far.</param>
    /// <param name="input">The input, for the clustering's conclusions.</param>
    /// <param name="flows">The lines drawn so far, to add to.</param>
    private static void AddFlows(
        BitcoinTransaction transaction, ActorTable actors, CheckInput input, List<ShapeFlow> flows)
    {
        var payers = transaction.Inputs
            .OrderBy(spent => spent.Index)
            .Where(spent => spent.Address is not null)
            .GroupBy(spent => actors.Of(spent.Address!))
            .ToList();
        if (payers.Count == 0)
        {
            return;
        }

        var payees = transaction.Outputs
            .Where(output => output.Address is not null)
            .OrderBy(output => output.Index)
            .GroupBy(output => actors.Of(output.Address!))
            .ToList();
        IReadOnlyList<string> conclusions = input.Conclusions.TryGetValue(transaction.Txid, out var stored)
            ? [.. stored.Findings.Select(found => found.Heuristic.Name).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)]
            : [];
        var line = new LineDraft(transaction, conclusions, flows);
        if (payers.Count == 1)
        {
            var payer = payers[0];
            var changeBack = payees.Any(payee => payee.Key == payer.Key);
            foreach (var payee in payees.Where(payee => payee.Key != payer.Key))
            {
                line.Add(payer.Key, payee.Key, Sum(payee), false, AddressesOf(payer), CoinsOf(payer),
                    AddressesOf(payee), CoinsOf(transaction, payee), changeBack);
            }

            return;
        }

        // Several payers: the transaction is an actor of its own, so that its lines are payers plus payees.
        var middle = actors.OfTransaction(transaction.Txid);
        var allPaid = transaction.Outputs.Select(output => new ShapeCoin(transaction.Txid, output.Index)).ToList();
        var allSpent = payers.SelectMany(CoinsOf).ToList();
        foreach (var payer in payers)
        {
            var amount = payer.Aggregate(Satoshi.Zero, (sum, spent) => sum + (spent.Amount ?? Satoshi.Zero));
            line.Add(payer.Key, middle, amount, true, AddressesOf(payer), CoinsOf(payer), [], allPaid, false);
        }

        foreach (var payee in payees)
        {
            line.Add(middle, payee.Key, Sum(payee), true, [], allSpent, AddressesOf(payee),
                CoinsOf(transaction, payee), false);
        }
    }

    /// <summary>Sums a payee's outputs.</summary>
    /// <param name="payee">The outputs.</param>
    /// <returns>The sum.</returns>
    private static Satoshi Sum(IEnumerable<BitcoinOutput> payee) =>
        payee.Aggregate(Satoshi.Zero, (sum, output) => sum + output.Amount);

    /// <summary>Gives a payer's addresses, each once.</summary>
    /// <param name="payer">The payer's inputs.</param>
    /// <returns>The addresses.</returns>
    private static List<BitcoinAddress> AddressesOf(IEnumerable<BitcoinInput> payer) =>
        [.. payer.Select(spent => spent.Address!).Distinct()];

    /// <summary>Gives a payee's addresses, each once.</summary>
    /// <param name="payee">The payee's outputs.</param>
    /// <returns>The addresses.</returns>
    private static List<BitcoinAddress> AddressesOf(IEnumerable<BitcoinOutput> payee) =>
        [.. payee.Select(output => output.Address!).Distinct()];

    /// <summary>Gives the coins a payer's inputs spend.</summary>
    /// <param name="payer">The payer's inputs.</param>
    /// <returns>The coins.</returns>
    private static List<ShapeCoin> CoinsOf(IEnumerable<BitcoinInput> payer) =>
        [.. payer.Where(spent => spent is { PreviousTxid: not null, PreviousIndex: not null })
            .Select(spent => new ShapeCoin(spent.PreviousTxid!, spent.PreviousIndex!.Value))];

    /// <summary>Gives the coins a payee's outputs make.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="payee">The payee's outputs.</param>
    /// <returns>The coins.</returns>
    private static List<ShapeCoin> CoinsOf(BitcoinTransaction transaction, IEnumerable<BitcoinOutput> payee) =>
        [.. payee.Select(output => new ShapeCoin(transaction.Txid, output.Index))];

    /// <summary>Indexes the lines by one of their ends.</summary>
    /// <param name="count">How many actors.</param>
    /// <param name="flows">The lines, in time order.</param>
    /// <param name="end">The end to index by.</param>
    /// <returns>The lines of each actor, in time order.</returns>
    private static int[][] Index(int count, IReadOnlyList<ShapeFlow> flows, Func<ShapeFlow, int> end)
    {
        var lists = new List<int>[count];
        for (var actor = 0; actor < count; actor++)
        {
            lists[actor] = [];
        }

        foreach (var flow in flows)
        {
            lists[end(flow)].Add(flow.Index);
        }

        return [.. lists.Select(list => list.ToArray())];
    }

    /// <summary>Adds the lines of one transaction, with what they share.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="conclusions">The clustering's conclusions about it.</param>
    /// <param name="flows">The lines, to add to.</param>
    private sealed class LineDraft(
        BitcoinTransaction transaction, IReadOnlyList<string> conclusions, List<ShapeFlow> flows)
    {
        /// <summary>Adds a line.</summary>
        /// <param name="from">The paying actor.</param>
        /// <param name="to">The paid actor.</param>
        /// <param name="amount">The amount.</param>
        /// <param name="shared">Whether the transaction has several payers.</param>
        /// <param name="fromAddresses">The payer's addresses.</param>
        /// <param name="spent">The coins the payer spent.</param>
        /// <param name="toAddresses">The payee's addresses.</param>
        /// <param name="paid">The coins the payee got.</param>
        /// <param name="changeBack">Whether the payer got change back.</param>
        public void Add(
            int from,
            int to,
            Satoshi amount,
            bool shared,
            IReadOnlyList<BitcoinAddress> fromAddresses,
            IReadOnlyList<ShapeCoin> spent,
            IReadOnlyList<BitcoinAddress> toAddresses,
            IReadOnlyList<ShapeCoin> paid,
            bool changeBack) =>
            flows.Add(new ShapeFlow(flows.Count, from, to, amount, transaction.Txid, transaction.Block, shared,
                fromAddresses, toAddresses, conclusions)
            {
                Spent = spent,
                Paid = paid,
                ChangeBack = changeBack,
            });
    }

    /// <summary>The actors met while the lines are drawn, each with its sort and addresses.</summary>
    private sealed class ActorTable
    {
        /// <summary>What the labels say about each labelled address, and about each owner of one.</summary>
        private readonly Dictionary<BitcoinAddress, (string Key, ActorSort Sort, string? Name)> _labels = [];

        /// <summary>The input, for the owners.</summary>
        private readonly CheckInput _input;

        /// <summary>The actor of each actor key.</summary>
        private readonly Dictionary<string, int> _byKey = new(StringComparer.Ordinal);

        /// <summary>The actors' keys, sorts, names, addresses and transactions, in the order met.</summary>
        private readonly List<Draft> _actors = [];

        /// <summary>The addresses already given to an actor.</summary>
        private readonly HashSet<BitcoinAddress> _seen = [];

        /// <summary>Reads the labels' mixers and services.</summary>
        /// <param name="input">The input.</param>
        public ActorTable(CheckInput input)
        {
            _input = input;

            // Two mixer labels that name the same service are the same mixer.
            foreach (var mixer in input.Mixers.GroupBy(mixer => mixer.Service ?? mixer.Addresses[0].Value))
            {
                var addresses = mixer.SelectMany(each => each.Addresses).ToList();
                Label(addresses, ActorSort.Mixer, mixer.First().Service);
            }

            foreach (var service in input.Services)
            {
                Label(service.Addresses, service.IsExchange ? ActorSort.Exchange : ActorSort.Service, service.Name);
            }
        }

        /// <summary>Gives the actor of an address, meeting it when new.</summary>
        /// <param name="address">The address.</param>
        /// <returns>The actor's index.</returns>
        public int Of(BitcoinAddress address)
        {
            var owner = _input.OwnerOf(address);
            var (key, sort, name) = _labels.TryGetValue(address, out var label) || _labels.TryGetValue(owner, out label)
                ? label
                : (owner.Value, ActorSort.Unknown, null);
            var index = Meet(key, sort, name, null);
            if (_seen.Add(address))
            {
                _actors[index].Addresses.Add(address);
            }

            return index;
        }

        /// <summary>Gives the actor of a transaction with several payers.</summary>
        /// <param name="txid">The transaction.</param>
        /// <returns>The actor's index.</returns>
        public int OfTransaction(Txid txid) => Meet("tx:" + txid.Value, ActorSort.Transaction, null, txid);

        /// <summary>Gives the actors met, the unlabelled ones with a protected address as protected.</summary>
        /// <param name="protectedAddresses">The start points' and victims' addresses.</param>
        /// <returns>The actors.</returns>
        public List<ShapeActor> Finish(IReadOnlySet<BitcoinAddress> protectedAddresses) =>
            [.. _actors.Select((actor, index) => new ShapeActor(
                index,
                actor.Key,
                actor.Sort == ActorSort.Unknown && actor.Addresses.Any(protectedAddresses.Contains)
                    ? ActorSort.Protected
                    : actor.Sort,
                actor.Addresses,
                actor.Name)
            {
                Transaction = actor.Transaction,
            })];

        /// <summary>Gives the actor of a key, meeting it when new.</summary>
        /// <param name="key">The key.</param>
        /// <param name="sort">Its sort.</param>
        /// <param name="name">Its name.</param>
        /// <param name="transaction">The transaction it is, for a transaction's actor.</param>
        /// <returns>The actor's index.</returns>
        private int Meet(string key, ActorSort sort, string? name, Txid? transaction)
        {
            if (!_byKey.TryGetValue(key, out var index))
            {
                index = _actors.Count;
                _byKey[key] = index;
                _actors.Add(new Draft(key, sort, name, [], transaction));
            }

            return index;
        }

        /// <summary>Gives a label's addresses, and their owners, the label's actor; the first label wins.</summary>
        /// <param name="addresses">The label's addresses.</param>
        /// <param name="sort">The label's sort.</param>
        /// <param name="name">The label's name.</param>
        private void Label(IReadOnlyList<BitcoinAddress> addresses, ActorSort sort, string? name)
        {
            var key = addresses.MinBy(address => address.Value, StringComparer.Ordinal)!.Value;
            foreach (var address in addresses)
            {
                _labels.TryAdd(address, (key, sort, name));
                _labels.TryAdd(_input.OwnerOf(address), (key, sort, name));
            }
        }

        /// <summary>An actor being met.</summary>
        /// <param name="Key">Its key.</param>
        /// <param name="Sort">Its sort.</param>
        /// <param name="Name">Its name.</param>
        /// <param name="Addresses">Its addresses so far.</param>
        /// <param name="Transaction">The transaction it is, for a transaction's actor.</param>
        private sealed record Draft(
            string Key, ActorSort Sort, string? Name, List<BitcoinAddress> Addresses, Txid? Transaction);
    }
}

/// <summary>What sort of actor the case's labels and start points say an actor in the shape is.</summary>
public enum ActorSort
{
    /// <summary>Nothing is known about it.</summary>
    Unknown,

    /// <summary>A start point or a victim: never counted as part of a pattern's business.</summary>
    Protected,

    /// <summary>A label says it is a mixer.</summary>
    Mixer,

    /// <summary>A label says it is an exchange, where money is taken out.</summary>
    Exchange,

    /// <summary>A label says it is another service.</summary>
    Service,

    /// <summary>
    /// A transaction with several payers (for example a CoinJoin), drawn as an actor of its own: whose money went
    /// where in it is not known.
    /// </summary>
    Transaction,
}

/// <summary>
/// A coin a line spends or pays: on Bitcoin an output, <c>txid:index</c>. On an account chain, where there are no
/// coins, it is the transfer that funded the line (its transaction and the transfer's place in it) as far as the
/// chain lets it be read; the shape is read from Bitcoin today.
/// </summary>
/// <param name="Txid">The transaction.</param>
/// <param name="Index">The output's index, or the transfer's place.</param>
public sealed record ShapeCoin(Txid Txid, int Index)
{
    /// <summary>Writes the coin as <c>txid:index</c>.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Txid.Value + ":" + Index.ToString(CultureInfo.InvariantCulture);
}

/// <summary>An actor in the graph's shape: a cluster as the clustering counts it, or a labelled service.</summary>
/// <param name="Index">Its place among the shape's actors.</param>
/// <param name="Key">
/// A stable key: the owner's smallest address, the label's smallest, or <c>tx:</c> and the txid for a transaction's
/// actor.
/// </param>
/// <param name="Sort">What sort the labels say it is.</param>
/// <param name="Addresses">Its addresses in the graph, in the order met; empty for a transaction's actor.</param>
/// <param name="Name">What the label calls it, or <see langword="null"/>.</param>
public sealed record ShapeActor(
    int Index, string Key, ActorSort Sort, IReadOnlyList<BitcoinAddress> Addresses, string? Name)
{
    /// <summary>Gets the transaction it is, for a transaction's actor (<see cref="ActorSort.Transaction"/>).</summary>
    public Txid? Transaction { get; init; }

    /// <summary>Gets whether a label says it is a mixer, an exchange or another service.</summary>
    public bool IsLabelled => Sort is ActorSort.Mixer or ActorSort.Exchange or ActorSort.Service;
}

/// <summary>A line of money in the graph's shape, from one actor to another in one transaction.</summary>
/// <param name="Index">Its place among the shape's lines.</param>
/// <param name="From">The paying actor's index.</param>
/// <param name="To">The paid actor's index.</param>
/// <param name="Amount">
/// What the paid actor received in the transaction (fact); for a line into a transaction's actor, what the payer put in.
/// </param>
/// <param name="Txid">The transaction.</param>
/// <param name="Block">Its block; <see langword="null"/> while unconfirmed.</param>
/// <param name="IsShared">Whether other actors paid into the same transaction too.</param>
/// <param name="FromAddresses">The paying actor's addresses among the inputs.</param>
/// <param name="ToAddresses">The paid actor's addresses among the outputs.</param>
/// <param name="Conclusions">The names of the clustering's heuristics that concluded about the transaction.</param>
public sealed record ShapeFlow(
    int Index,
    int From,
    int To,
    Satoshi Amount,
    Txid Txid,
    BlockReference? Block,
    bool IsShared,
    IReadOnlyList<BitcoinAddress> FromAddresses,
    IReadOnlyList<BitcoinAddress> ToAddresses,
    IReadOnlyList<string> Conclusions)
{
    /// <summary>
    /// Gets the coins the line's payer spent in the transaction (fact): a later line that spends one of this line's
    /// <see cref="Paid"/> coins carries on, coin for coin, money from it.
    /// </summary>
    public IReadOnlyList<ShapeCoin> Spent { get; init; } = [];

    /// <summary>Gets the coins the paid actor got in the transaction (fact).</summary>
    public IReadOnlyList<ShapeCoin> Paid { get; init; } = [];

    /// <summary>Gets whether the payer also got change back in the transaction (fact).</summary>
    public bool ChangeBack { get; init; }

    /// <summary>Gets the block height to order by: unconfirmed lines come last.</summary>
    public long Height => Block?.Height ?? long.MaxValue;
}
