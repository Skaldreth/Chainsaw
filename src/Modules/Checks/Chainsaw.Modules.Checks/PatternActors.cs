using System.Runtime.CompilerServices;
using Chainsaw.Modules.Checks.Contracts;

namespace Chainsaw.Modules.Checks;

/// <summary>Readings of actors and lines that the pattern rules share, so that each is written once.</summary>
internal static class PatternActors
{
    /// <summary>
    /// How many different counterparties make an actor a hub: an exchange, a payment service or a mixer without a
    /// label, whose many customers make everyday money look like a pattern. Only counterparties that say so count
    /// (<see cref="Counts"/>), so that a large fraud, with many victims and many collecting addresses, is no hub.
    /// </summary>
    public const int HubCounterparties = 10;

    /// <summary>Which actors of each shape are hubs, read once per shape.</summary>
    private static readonly ConditionalWeakTable<GraphShape, bool[]> Hubs = [];

    /// <summary>
    /// Whether an actor is a hub: at least <see cref="HubCounterparties"/> counterparties that count
    /// (<see cref="Counts"/>) have paid it or been paid by it.
    /// </summary>
    /// <param name="shape">The shape.</param>
    /// <param name="actor">The actor.</param>
    /// <returns>Whether it is.</returns>
    public static bool IsHub(GraphShape shape, ShapeActor actor) =>
        Hubs.GetValue(shape, ReadHubs)[actor.Index];

    /// <summary>Reads which actors of a shape are hubs.</summary>
    /// <param name="shape">The shape.</param>
    /// <returns>Whether each actor is.</returns>
    private static bool[] ReadHubs(GraphShape shape)
    {
        var paid = shape.Actors
            .Select(actor => shape.Out(actor.Index).Select(index => shape.Flows[index].To).ToHashSet())
            .ToArray();
        var passesFor = shape.Actors.Select(actor => PassesOnFor(shape, actor.Index)).ToArray();
        return [.. shape.Actors.Select(actor => paid[actor.Index]
            .Concat(shape.In(actor.Index).Select(index => shape.Flows[index].From))
            .Distinct()
            .Where(other => passesFor[other] != actor.Index && Counts(shape, paid, actor.Index, other))
            .Take(HubCounterparties)
            .Count() >= HubCounterparties)];
    }

    /// <summary>
    /// Gives the one actor whose coins an actor only passes on: it got money from that actor alone, has paid on, and
    /// every coin it spent was paid to it by that actor (a mule). Such a recipient does not count towards making the
    /// payer a hub, as a payer that only collects and empties does not count towards making its recipient one, so
    /// that a scammer who pays out to many mules is no hub.
    /// </summary>
    /// <param name="shape">The shape.</param>
    /// <param name="actor">The actor's index.</param>
    /// <returns>The actor whose coins it passes on, or <c>-1</c>.</returns>
    private static int PassesOnFor(GraphShape shape, int actor)
    {
        var sources = shape.In(actor).Select(index => shape.Flows[index].From).Distinct().Take(2).ToList();
        var outs = shape.Out(actor).Select(index => shape.Flows[index]).ToList();
        if (sources.Count != 1 || outs.Count == 0 || outs.Any(flow => flow.IsShared || flow.Spent.Count == 0))
        {
            return -1;
        }

        var source = sources[0];
        var passes = outs.All(flow => flow.Spent.All(coin =>
            shape.PayerOf(coin) is { } line && shape.Flows[line].From == source));
        return passes ? source : -1;
    }

    /// <summary>
    /// Whether a counterparty counts towards making an actor a hub. A start point or victim never does, and neither
    /// does one that only collects and empties into the actor (a victim who paid it, or a collecting address that
    /// swept to it): it pays the actor, pays no one else, and the actor never pays it. Everything else counts: a
    /// labelled actor, and every actor the actor pays (an exchange pays out to many customers, a scam's collector
    /// sweeps to few), except one that only passes on the actor's coins (<see cref="PassesOnFor"/>).
    /// </summary>
    /// <param name="shape">The shape.</param>
    /// <param name="paid">The actors each actor pays.</param>
    /// <param name="actor">The actor's index.</param>
    /// <param name="other">The counterparty's index.</param>
    /// <returns>Whether it counts.</returns>
    private static bool Counts(GraphShape shape, HashSet<int>[] paid, int actor, int other)
    {
        var counterparty = shape.Actors[other];
        if (counterparty.Sort == ActorSort.Protected)
        {
            return false;
        }

        return counterparty.IsLabelled
            || paid[actor].Contains(other)
            || paid[other].Count > 1
            || (paid[other].Count == 1 && !paid[other].Contains(actor));
    }

    /// <summary>Whether an actor does more than pass money on: it has lines beyond the given ones, in or out.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="actor">The actor's index.</param>
    /// <param name="lines">The lines it is known by.</param>
    /// <returns>Whether it has others.</returns>
    public static bool HasOtherBusiness(GraphShape shape, int actor, IReadOnlySet<int> lines) =>
        shape.Out(actor).Concat(shape.In(actor)).Any(index => !lines.Contains(index));

    /// <summary>
    /// Whether a line only forwards money its payer got in the shape: the payer is not a start point or victim, got
    /// no change back, and every coin it spent was paid to it by a line in the shape (as a deposit address sweeps a
    /// customer's payment on to its hot wallet).
    /// </summary>
    /// <param name="shape">The shape.</param>
    /// <param name="flow">The line.</param>
    /// <returns>Whether it only forwards.</returns>
    public static bool Forwards(GraphShape shape, ShapeFlow flow) =>
        shape.Actors[flow.From].Sort != ActorSort.Protected
        && !flow.ChangeBack
        && flow.Spent.Count > 0
        && flow.Spent.All(coin => shape.PayerOf(coin) is not null);
}
