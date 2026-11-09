using System.Globalization;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Requests.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>An actor marked in the graph, as the report's chips draw it (sketch RP1b): the form and the colour.</summary>
/// <param name="Key">The actor's node.</param>
/// <param name="Name">Its name as the report may write it.</param>
/// <param name="Ink">Its sort's colour.</param>
/// <param name="IsRound">Whether it is round (an actor we know nothing about) rather than square.</param>
/// <param name="IsDashed">Whether its frame is dashed, as the sort's is in the graph.</param>
public sealed record ReportActor(string Key, string Name, GraphInk Ink, bool IsRound, bool IsDashed);

/// <summary>
/// What a report is made of, found in the graph from what is marked (sketch RP1b): the trails between the marked
/// actors, the conclusions about them with why, the labels and their sources.
/// </summary>
/// <param name="Actors">The actors marked, in the graph's order.</param>
/// <param name="Trails">The trails between them.</param>
/// <param name="Conclusions">The conclusions, each with why.</param>
/// <param name="Labels">The labels and their sources.</param>
/// <param name="Method">The tracing method the traced amounts were counted by.</param>
public sealed record ReportMaterial(
    IReadOnlyList<ReportActor> Actors,
    IReadOnlyList<ReportTrail> Trails,
    IReadOnlyList<ReportConclusion> Conclusions,
    IReadOnlyList<ReportLabel> Labels,
    string Method)
{
    /// <summary>Gets nothing: no actor marked.</summary>
    public static ReportMaterial Empty { get; } = new([], [], [], [], string.Empty);

    /// <summary>Gets how many transactions the trails rest on.</summary>
    public int TransactionCount =>
        Trails.SelectMany(trail => trail.Steps).Select(step => step.Transaction).Distinct(StringComparer.Ordinal).Count();

    /// <summary>Gets how many trails end where the money leaves the chain or the trail ends.</summary>
    public int EndCount => Trails.Count(trail => trail.End != ReportTrailEnd.Open);
}

/// <summary>
/// Finds a report's material in the graph shown, from the actors marked (sketch RP1b), without WPF. The trails run
/// along traced money between the marked actors; a transaction drawn as a node of its own (a CoinJoin) is passed
/// through and is never a step or a hop. What the report may say is decided here (principle 4): an actor is named only
/// by a service's label of its own sort, otherwise by its address shortened, and a victim's label, a label's reason or
/// note and the case's title never come along, so no name of a person can reach the file.
/// </summary>
public static class ReportSelection
{
    /// <summary>The most transactions a cluster's <em>Varför?</em> names before it counts the rest.</summary>
    private const int MostTransactionsNamed = 2;

    /// <summary>Gives no rate, when the case's rates are not at hand.</summary>
    private static readonly Func<Asset, DateTimeOffset, Price?> NoRate = (_, _) => null;

    /// <summary>The groups of labels that name a service, whose name a report may write.</summary>
    private static readonly HashSet<LabelGroup> ServiceGroups =
        [LabelGroup.Withdrawal, LabelGroup.Spending, LabelGroup.Risk, LabelGroup.OtherChain];

    /// <summary>Finds the material.</summary>
    /// <param name="scene">The graph shown.</param>
    /// <param name="picked">The actors marked, by key.</param>
    /// <param name="clusters">The clusters of the marked cluster nodes, by node key, read from the case.</param>
    /// <param name="rateAt">
    /// The case's rate of an asset at a time, which values the traced amounts; <see langword="null"/> for none.
    /// </param>
    /// <returns>The material; empty when nothing marked is drawn.</returns>
    public static ReportMaterial Build(
        GraphScene scene,
        IReadOnlyCollection<string> picked,
        IReadOnlyDictionary<string, Cluster> clusters,
        Func<Asset, DateTimeOffset, Price?>? rateAt = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(picked);
        ArgumentNullException.ThrowIfNull(clusters);
        var marked = picked.ToHashSet(StringComparer.Ordinal);
        var actors = scene.Nodes.Where(node => marked.Contains(node.Key) && IsActor(node)).ToList();
        if (actors.Count == 0)
        {
            return ReportMaterial.Empty;
        }

        var trails = new ReportTrails(scene, actors, rateAt ?? NoRate).Find();
        var conclusions = new List<ReportConclusion>();
        conclusions.AddRange(actors.Select(node => ClusterConclusion(node, clusters)).OfType<ReportConclusion>());
        conclusions.AddRange(actors.SelectMany(LabelConclusions));
        if (trails.Count > 0)
        {
            conclusions.Add(MethodConclusion(scene.MethodText));
            conclusions.AddRange(SwapConclusions(scene, trails));
        }

        return new ReportMaterial(
            [.. actors.Select(Actor)],
            trails,
            conclusions,
            [.. actors.SelectMany(Labels)],
            scene.MethodText);
    }

