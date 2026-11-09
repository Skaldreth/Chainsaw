using Chainsaw.Modules.Checks.Contracts;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Follows money forward through the graph's shape coin for coin, the way the route and round-trip rules read it: each
/// next line spends a coin the last one paid (<see cref="GraphShape.Spends"/>), has one payer, comes within
/// <see cref="Window"/> of the first line and carries a share of the last that the rule sets (<see cref="Carry"/>).
/// A walk never passes a transaction with several payers (whose money went where is not known), and every walk is
/// bounded by <see cref="StepsPerStart"/> lines tried, so that a graph of thousands of actors is read in time
/// (principle 6).
/// </summary>
internal static class PatternWalk
{
    /// <summary>How many lines a walk from one line may try at most.</summary>
    public const int StepsPerStart = 256;

    /// <summary>The longest time from a walk's first line to its last.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(30);

    /// <summary>
    /// Gives every walk from a line, of two lines or more and at most the rules' most, in a fixed order: the coins in
    /// order and their spenders in time order, depth first.
    /// </summary>
    /// <param name="shape">The shape.</param>
    /// <param name="start">The first line.</param>
    /// <param name="rules">What a walk may do.</param>
    /// <returns>The walks, each the lines' indexes in order.</returns>
    public static List<int[]> From(GraphShape shape, ShapeFlow start, Rules rules)
    {
        var walks = new List<int[]>();
        if (start.IsShared || start.Block is null || rules.Ends(shape.Actors[start.To]))
        {
            return walks;
        }

        var path = new List<int> { start.Index };
        var visited = new HashSet<int> { start.From, start.To };
        var steps = 0;
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

        var first = shape.Flows[path[0]];
        var last = shape.Flows[path[^1]];
        foreach (var index in last.Paid.SelectMany(shape.SpentBy).Distinct())
        {
            if (++steps > StepsPerStart)
            {
                return;
            }

            var next = shape.Flows[index];
            if (next.From != last.To || !Follows(first, last, next, rules.Carry))
            {
                continue;
            }

            if (next.To == first.From)
            {
                if (rules.MayReturn)
                {
                    walks.Add([.. path, index]);
                }

                continue;
            }

            var actor = shape.Actors[next.To];
            if (visited.Contains(next.To) || !rules.MayEnter(actor))
            {
                continue;
            }

            path.Add(index);
            walks.Add([.. path]);
            if (!rules.Ends(actor))
            {
                visited.Add(next.To);
                Extend(shape, path, visited, rules, walks, ref steps);
                visited.Remove(next.To);
            }

            path.RemoveAt(path.Count - 1);
        }
    }

    /// <summary>Whether a line carries on the money of the line before it.</summary>
    /// <param name="first">The walk's first line.</param>
    /// <param name="last">The line before.</param>
    /// <param name="next">A line that spends a coin the line before paid.</param>
    /// <param name="carry">How much of the line before the next must carry.</param>
    /// <returns>Whether it has one payer, is in a block within the window, and carries what it must.</returns>
    private static bool Follows(ShapeFlow first, ShapeFlow last, ShapeFlow next, Carry carry) =>
        !next.IsShared
        && next.Block is { } block
        && block.Time - first.Block!.Time <= Window
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
    /// <param name="MayEnter">Whether a walk may reach an actor (other than the one it started at).</param>
    /// <param name="Ends">Whether a walk that reaches an actor ends there.</param>
    /// <param name="MayReturn">Whether a walk may come back to the actor it started at, and then ends there.</param>
    /// <param name="Carry">How much of a line the next must carry.</param>
    public sealed record Rules(
        int MaxLines, Func<ShapeActor, bool> MayEnter, Func<ShapeActor, bool> Ends, bool MayReturn, Carry Carry);
}
