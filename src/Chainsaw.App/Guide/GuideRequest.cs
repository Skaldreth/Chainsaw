using Chainsaw.App.Graph;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Guide;

/// <summary>
/// The answer for what is chosen, shown above the section in the Guide's panel (sketch GD1b): what it rests on, with
/// heuristic, version and tx, in the inspector's own words.
/// </summary>
/// <param name="Label">What the answer is about, for example <c>Varför? · bc1q…x0wl</c>.</param>
/// <param name="Lines">The answers, each with its own section of the Guide.</param>
public sealed record GuideContext(string Label, IReadOnlyList<WhyLine> Lines)
{
    /// <summary>
    /// Gets the steps behind a line's value (sketch KU1a), shown above the section in their own form, or
    /// <see langword="null"/> when the answer is about something else.
    /// </summary>
    public GuideValuation? Valuation { get; init; }
}

/// <summary>
/// A wish to open the Guide: a section, the heading to mark in it, and the answer for what is chosen.
/// </summary>
/// <param name="Topic">The section.</param>
/// <param name="Heading">The heading that answers, as written in the text, or <see langword="null"/>.</param>
/// <param name="Context">The answer for what is chosen, or <see langword="null"/>.</param>
/// <param name="Subject">
/// What the answer is about (a node's or a line's key), so that the panel can tell a new answer for the same thing
/// from one for something else; <see langword="null"/> when it is about nothing chosen.
/// </param>
public sealed record GuideRequest(GuideTopic Topic, string? Heading, GuideContext? Context, string? Subject)
{
    /// <summary>
    /// Gets whether the user chose the thing just now (a click, a key), so that the open panel shows its answer even
    /// after a link was followed; otherwise the request only brings a fuller or newer answer about what was chosen
    /// before (a cluster's answers, the graph read anew), which never moves the reader.
    /// </summary>
    public bool IsChoice { get; init; }

    /// <summary>The order in which a <em>Varför?</em> picks the section to open: the most telling first.</summary>
    private static readonly GuideTopic[] Preference =
    [
        GuideTopic.TrailEnds, GuideTopic.MixerCandidates, GuideTopic.MixerChain, GuideTopic.CoinJoin, GuideTopic.CommonInputOwnership,
        GuideTopic.ChangeAddress, GuideTopic.PeelChain, GuideTopic.StartPoints, GuideTopic.Labels, GuideTopic.Methods,
        GuideTopic.Utxo, GuideTopic.Checks, GuideTopic.Areas, GuideTopic.Graph,
    ];

    /// <summary>
    /// Gives the request for <em>Varför?</em> on what is chosen: every answer at the top, and the section of the most
    /// telling answer (an end before a heuristic, a heuristic before a start point or a label) opened at its heading.
    /// Without any answer that links to the Guide, the graph's section opens at <em>Inspektören och Varför?</em>.
    /// </summary>
    /// <param name="subject">The chosen node's or line's key.</param>
    /// <param name="title">The chosen thing's name, as the inspector shows it.</param>
    /// <param name="lines">The answers; each gets the heading it points to (<see cref="HeadingOf"/>).</param>
    /// <param name="method">The method the graph traces with, for the heading of the methods' section.</param>
    /// <returns>The request.</returns>
    public static GuideRequest ForWhy(
        string subject, string title, IReadOnlyList<WhyLine> lines, TracingMethodKind method)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var answers = lines.Select(line => line with { Heading = HeadingOf(line, method) }).ToList();
        var context = new GuideContext("Varför? · " + title, answers);
        var best = answers
            .Where(line => line.Topic is not null)
            .OrderBy(line => Array.IndexOf(Preference, line.Topic!.Value))
            .FirstOrDefault();
        if (best?.Topic is not { } topic)
        {
            return new GuideRequest(GuideTopic.Graph, GuideTopics.InspectorHeading, context, subject);
        }

        return new GuideRequest(topic, best.Heading, context, subject);
    }

    /// <summary>Gives the request for the method in the graph's corner: the method's heading and its version.</summary>
    /// <param name="method">The method the graph traces with.</param>
    /// <param name="answer">What the graph says about the method, for example its version and what it traced.</param>
    /// <returns>The request.</returns>
    public static GuideRequest ForMethod(TracingMethodKind method, string answer) => new(
        GuideTopic.Methods,
        GuideTopics.MethodHeading(method),
        new GuideContext("Spårningsmetoden i grafen", [new WhyLine(answer, null)]),
        null);

    /// <summary>The heading in the section on rates that shows the steps.</summary>
    public const string ValueHeading = "Så ser stegen ut";

    /// <summary>What <em>Varför?</em> for a value says first: what it rests on, and that it is no fact.</summary>
    public const string ValueAnswer = "Uträknat ur beloppet på kedjan och kurserna. Inte på kedjan.";

    /// <summary>
    /// Gives the request for <em>Varför?</em> on a line's value (sketch KU1a): the steps (the amount, the crypto rate
    /// and the exchange rate, each with its hash, and the calculation) above the section on rates.
    /// </summary>
    /// <param name="subject">The line's key.</param>
    /// <param name="valuation">The steps.</param>
    /// <returns>The request, chosen just now.</returns>
    public static GuideRequest ForValue(string subject, GuideValuation valuation)
    {
        ArgumentNullException.ThrowIfNull(valuation);
        var context = new GuideContext("Varför " + valuation.Total + "?", [new WhyLine(ValueAnswer, null)])
        {
            Valuation = valuation,
        };
        return new GuideRequest(GuideTopic.Prices, ValueHeading, context, subject) { IsChoice = true };
    }

    /// <summary>Gives the request for a section without an answer, for example the visual language.</summary>
    /// <param name="topic">The section.</param>
    /// <returns>The request.</returns>
    public static GuideRequest ForTopic(GuideTopic topic) => new(topic, null, null, null);

    /// <summary>Gives the heading an answer points to: its own, the method's or its section's.</summary>
    /// <param name="line">The answer.</param>
    /// <param name="method">The graph's method.</param>
    /// <returns>The heading, or <see langword="null"/>.</returns>
    public static string? HeadingOf(WhyLine line, TracingMethodKind method)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line.Heading ?? line.Topic switch
        {
            GuideTopic.Methods => GuideTopics.MethodHeading(method),
            { } topic => topic.DefaultHeading(),
            _ => null,
        };
    }
}
