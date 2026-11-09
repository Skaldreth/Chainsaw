using System.Runtime.CompilerServices;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks.Contracts;

/// <summary>
/// The graph's shape as a pattern analysis reads it (<see cref="IGraphShapeAnalysis"/>): the actors, what sort the
/// labels say each is, and the lines of money between them with amounts, blocks and the clustering's conclusions. It
/// is read only from what the case holds (<see cref="CheckInput"/>); the actors are the clustering's owners and the
/// labels' services, so the shape is a judgement built on facts (principle 2), and every line names its transaction
/// and block so that it can be checked against the chain (principle 1).
/// </summary>
/// <remarks>
/// A line is drawn from each owner that paid into a transaction to each other owner it paid, with the amount that
/// owner received; change back to a payer is no line. A transaction with several payers (for example a CoinJoin)
/// gives a line from each, marked <see cref="ShapeFlow.IsShared"/>, since it is not known whose money went where.
/// </remarks>
public sealed class GraphShape
{
    /// <summary>The shapes already read, so that several analyses over the same input read it once.</summary>
    private static readonly ConditionalWeakTable<CheckInput, GraphShape> Read = [];

    /// <summary>The lines out of each actor, in time order.</summary>
    private readonly int[][] _out;

    /// <summary>The lines into each actor, in time order.</summary>
    private readonly int[][] _in;

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
    }

    /// <summary>Gets the actors.</summary>
    public IReadOnlyList<ShapeActor> Actors { get; }

    /// <summary>Gets the lines, in time order (block height, then txid; unconfirmed last).</summary>
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
        var drafts = new List<ShapeFlow>();
        var ordered = input.Transactions
            .Where(transaction => !transaction.IsCoinbase)
            .OrderBy(transaction => transaction.Block?.Height ?? long.MaxValue)
            .ThenBy(transaction => transaction.Txid.Value, StringComparer.Ordinal);
        foreach (var transaction in ordered)
        {
            drafts.AddRange(FlowsOf(transaction, actors, input, drafts.Count));
        }

        return new GraphShape(actors.Finish(input.Protected), drafts);
    }

    /// <summary>Draws the lines of one transaction: from each payer to each other owner paid.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="actors">The actors met so far.</param>
    /// <param name="input">The input, for the clustering's conclusions.</param>
    /// <param name="first">The index of the first line drawn.</param>
    /// <returns>The lines.</returns>
    private static List<ShapeFlow> FlowsOf(
        BitcoinTransaction transaction, ActorTable actors, CheckInput input, int first)
    {
        var payers = transaction.Inputs
            .OrderBy(spent => spent.Index)
            .Select(spent => spent.Address)
            .OfType<BitcoinAddress>()
            .Distinct()
            .GroupBy(actors.Of)
            .ToList();
        var paid = transaction.Outputs
            .Where(output => output.Address is not null)
            .OrderBy(output => output.Index)
            .GroupBy(output => actors.Of(output.Address!))
            .Where(group => payers.All(payer => payer.Key != group.Key))
            .ToList();
        var conclusions = input.Conclusions.TryGetValue(transaction.Txid, out var stored)
            ? stored.Findings.Select(found => found.Heuristic.Name).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToList()
            : [];
        var flows = new List<ShapeFlow>();
        foreach (var payer in payers)
        {
            foreach (var receiver in paid)
            {
                flows.Add(new ShapeFlow(
                    first + flows.Count,
                    payer.Key,
                    receiver.Key,
                    receiver.Aggregate(Satoshi.Zero, (sum, output) => sum + output.Amount),
                    transaction.Txid,
                    transaction.Block,
                    payers.Count > 1,
                    [.. payer],
                    [.. receiver.Select(output => output.Address!).Distinct()],
                    conclusions));
            }
        }

        return flows;
    }

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

    /// <summary>The actors met while the lines are drawn, each with its sort and addresses.</summary>
    private sealed class ActorTable
    {
        /// <summary>What the labels say about each labelled address, and about each owner of one.</summary>
        private readonly Dictionary<BitcoinAddress, (BitcoinAddress Key, ActorSort Sort, string? Name)> _labels = [];

        /// <summary>The input, for the owners.</summary>
        private readonly CheckInput _input;

        /// <summary>The actor of each actor key.</summary>
        private readonly Dictionary<BitcoinAddress, int> _byKey = [];

        /// <summary>The actors' keys, sorts, names and addresses, in the order met.</summary>
        private readonly List<(BitcoinAddress Key, ActorSort Sort, string? Name, List<BitcoinAddress> Addresses)>
            _actors = [];

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
                : (owner, ActorSort.Unknown, null);
            if (!_byKey.TryGetValue(key, out var index))
            {
                index = _actors.Count;
                _byKey[key] = index;
                _actors.Add((key, sort, name, []));
            }

            if (_seen.Add(address))
            {
                _actors[index].Addresses.Add(address);
            }

            return index;
        }

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
                actor.Name))];

        /// <summary>Gives a label's addresses, and their owners, the label's actor; the first label wins.</summary>
        /// <param name="addresses">The label's addresses.</param>
        /// <param name="sort">The label's sort.</param>
        /// <param name="name">The label's name.</param>
        private void Label(IReadOnlyList<BitcoinAddress> addresses, ActorSort sort, string? name)
        {
            var key = addresses.MinBy(address => address.Value, StringComparer.Ordinal)!;
            foreach (var address in addresses)
            {
                _labels.TryAdd(address, (key, sort, name));
                _labels.TryAdd(_input.OwnerOf(address), (key, sort, name));
            }
        }
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
}

/// <summary>An actor in the graph's shape: a cluster as the clustering counts it, or a labelled service.</summary>
/// <param name="Index">Its place among the shape's actors.</param>
/// <param name="Key">A stable key: the owner's smallest address, or the label's smallest.</param>
/// <param name="Sort">What sort the labels say it is.</param>
/// <param name="Addresses">Its addresses in the graph, in the order met.</param>
/// <param name="Name">What the label calls it, or <see langword="null"/>.</param>
public sealed record ShapeActor(
    int Index, BitcoinAddress Key, ActorSort Sort, IReadOnlyList<BitcoinAddress> Addresses, string? Name)
{
    /// <summary>Gets whether a label says it is a mixer, an exchange or another service.</summary>
    public bool IsLabelled => Sort is ActorSort.Mixer or ActorSort.Exchange or ActorSort.Service;
}

/// <summary>A line of money in the graph's shape, from one actor to another in one transaction.</summary>
/// <param name="Index">Its place among the shape's lines.</param>
/// <param name="From">The paying actor's index.</param>
/// <param name="To">The paid actor's index.</param>
/// <param name="Amount">What the paid actor received in the transaction (fact).</param>
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
    /// <summary>Gets the block height to order by: unconfirmed lines come last.</summary>
    public long Height => Block?.Height ?? long.MaxValue;
}