    /// <summary>Gets the clusters the marked actors stand for, which the case is asked for off the UI thread.</summary>
    /// <param name="scene">The graph shown.</param>
    /// <param name="picked">The actors marked.</param>
    /// <returns>Each marked cluster node's key and its first address.</returns>
    public static IReadOnlyList<(string Key, BitcoinAddress Address)> ClustersWanted(
        GraphScene scene, IReadOnlyCollection<string> picked)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(picked);
        return [.. picked.Select(scene.Node).OfType<NodeView>()
            .Where(node => node.Node.Kind == GraphNodeKind.Cluster && node.Node.GraphAddresses.Count > 0)
            .Select(node => (node.Key, node.Node.GraphAddresses[0]))];
    }

    /// <summary>
    /// Names an actor for a report: the name of a service's label of its own sort, otherwise its address shortened,
    /// with <em>Kluster</em> before a cluster's. A victim's label or the user's own name for an unknown actor is never
    /// written, since it may name a person.
    /// </summary>
    /// <param name="node">The actor.</param>
    /// <returns>The name.</returns>
    public static string SafeName(NodeView node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (ServiceLabel(node) is { Name: { } name })
        {
            return name;
        }

        var shortened = GraphScene.Shorten(node.Node.Addresses.FirstOrDefault()?.Value ?? node.Subject);
        return node.Node.Kind == GraphNodeKind.Cluster ? "Kluster " + shortened : shortened;
    }

    /// <summary>Says whether a node is an actor a report may mark, not a transaction, a pile or a clue.</summary>
    /// <param name="node">The node.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    internal static bool IsActor(NodeView node) =>
        node.Node.Kind is GraphNodeKind.Address or GraphNodeKind.Cluster or GraphNodeKind.Coin
            or GraphNodeKind.OtherChain;

    /// <summary>Gives a service's label of the actor's own sort with a name, the only one whose name is written.
    /// </summary>
    /// <param name="node">The actor.</param>
    /// <returns>The label, or <see langword="null"/>.</returns>
    private static Label? ServiceLabel(NodeView node) =>
        node.Labels.FirstOrDefault(label => IsService(label)
            && VisualLanguage.SortOf(label.Kind) == node.Look.Sort
            && NameOf(label) is not null);

    /// <summary>Says whether a label says the actor is a service, not a victim and not unknown.</summary>
    /// <param name="label">The label.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool IsService(Label label) => ServiceGroups.Contains(label.Kind.Group());

    /// <summary>Gives an actor's chip.</summary>
    /// <param name="node">The actor.</param>
    /// <returns>The chip.</returns>
    private static ReportActor Actor(NodeView node) =>
        new(node.Key, SafeName(node), node.Look.Ink, node.Look.Shape == NodeShape.Round, node.Look.IsDashed);

    /// <summary>Says what a cluster rests on: the heuristics with their versions and the transactions.</summary>
    /// <param name="node">The actor.</param>
    /// <param name="clusters">The clusters read from the case.</param>
    /// <returns>The conclusion, or <see langword="null"/> for an actor that is no cluster of more than one.</returns>
    private static ReportConclusion? ClusterConclusion(NodeView node, IReadOnlyDictionary<string, Cluster> clusters)
    {
        if (!clusters.TryGetValue(node.Key, out var cluster) || cluster.Members.Count < 2)
        {
            return null;
        }

        var links = cluster.Links.Where(link => link.State == LinkState.Merged)
            .DistinctBy(link => link.Evidence.Sequence)
            .ToList();
        if (links.Count == 0)
        {
            return null;
        }

        var heuristics = string.Join(", ", links.Select(link => link.Evidence.Heuristic.ToString()).Distinct());
        var transactions = links.Select(link => link.Evidence.Txid.Value).Distinct(StringComparer.Ordinal).ToList();
        var named = string.Join(", ", transactions.Take(MostTransactionsNamed).Select(GraphScene.Shorten));
        var why = $"{heuristics} · tx {named}";
        if (transactions.Count > MostTransactionsNamed)
        {
            why += string.Create(CultureInfo.InvariantCulture, $" och {transactions.Count - MostTransactionsNamed} till");
        }

        if (cluster.Splits.Count > 0)
        {
            why += string.Create(CultureInfo.InvariantCulture, $" · {cluster.Splits.Count} uppbrutna av användaren");
        }

        var statement = string.Create(
            CultureInfo.InvariantCulture,
            $"{SafeName(node)}: {cluster.Members.Count} adresser har enligt klustringen samma ägare.");
        return new ReportConclusion(statement, why);
    }

    /// <summary>Gives what each label on an actor says, with its sources; never a victim's or an unknown's.</summary>
    /// <param name="node">The actor.</param>
    /// <returns>The conclusions.</returns>
    private static IEnumerable<ReportConclusion> LabelConclusions(NodeView node) =>
        node.Labels.Where(IsService).Select(label => new ReportConclusion(
            $"{Subject(node)} är {label.Kind.Describe().ToLowerInvariant()}{NameIn(label)}.",
            Sources(label)));

    /// <summary>Gives the labels on an actor as the report lists them, with their sources.</summary>
    /// <param name="node">The actor.</param>
    /// <returns>The labels.</returns>
    private static IEnumerable<ReportLabel> Labels(NodeView node) =>
        node.Labels.Where(IsService).Select(label => new ReportLabel(
            $"{label.Kind.Describe()} · {NameOf(label) ?? Subject(node)}", Sources(label)));

    /// <summary>Writes the actor a label is on: its address shortened, never a name.</summary>
    /// <param name="node">The actor.</param>
    /// <returns>For example <c>bc1q…x0wl</c>.</returns>
    private static string Subject(NodeView node) =>
        GraphScene.Shorten(node.Node.Addresses.FirstOrDefault()?.Value ?? node.Subject);

    /// <summary>Writes a service's name in brackets after what the label says, or nothing.</summary>
    /// <param name="label">The label.</param>
    /// <returns>For example <c> (Börs A)</c>.</returns>
    private static string NameIn(Label label) => NameOf(label) is { } name ? $" ({name})" : string.Empty;

    /// <summary>
    /// Gives a service's name from a label, never a report's: what someone reported to a supplier may name a person.
    /// </summary>
    /// <param name="label">The label.</param>
    /// <returns>The name, or <see langword="null"/>.</returns>
    private static string? NameOf(Label label) =>
        label.Kind == LabelKind.Reported || string.IsNullOrWhiteSpace(label.Name) ? null : label.Name;

    /// <summary>
    /// Says where a label comes from: each origin's source, version and day, and whether it came from another case.
    /// Never who set an own label's reason or note, which stay in the case (principle 4).
    /// </summary>
    /// <param name="label">The label.</param>
    /// <returns>The sources.</returns>
    private static string Sources(Label label)
    {
        var sources = string.Join("; ", label.Origins.Select(Source));
        return label.IsConfirmed
            ? string.Create(CultureInfo.InvariantCulture, $"{sources} · bekräftad av {label.IndependentOrigins} ursprung")
            : sources;
    }

    /// <summary>Writes one origin: its source, version and day.</summary>
    /// <param name="origin">The origin.</param>
    /// <returns>For example <c>OFAC SDN (2026-11-02), 2026-11-02</c>.</returns>
    private static string Source(LabelOrigin origin)
    {
        var day = origin.SetUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var version = string.IsNullOrWhiteSpace(origin.SourceVersion) ? string.Empty : $" {origin.SourceVersion}";
        var what = origin.OriginalSource switch
        {
            LabelSource.Own => $"egen etikett {day}",
            LabelSource.SanctionList => $"{origin.SourceName}{version}, kontrollerad {day}",
            LabelSource.OpenSource => $"öppen källa {origin.SourceName}{version}, hämtad {day}",
            LabelSource.Behaviour => $"beteende enligt {origin.SourceName}{version}, {day}",
            LabelSource.Provider => $"{origin.SourceName}{version}, hämtad {day}",
            _ => $"{origin.SourceName}{version}, {day}",
        };
        return origin.IsFromOtherCase ? "från ett annat ärende: " + what : what;
    }

    /// <summary>
    /// Says, for each swap on the trails, what the rule <see cref="SwapValue"/> did there (sketch SW1a): that the
    /// traced value follows into the new asset, with the share, or that the trail ends in the swap by the user's
    /// choice. The choice's reason never comes along, since it is the user's free text.
    /// </summary>
    /// <param name="scene">The graph shown, with its trace.</param>
    /// <param name="trails">The trails.</param>
    /// <returns>The conclusions, in chain order of the swaps.</returns>
    private static IEnumerable<ReportConclusion> SwapConclusions(GraphScene scene, IReadOnlyList<ReportTrail> trails)
    {
        var steps = trails.SelectMany(trail => trail.Steps).Select(step => step.Transaction)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var conversion in scene.Snapshot.AccountTrace?.Swaps ?? [])
        {
            var swap = conversion.Swap;
            if (!steps.Contains(swap.Transaction.Value))
            {
                continue;
            }

            var pill = SwapMarks.Of(swap).Text;
            var venue = swap.Venue is { } known ? $" genom {known.Name}" : string.Empty;
            yield return conversion.Follows
                ? new ReportConclusion(
                    $"Värdet följer med genom swappen ({pill}) i tx {swap.Transaction.Value}.",
                    $"{SwapValue.Analysis}: {conversion.ShareText} av det som gick in var spårat enligt "
                        + $"{conversion.Method}, och samma andel av det som kom ut{venue} räknas som spårat; "
                        + (conversion.Price ?? SwapValue.CheckPrice(swap, null)).Summary)
                : new ReportConclusion(
                    $"Spåret slutar i swappen ({pill}) i tx {swap.Transaction.Value}.",
                    $"utredaren valde att värdet inte följer med genom swappen ({SwapValue.Analysis} gäller annars)");
        }
    }

    /// <summary>Says that the traced amounts are counted by the method, and so are a judgement.</summary>
    /// <param name="method">The method.</param>
    /// <returns>The conclusion.</returns>
    private static ReportConclusion MethodConclusion(string method) => new(
        $"De spårade beloppen är räknade med {method}.",
        $"spårningsmetoden {method} följer pengarna genom varje tx i spåren; en annan metod ger andra belopp");
}
