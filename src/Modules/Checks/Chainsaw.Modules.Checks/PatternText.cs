using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>What the pattern rules share: how actors and lines are named, and how a finding is put together.</summary>
internal static class PatternText
{
    /// <summary>How many parts a <em>Varför?</em> names one by one before it says how many more.</summary>
    public const int MostNamed = 5;

    /// <summary>What every pattern's <em>Varför?</em> ends with: that it is a judgement, and why.</summary>
    public const string Judgement = "Det är en bedömning, inte ett faktum: mönstret bygger på klustringens ägare och "
        + "etiketternas sorter, som också är bedömningar, och samma form kan ha en vardaglig förklaring.";

    /// <summary>Names an actor: its label's name, or its key address shortened.</summary>
    /// <param name="actor">The actor.</param>
    /// <returns>For example <c>bc1qxy2k…x0wl</c>.</returns>
    public static string Actor(ShapeActor actor) => actor.Name ?? CheckText.Short(actor.Key);

    /// <summary>Names a sort the way the texts say it, in Swedish.</summary>
    /// <param name="sort">The sort.</param>
    /// <returns>For example <c>mixer</c>.</returns>
    public static string Sort(ActorSort sort) => sort switch
    {
        ActorSort.Mixer => "mixer",
        ActorSort.Exchange => "börs",
        ActorSort.Service => "tjänst",
        ActorSort.Protected => "startpunkt eller offer",
        _ => "okänd",
    };

    /// <summary>Names an actor with its sort when a label gives one.</summary>
    /// <param name="actor">The actor.</param>
    /// <returns>For example <c>börs Kraken</c> or <c>bc1qxy2k…x0wl</c>.</returns>
    public static string ActorWithSort(ShapeActor actor) =>
        actor.IsLabelled ? Sort(actor.Sort) + " " + Actor(actor) : Actor(actor);

    /// <summary>Writes the parts of a <em>Varför?</em> one per sentence, at most <see cref="MostNamed"/>.</summary>
    /// <param name="parts">The parts' sentences.</param>
    /// <returns>The text.</returns>
    public static string Parts(IReadOnlyList<string> parts)
    {
        var named = string.Join(" ", parts.Take(MostNamed));
        return parts.Count > MostNamed
            ? named + $" Och {CheckText.Count(parts.Count - MostNamed)} till av samma form."
            : named;
    }

    /// <summary>Gives the time between two lines, when both are in blocks.</summary>
    /// <param name="from">The earlier line.</param>
    /// <param name="to">The later line.</param>
    /// <returns>For example <c>3 dygn</c>, or <c>okänd tid</c>.</returns>
    public static string Between(ShapeFlow from, ShapeFlow to) => from.Block is { } start && to.Block is { } end
        ? CheckText.Span(end.Time >= start.Time ? end.Time - start.Time : TimeSpan.Zero)
        : "okänd tid";

    /// <summary>Puts a check together from the lines it rests on.</summary>
    /// <param name="key">The finding's key.</param>
    /// <param name="title">The title.</param>
    /// <param name="summary">The line under the title.</param>
    /// <param name="why">Why, ending with <see cref="Judgement"/>.</param>
    /// <param name="flows">The lines it rests on.</param>
    /// <returns>The check, drawn around the lines' addresses.</returns>
    public static CheckFinding Check(
        string key, string title, string summary, string why, IEnumerable<ShapeFlow> flows)
    {
        var lines = flows.ToList();
        var addresses = lines.SelectMany(flow => flow.FromAddresses.Concat(flow.ToAddresses)).Distinct();
        var basis = lines
            .GroupBy(flow => flow.Txid)
            .Select(group => new CheckBasis(group.Key, group.First().Block))
            .OrderBy(each => each.Block?.Height ?? long.MaxValue)
            .ThenBy(each => each.Txid.Value, StringComparer.Ordinal);
        return new CheckFinding(
            key,
            CheckKind.Check,
            AreaSort.Obfuscation,
            title,
            summary,
            why + " " + Judgement,
            [.. addresses],
            [],
            [.. basis],
            []);
    }

    /// <summary>Shortens a line's transaction.</summary>
    /// <param name="flow">The line.</param>
    /// <returns>For example <c>4a5e1e4b…a33b3c44</c>.</returns>
    public static string Tx(ShapeFlow flow) => CheckText.Short(flow.Txid);

    /// <summary>Gives the share one amount is of another.</summary>
    /// <param name="part">The part.</param>
    /// <param name="whole">The whole; above zero.</param>
    /// <returns>The share.</returns>
    public static double Share(Satoshi part, Satoshi whole) => (double)part.Value / whole.Value;
}
