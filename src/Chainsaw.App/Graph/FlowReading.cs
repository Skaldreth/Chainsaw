using System.Globalization;
using System.Numerics;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>Where a part of the money ended in the view Flöde (sketch FL1a), as its strand is coloured.</summary>
public enum FlowEnd
{
    /// <summary>At a cash-out (blue).</summary>
    CashOut,

    /// <summary>Where money is spent (yellow).</summary>
    Spent,

    /// <summary>At risk or the end of the trail (red).</summary>
    Risk,

    /// <summary>On another chain (purple).</summary>
    OtherChain,

    /// <summary>Still at an actor without a service's sort, where it may yet be frozen or seized (accent stub).</summary>
    Kept,
}

/// <summary>
/// A part of the money that went one way: from one source, along one path of actors, in one amount. Its amount and
/// path are the flow analysis's judgement by the method (principle 2); every step rests on transfers on the chain.
/// </summary>
/// <param name="Source">The actor the money came from: a victim, or another actor whose money mixed in.</param>
/// <param name="IsVictim">Whether it is the victims' money.</param>
/// <param name="Path">The actors it passed, the source first and where it is now last.</param>
/// <param name="Units">How much, in the asset's smallest units.</param>
/// <param name="IsGuess">Whether a step rests on something uncertain (a suggestion, another chain, unconfirmed).</param>
/// <param name="End">Where it ended.</param>
public sealed record FlowLot(
    string Source, bool IsVictim, IReadOnlyList<string> Path, BigInteger Units, bool IsGuess, FlowEnd End)
{
    /// <summary>Gets the actor it is at now.</summary>
    public string Last => Path[^1];

    /// <summary>Gives whether it went from one actor straight to another.</summary>
    /// <param name="from">The first actor.</param>
    /// <param name="to">The second.</param>
    /// <returns><see langword="true"/> when the two stand next to each other in its path.</returns>
    public bool Passes(string from, string to)
    {
        for (var index = 1; index < Path.Count; index++)
        {
            if (Path[index - 1] == from && Path[index] == to)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// What the view Flöde shows: the parts of the money by the method, and the transactions of every step.
/// </summary>
/// <param name="Asset">The asset followed.</param>
/// <param name="Method">The tracing method it was counted with.</param>
/// <param name="Lots">The parts, the victims' first and the largest first.</param>
/// <param name="Steps">Every step between two actors, by <c>from&gt;to</c>, with its line and transactions.</param>
/// <param name="IsSimplified">Whether small parts at an actor with very many were merged into larger ones.</param>
public sealed record FlowResult(
    Asset Asset,
    TracingMethodId Method,
    IReadOnlyList<FlowLot> Lots,
    IReadOnlyDictionary<string, FlowStep> Steps,
    bool IsSimplified)
{
    /// <summary>The analysis's name, as <em>Varför?</em> names it.</summary>
    public const string AnalysisName = "flow";

    /// <summary>The analysis's version.</summary>
    public const int AnalysisVersion = 1;

    /// <summary>Gets the analysis with its version, for example <c>flow v1</c>.</summary>
    public static string Analysis { get; } =
        AnalysisName + " v" + AnalysisVersion.ToString(CultureInfo.InvariantCulture);

    /// <summary>Gets how much of the victims' money the flow follows.</summary>
    public BigInteger VictimUnits => Lots.Where(lot => lot.IsVictim).Aggregate(BigInteger.Zero, (s, l) => s + l.Units);

    /// <summary>Gets whether any of the victims' money moved.</summary>
    public bool HasVictimMoney => VictimUnits.Sign > 0;

    /// <summary>Gives the key of a step.</summary>
    /// <param name="from">The sending actor.</param>
    /// <param name="to">The receiving actor.</param>
    /// <returns>The key, <c>from&gt;to</c>.</returns>
    public static string StepKey(string from, string to) => from + ">" + to;
}

/// <summary>A step between two actors: the graph's line and the transactions it rests on (facts).</summary>
/// <param name="EdgeKey">The graph's line.</param>
/// <param name="Transactions">The transactions, oldest first.</param>
/// <param name="IsGuess">Whether the line is uncertain: a suggestion, another chain or unconfirmed.</param>
public sealed record FlowStep(string EdgeKey, IReadOnlyList<ChainTransactionRef> Transactions, bool IsGuess);

/// <summary>
/// The flow analysis of the view Flöde (sketch FL1a), without WPF: it follows the money through the graph's actors in
/// time order, each actor read as a wallet whose money leaves by the tracing method (FIFO: the oldest first; haircut:
/// a share of everything), from the victims (the start points and the actors labelled victim) and from every other
/// actor whose money mixed in. Each part keeps its path, so that its strand can be coloured by where it ended. It is a
/// judgement with a name and version (principle 2), counted off the UI thread, and writes nothing in the case.
/// </summary>
public static class FlowReading
{
    /// <summary>How many parts an actor keeps apart before the smallest of the same source are merged.</summary>
    public const int MostParts = 120;

    /// <summary>Gives the group an end falls in, as the side panel and the strands colour it.</summary>
    /// <param name="end">The end.</param>
    /// <returns>The colour's meaning.</returns>
    public static GraphInk InkOf(FlowEnd end) => end switch
    {
        FlowEnd.CashOut => GraphInk.CashOut,
        FlowEnd.Spent => GraphInk.Spent,
        FlowEnd.Risk => GraphInk.Risk,
        FlowEnd.OtherChain => GraphInk.OtherChain,
        _ => GraphInk.Accent,
    };

    /// <summary>Gives an end's name.</summary>
    /// <param name="end">The end.</param>
    /// <returns>The name, in Swedish.</returns>
    public static string NameOf(FlowEnd end) => end switch
    {
        FlowEnd.CashOut => "Uttag",
        FlowEnd.Spent => "Spenderas",
        FlowEnd.Risk => "Risk och slut",
        FlowEnd.OtherChain => "Annan kedja",
        _ => "Ligger kvar",
    };

    /// <summary>Gives the assets the graph's lines move, the native coin first.</summary>
    /// <param name="scene">The graph.</param>
    /// <returns>The assets.</returns>
    public static IReadOnlyList<Asset> Assets(GraphScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return
        [
            .. scene.Snapshot.Graph.Edges
                .Where(edge => edge.Kind == GraphEdgeKind.Transfer && edge.From != edge.To)
                .Select(edge => edge.Amount.Asset)
                .Distinct()
                .OrderBy(asset => asset.Kind != AssetKind.Native)
                .ThenBy(asset => asset.Symbol, StringComparer.Ordinal),
        ];
    }

    /// <summary>Follows the money of one asset through the graph with a method.</summary>
    /// <param name="scene">The graph shown.</param>
    /// <param name="asset">The asset.</param>
    /// <param name="method">The tracing method.</param>
    /// <returns>The parts and steps.</returns>
    public static FlowResult Read(GraphScene scene, Asset asset, TracingMethodKind method)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(asset);
        var actors = ExposureMap.Actors(scene);
        var looks = ExposureMap.Looks(scene);
        var victims = actors.Values
            .Where(node => node.IsStartPoint || ExposureMap.GroupOf(node.Look) == ExposureGroup.Victim)
            .Select(node => node.Key)
            .ToHashSet(StringComparer.Ordinal);
        var edges = scene.Snapshot.Graph.Edges
            .Where(edge => edge.Kind is GraphEdgeKind.Transfer or GraphEdgeKind.ContinuesOnOtherChain)
            .Where(edge => edge.Amount.Asset == asset && edge.From != edge.To)
            .Where(edge => actors.ContainsKey(edge.From) && actors.ContainsKey(edge.To))
            .ToList();
        var steps = Steps(scene, edges);
        var run = new Run(method, victims, steps);
        foreach (var (edge, transfer) in InTimeOrder(edges))
        {
            run.Move(edge.From, edge.To, transfer.Amount.Units);
        }

        var lots = run.Lots(looks);
        return new FlowResult(asset, new TracingMethodId(method, 1), lots, steps, run.IsSimplified);
    }

    /// <summary>Gives where a part ends, from the sort of the actor it is at.</summary>
    /// <param name="look">How the actor is drawn, or <see langword="null"/>.</param>
    /// <param name="isVictim">Whether the actor is a victim, where money that comes back is kept.</param>
    /// <returns>The end.</returns>
    public static FlowEnd EndOf(SortLook? look, bool isVictim)
    {
        if (look is null || isVictim)
        {
            return FlowEnd.Kept;
        }

        return ExposureMap.GroupOf(look) switch
        {
            ExposureGroup.CashOut => FlowEnd.CashOut,
            ExposureGroup.Spent => FlowEnd.Spent,
            ExposureGroup.Risk => FlowEnd.Risk,
            ExposureGroup.OtherChain => FlowEnd.OtherChain,
            _ => FlowEnd.Kept,
        };
    }

    /// <summary>Gives every step's line and transactions, and whether it is uncertain.</summary>
    /// <param name="scene">The graph.</param>
    /// <param name="edges">The lines followed.</param>
    /// <returns>The steps by key.</returns>
    private static Dictionary<string, FlowStep> Steps(GraphScene scene, IReadOnlyList<GraphEdge> edges)
    {
        var steps = new Dictionary<string, FlowStep>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            var guess = edge.Kind == GraphEdgeKind.ContinuesOnOtherChain
                || scene.Edge(edge.Key) is { Dash: not EdgeDash.None };
            var key = FlowResult.StepKey(edge.From, edge.To);
            var transactions = edge.Transfers.Select(transfer => transfer.Transaction).Distinct().ToList();
            steps[key] = steps.TryGetValue(key, out var known)
                ? known with
                {
                    Transactions = [.. known.Transactions.Concat(transactions).Distinct()],
                    IsGuess = known.IsGuess || guess,
                }
                : new FlowStep(edge.Key, transactions, guess);
        }

        return steps;
    }

    /// <summary>
    /// Gives the transfers in time order: confirmed by the block's time and height, unconfirmed last, and in one
    /// transaction by the line, so that the order is the same every time.
    /// </summary>
    /// <param name="edges">The lines.</param>
    /// <returns>The transfers with their lines.</returns>
    private static IEnumerable<(GraphEdge Edge, AssetTransfer Transfer)> InTimeOrder(IEnumerable<GraphEdge> edges) =>
        edges.SelectMany(edge => edge.Transfers.Select(transfer => (Edge: edge, Transfer: transfer)))
            .OrderBy(pair => pair.Transfer.Block is null)
            .ThenBy(pair => pair.Transfer.Block?.Time ?? DateTimeOffset.MaxValue)
            .ThenBy(pair => pair.Transfer.Block?.Height ?? long.MaxValue)
            .ThenBy(pair => pair.Transfer.Transaction.Id, StringComparer.Ordinal)
            .ThenBy(pair => pair.Edge.Key, StringComparer.Ordinal);

    /// <summary>A part while the money is followed: its amount changes as it is taken.</summary>
    private sealed class Part
    {
        /// <summary>Gets or sets the source.</summary>
        public required string Source { get; init; }

        /// <summary>Gets whether it is the victims' money.</summary>
        public required bool IsVictim { get; init; }

        /// <summary>Gets the path.</summary>
        public required IReadOnlyList<string> Path { get; init; }

        /// <summary>Gets whether a step rests on something uncertain.</summary>
        public required bool IsGuess { get; init; }

        /// <summary>Gets or sets how much is left.</summary>
        public BigInteger Units { get; set; }

        /// <summary>Gets whether it is the same way as another, so that the two can be one.</summary>
        /// <param name="other">The other.</param>
        /// <returns><see langword="true"/> when the source, the path and the certainty are the same.</returns>
        public bool SameWay(Part other) => Source == other.Source && IsVictim == other.IsVictim
            && IsGuess == other.IsGuess && Path.SequenceEqual(other.Path, StringComparer.Ordinal);
    }

    /// <summary>The money at every actor while the transfers are followed in time order.</summary>
    /// <param name="method">How money leaves an actor.</param>
    /// <param name="victims">The victims' actors.</param>
    /// <param name="steps">The steps, which say which are uncertain.</param>
    private sealed class Run(
        TracingMethodKind method, IReadOnlySet<string> victims, IReadOnlyDictionary<string, FlowStep> steps)
    {
        /// <summary>The parts at each actor, in the order they came.</summary>
        private readonly Dictionary<string, List<Part>> _pools = new(StringComparer.Ordinal);

        /// <summary>Gets whether small parts were merged at an actor with very many.</summary>
        public bool IsSimplified { get; private set; }

        /// <summary>Moves an amount from one actor to another by the method.</summary>
        /// <param name="from">The sender.</param>
        /// <param name="to">The receiver.</param>
        /// <param name="units">How much.</param>
        public void Move(string from, string to, BigInteger units)
        {
            if (units.Sign <= 0)
            {
                return;
            }

            var pool = Pool(from);
            var held = pool.Aggregate(BigInteger.Zero, (sum, part) => sum + part.Units);
            if (held < units)
            {
                // Money the graph does not show coming in: the actor's own, from outside what is expanded.
                pool.Add(new Part
                {
                    Source = from, IsVictim = victims.Contains(from), Path = [from], IsGuess = false,
                    Units = units - held,
                });
                held = units;
            }

            var guess = steps.TryGetValue(FlowResult.StepKey(from, to), out var step) && step.IsGuess;
            var taken = method == TracingMethodKind.Haircut ? Share(pool, units, held) : Oldest(pool, units);
            pool.RemoveAll(part => part.Units.IsZero);
            var target = Pool(to);
            foreach (var (part, amount) in taken)
            {
                Add(target, Onwards(part, to, amount, guess));
            }

            Simplify(target);
        }

        /// <summary>Gives every part that moved, with where it ended; the victims' first, the largest first.</summary>
        /// <param name="looks">How each actor is drawn.</param>
        /// <returns>The parts.</returns>
        public List<FlowLot> Lots(IReadOnlyDictionary<string, SortLook> looks)
        {
            var moved = _pools.Values.SelectMany(pool => pool)
                .Where(part => part.Path.Count > 1 && !part.Units.IsZero)
                .ToList();
            var victimWay = moved.Where(part => part.IsVictim).SelectMany(part => part.Path)
                .ToHashSet(StringComparer.Ordinal);
            return
            [
                .. moved
                    .Where(part => part.IsVictim || part.Path.Skip(1).Any(victimWay.Contains))
                    .Select(part => new FlowLot(
                        part.Source, part.IsVictim, part.Path, part.Units, part.IsGuess,
                        EndOf(looks.GetValueOrDefault(part.Path[^1]), victims.Contains(part.Path[^1]))))
                    .OrderByDescending(lot => lot.IsVictim)
                    .ThenByDescending(lot => lot.Units),
            ];
        }

        /// <summary>Takes the oldest parts first (FIFO).</summary>
        /// <param name="pool">The parts at the sender.</param>
        /// <param name="units">How much leaves.</param>
        /// <returns>What was taken of each part.</returns>
        private static List<(Part Part, BigInteger Units)> Oldest(List<Part> pool, BigInteger units)
        {
            var taken = new List<(Part, BigInteger)>();
            foreach (var part in pool)
            {
                if (units.IsZero)
                {
                    break;
                }

                var take = BigInteger.Min(units, part.Units);
                part.Units -= take;
                units -= take;
                taken.Add((part, take));
            }

            return taken;
        }

        /// <summary>
        /// Takes a share of every part (haircut), rounded down, with what the rounding left taken from the largest.
        /// </summary>
        /// <param name="pool">The parts at the sender.</param>
        /// <param name="units">How much leaves.</param>
        /// <param name="held">How much the sender holds.</param>
        /// <returns>What was taken of each part.</returns>
        private static List<(Part Part, BigInteger Units)> Share(List<Part> pool, BigInteger units, BigInteger held)
        {
            var taken = pool.Select(part => (Part: part, Units: units * part.Units / held)).ToList();
            var left = units - taken.Aggregate(BigInteger.Zero, (sum, each) => sum + each.Units);
            if (left.Sign > 0)
            {
                var room = taken.Select((each, index) => (Left: each.Part.Units - each.Units, Index: index));
                var largest = room.MaxBy(pair => pair.Left).Index;
                taken[largest] = (taken[largest].Part, taken[largest].Units + left);
            }

            foreach (var (part, take) in taken)
            {
                part.Units -= take;
            }

            return [.. taken.Where(each => each.Units.Sign > 0)];
        }

        /// <summary>
        /// Gives a part one step on: the path grows by the receiver, or is cut back where it returns to an actor it
        /// passed; money that is not the victims' and comes to a victim becomes that victim's, as the trace starts there.
        /// </summary>
        /// <param name="part">The part taken.</param>
        /// <param name="to">The receiver.</param>
        /// <param name="units">How much of it.</param>
        /// <param name="guess">Whether the step is uncertain.</param>
        /// <returns>The part at the receiver.</returns>
        private Part Onwards(Part part, string to, BigInteger units, bool guess)
        {
            if (!part.IsVictim && victims.Contains(to))
            {
                return new Part { Source = to, IsVictim = true, Path = [to], IsGuess = false, Units = units };
            }

            var back = IndexOf(part.Path, to);
            IReadOnlyList<string> path = back >= 0 ? [.. part.Path.Take(back + 1)] : [.. part.Path, to];
            return new Part
            {
                Source = part.Source, IsVictim = part.IsVictim, Path = path, IsGuess = part.IsGuess || guess,
                Units = units,
            };
        }

        /// <summary>Adds a part to an actor, joining it to one that went the same way.</summary>
        /// <param name="pool">The actor's parts.</param>
        /// <param name="part">The part.</param>
        private static void Add(List<Part> pool, Part part)
        {
            if (pool.Find(part.SameWay) is { } same)
            {
                same.Units += part.Units;
            }
            else
            {
                pool.Add(part);
            }
        }

        /// <summary>
        /// Keeps an actor's parts few enough: past <see cref="MostParts"/>, the smallest is joined to the largest part
        /// of the same source, and the flow says it is simplified.
        /// </summary>
        /// <param name="pool">The actor's parts.</param>
        private void Simplify(List<Part> pool)
        {
            while (pool.Count > MostParts)
            {
                var smallest = pool.MinBy(part => part.Units)!;
                var into = pool.Where(part => part != smallest && part.Source == smallest.Source
                    && part.IsVictim == smallest.IsVictim).MaxBy(part => part.Units);
                if (into is null)
                {
                    return;
                }

                into.Units += smallest.Units;
                pool.Remove(smallest);
                IsSimplified = true;
            }
        }

        /// <summary>Gives an actor's parts.</summary>
        /// <param name="key">The actor.</param>
        /// <returns>Its parts.</returns>
        private List<Part> Pool(string key)
        {
            if (!_pools.TryGetValue(key, out var pool))
            {
                pool = [];
                _pools[key] = pool;
            }

            return pool;
        }

        /// <summary>Gives where an actor stands in a path.</summary>
        /// <param name="path">The path.</param>
        /// <param name="key">The actor.</param>
        /// <returns>Its index, or -1.</returns>
        private static int IndexOf(IReadOnlyList<string> path, string key)
        {
            for (var index = 0; index < path.Count; index++)
            {
                if (path[index] == key)
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
