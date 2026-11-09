using Chainsaw.Modules.Checks.Contracts;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Follows money forward through the graph's shape, the way the route and round-trip rules read it: each next line
/// leaves the actor the last one reached, no earlier than it, in another transaction, and carries a share of it that
/// the rule sets (<see cref="Carry"/>), so that it can be the same money. Every walk is bounded by
/// <see cref="StepsPerStart"/>, so that a graph of thousands of actors is read in time (principle 6).
/// </summary>
internal static class PatternWalk
{
    /// <summary>How many lines a walk from one line may try at most.</summary>
    public const int StepsPerStart = 256;

    /// <summary>
    /// Gives every walk from a line, of two lines or more and at most <paramref name="maxLines"/>, in a fixed order:
    /// each actor's lines in time order, depth first.
    /// </summary>
    /// <param name="shape">The shape.</param>
    /// <param name="start">The first line.</param>
    /// <param name="maxLines">The most lines in a walk.</param>
    /// <param name="mayEnter">Whether a walk may reach an actor (other than the one it started at).</param>
    /// <param name="mayReturn">Whether a walk may come back to the actor it started at, and then ends there.</param>
    /// <param name="carry">How much of a line the next must carry.</param>
    /// <returns>The walks, each the lines' indexes in order.</returns>
    public static List<int[]> From(
        GraphShape shape,
        ShapeFlow start,
        int maxLines,
        Func<ShapeActor, bool> mayEnter,
        bool mayReturn,
        Carry carry)
    {
        var walks = new List<int[]>();
        var path = new List<int> { start.Index };
        var visited = new HashSet<int> { start.From, start.To };
        var steps = 0;
        var rules = new Rules(maxLines, mayEnter, mayReturn, carry);
        Extend(shape, path, visited, rules, walks, ref steps);
        return walks;
    }

    /// <summary>Extends a walk by every line that may follow its last, depth first.</summary>
    /// <param name="shape">The shape.</param>
    /// <param name="path">The walk so far.</param>
    /// <param name="visited">The actors the walk has reached.</param>
    /// <param name="rules">What a walk may do.</param>
    /// <param name="walks">The walks found.</param>
    /// <param name="steps">The lines tried so far.</param>
    private static void Extend(
        GraphShape shape, List<int> path, HashSet<int> visited, Rules rules, List<int[]> walks, ref int steps)
    {
        if (path.Count >= rules.MaxLines)
        {
            return;
        }

        var last = shape.Flows[path[^1]];
        var origin = shape.Flows[path[0]].From;
        foreach (var index in shape.Out(last.To))
        {
            if (++steps > StepsPerStart)
            {
                return;
            }

            var next = shape.Flows[index];
            if (!Follows(last, next, rules.Carry))
            {
                continue;
            }

            if (next.To == origin)
            {
                if (rules.MayReturn)
                {
                    walks.Add([.. path, index]);
                }

                continue;
            }

            if (visited.Contains(next.To) || !rules.MayEnter(shape.Actors[next.To]))
            {
                continue;
            }

            path.Add(index);
            visited.Add(next.To);
            walks.Add([.. path]);
            Extend(shape, path, visited, rules, walks, ref steps);
            visited.Remove(next.To);
            path.RemoveAt(path.Count - 1);
        }
    }

    /// <summary>Whether a line can carry on the money of the line before it.</summary>
    /// <param name="last">The line before.</param>
    /// <param name="next">The next line, out of the actor the line before reached.</param>
    /// <param name="carry">How much of the line before the next must carry.</param>
    /// <returns>Whether it comes no earlier, in another transaction, carrying what it must.</returns>
    private static bool Follows(ShapeFlow last, ShapeFlow next, Carry carry) =>
        !next.IsShared
        && next.Txid != last.Txid
        && next.Height >= last.Height
        && next.Amount.Value >= carry.Least * last.Amount.Value
        && (carry.MayGrow || next.Amount.Value <= last.Amount.Value);

    /// <summary>How much of a line the next must carry to be the same money.</summary>
    /// <param name="Least">The smallest share of the line before.</param>
    /// <param name="MayGrow">
    /// Whether the next may carry more than the line before (when money is gathered on the way).
    /// </param>
    public sealed record Carry(double Least, bool MayGrow);

    /// <summary>What a walk may do.</summary>
    /// <param name="MaxLines">The most lines in a walk.</param>
    /// <param name="MayEnter">Whether a walk may reach an actor.</param>
    /// <param name="MayReturn">Whether a walk may come back to the actor it started at.</param>
    /// <param name="Carry">How much of a line the next must carry.</param>
    private sealed record Rules(int MaxLines, Func<ShapeActor, bool> MayEnter, bool MayReturn, Carry Carry);
}
