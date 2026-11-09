using System.Runtime.CompilerServices;
using Chainsaw.Modules.Checks.Contracts;

namespace Chainsaw.Modules.Checks;

/// <summary>Readings of actors and lines that the pattern rules share, so that each is written once.</summary>
internal static class PatternActors
{
    /// <summary>
    /// How many different counterparties make an actor a hub: an exchange, a payment service or a mixer without a
    /// label, whose many customers make everyday money look like a pattern.
    /// </summary>
    public const int HubCounterparties = 10;

    /// <summary>Which actors of each shape are hubs, read once per shape.</summary>
    private static readonly ConditionalWeakTable<GraphShape, bool[]> Hubs = [];

    /// <summary>
    /// Whether an actor is a hub: it has paid or been paid by at least <see cref="HubCounterparties"/> others.
    /// </summary>
    /// <param name="shape">The shape.</param>
    /// <param name="actor">The actor.</param>
    /// <returns>Whether it is.</returns>
    public static bool IsHub(GraphShape shape, ShapeActor actor) =>
        Hubs.GetValue(shape, ReadHubs)[actor.Index];

    /// <summary>Reads which actors of a shape are hubs.</summary>
    /// <param name="shape">The shape.</param>
    /// <returns>Whether each actor is.</returns>
    private static bool[] ReadHubs(GraphShape shape) =>
        [.. shape.Actors.Select(actor => shape.Out(actor.Index).Select(index => shape.Flows[index].To)
            .Concat(shape.In(actor.Index).Select(index => shape.Flows[index].From))
            .Distinct()
            .Take(HubCounterparties)
            .Count() >= HubCounterparties)];

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
