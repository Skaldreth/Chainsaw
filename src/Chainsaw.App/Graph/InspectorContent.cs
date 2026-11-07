using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>A fact in the inspector's grid: a short name and its value.</summary>
/// <param name="Name">The name, for example <c>In</c>.</param>
/// <param name="Value">The value.</param>
public sealed record InspectorFact(string Name, string Value);

/// <summary>
/// One answer to <em>Varför?</em> (principle 3): what a conclusion rests on, in plain Swedish, with the Guide's section
/// that explains it.
/// </summary>
/// <param name="Text">The answer.</param>
/// <param name="Topic">The Guide's section, or <see langword="null"/>.</param>
/// <param name="Heading">
/// The heading in the section that answers, as written in the text; <see langword="null"/> for the section's own
/// choice (<see cref="GuideTopics.DefaultHeading"/>).
/// </param>
public sealed record WhyLine(string Text, GuideTopic? Topic, string? Heading = null);

/// <summary>What the inspector shows for what is chosen (sketch GR4): a node or a line.</summary>
/// <param name="Title">The heading: the node's name, or the line's two ends.</param>
/// <param name="Icon">The heading's icon's resource key.</param>
/// <param name="Ink">The heading icon's colour.</param>
/// <param name="Shape">The heading icon's frame.</param>
/// <param name="Facts">The facts in the grid.</param>
/// <param name="Details">
/// Addresses, txids and blocks, written out in full so that they can be checked against the chain.
/// </param>
/// <param name="Why">The answers to <em>Varför?</em>.</param>
/// <param name="Expansion">What expanding it looks up; empty when it cannot be expanded.</param>
public sealed record InspectorContent(
    string Title,
    string Icon,
    GraphInk Ink,
    NodeShape Shape,
    IReadOnlyList<InspectorFact> Facts,
    IReadOnlyList<string> Details,
    IReadOnlyList<WhyLine> Why,
    IReadOnlyList<ExpansionRequest> Expansion)
{
    /// <summary>The most addresses, transactions or blocks written out; the rest are counted.</summary>
    public const int MostDetails = 12;

    /// <summary>The icon of a chosen line.</summary>
    private const string EdgeIcon = "Icon.Kind.Swap";

    /// <summary>Gets whether the chosen node can be expanded.</summary>
    public bool CanExpand => Expansion.Count > 0;

    /// <summary>
    /// Gets whether the chosen node is a start point, which the inspector says under its heading with the flag
    /// (sketch GR5); the node keeps its own sort.
    /// </summary>
    public bool IsStartPoint { get; init; }

    /// <summary>Gives the inspector's content for a node.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="node">The node.</param>
    /// <returns>The content.</returns>
    public static InspectorContent ForNode(GraphScene scene, NodeView node)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(node);
        var model = node.Node;
        var facts = new List<InspectorFact>
        {
            new("In", AmountText.BtcWithUnit(node.In)),
            new("Ut", AmountText.BtcWithUnit(node.Out)),
            new("Spårat in", Traced(scene, node.TracedIn)),
            new("Sort", node.Look.Name),
        };
        if (model.Hop is { } hop)
        {
            facts.Add(new InspectorFact("Hopp", HopLayout.NameOf(hop)));
        }

        if (model.FirstReceived is { } first)
        {
            facts.Add(new InspectorFact("Först in", Time(first)));
        }

        return new InspectorContent(
            node.Name,
            node.Look.Icon,
            node.Look.Ink,
            node.Look.Shape,
            facts,
            NodeDetails(model),
            NodeWhy(scene, node),
            ExpansionOf(model))
        {
            IsStartPoint = model.IsStartPoint,
        };
    }

    /// <summary>Gives the inspector's content for a line.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="edge">The line.</param>
    /// <returns>The content.</returns>
    public static InspectorContent ForEdge(GraphScene scene, EdgeView edge)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(edge);
        var model = edge.Edge;
        var transactions = model.Transfers.Select(transfer => transfer.Txid).Distinct().Count();
        var facts = new List<InspectorFact>
        {
            new("Belopp", AmountText.BtcWithUnit(model.Amount)),
            new("Spårat", Traced(scene, edge.Traced)),
            new("Tid", model.FirstTime is { } time ? Time(time) : "Obekräftad"),
            new("Tx", transactions.ToString(CultureInfo.InvariantCulture)),
            new("Kontroll", Checked(scene, model.Transfers.Select(transfer => transfer.Txid).Distinct().ToList())),
        };
        var title = $"{scene.Node(model.From)?.Name ?? model.From} → {scene.Node(model.To)?.Name ?? model.To}";
        return new InspectorContent(
            title, EdgeIcon, edge.Ink, NodeShape.Round, facts, EdgeDetails(model), EdgeWhy(scene, edge), []);
    }

    /// <summary>What a line says when every transaction on it is checked against its own bytes.</summary>
    public const string CheckedText = "Mot tx:ens bytes";

    /// <summary>What a line says when no transaction on it is checked against its own bytes yet.</summary>
    public const string UncheckedText = "Inte mot bytes än";

    /// <summary>
    /// Says whether a line's transactions are checked against their own bytes (txid as their double SHA-256), or only
    /// rest on the source; a transaction is checked when it is expanded by itself.
    /// </summary>
    /// <param name="scene">The scene.</param>
    /// <param name="transactions">The line's transactions.</param>
    /// <returns>The text for the fact.</returns>
    public static string Checked(GraphScene scene, IReadOnlyCollection<Txid> transactions)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(transactions);
        var known = scene.Snapshot.Graph.Transactions;
        var verified = transactions.Count(txid => known.TryGetValue(txid, out var entry)
            && entry.Transaction.BytesVerified);
        if (verified == transactions.Count && verified > 0)
        {
            return CheckedText;
        }

        return verified == 0
            ? UncheckedText
            : string.Create(CultureInfo.InvariantCulture, $"{verified} av {transactions.Count} tx mot bytes");
    }

    /// <summary>What a traced amount says when there is no trace to read it from.</summary>
    public const string NoTraceText = "Ingen spårning";

    /// <summary>Writes a time in local time with the UTC time after it, both with their date (principle 1).</summary>
    /// <param name="time">The time.</param>
    /// <returns>For example <c>2026-11-07 13:00 (2026-11-07 12:00 UTC)</c>.</returns>
    public static string Time(DateTimeOffset time) => string.Create(
        CultureInfo.InvariantCulture,
        $"{time.ToLocalTime():yyyy-MM-dd HH:mm} ({time.UtcDateTime:yyyy-MM-dd HH:mm} UTC)");

    /// <summary>
    /// Writes a traced amount, or that there is no trace: without one, no amount is a method's result (principle 2).
    /// </summary>
    /// <param name="scene">The scene.</param>
    /// <param name="amount">The traced amount.</param>
    /// <returns>The amount with its unit, or <see cref="NoTraceText"/>.</returns>
    private static string Traced(GraphScene scene, Satoshi amount) =>
        scene.Snapshot.Trace is null ? NoTraceText : AmountText.BtcWithUnit(amount);

    /// <summary>Gives what expanding a node looks up.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The lookups; empty when it cannot be expanded.</returns>
    private static IReadOnlyList<ExpansionRequest> ExpansionOf(GraphNode node) => node.Kind switch
    {
        GraphNodeKind.Address or GraphNodeKind.Cluster =>
            [.. node.GraphAddresses.Select(address => ExpansionRequest.ForAddress(address))],
        GraphNodeKind.Transaction when node.Transaction is { } txid => [ExpansionRequest.ForTransaction(txid)],
        GraphNodeKind.Coin when node.Coin is { } coin => [ExpansionRequest.ForTransaction(coin.Txid)],
        _ => [],
    };

    /// <summary>Gives a node's addresses or ids, written out in full.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The lines.</returns>
    private static IReadOnlyList<string> NodeDetails(GraphNode node)
    {
        IEnumerable<string> lines = node.Kind switch
        {
            GraphNodeKind.Transaction when node.Transaction is { } txid => [txid.Value],
            GraphNodeKind.Coin when node.Coin is { } coin => [coin.ToString()],
            GraphNodeKind.OtherChain when node.OtherChain is { } other => [other.ToString()],
            _ => node.GraphAddresses.Select(address => address.Value),
        };
        return Limit(lines.ToList());
    }

    /// <summary>Gives a line's transactions and their blocks, so that each can be checked against the chain.</summary>
    /// <param name="edge">The line.</param>
    /// <returns>The lines: a txid, then its block's height and hash.</returns>
    private static IReadOnlyList<string> EdgeDetails(GraphEdge edge)
    {
        var lines = new List<string>();
        foreach (var transfer in edge.Transfers.DistinctBy(transfer => transfer.Txid))
        {
            lines.Add(transfer.Txid.Value);
            lines.Add(BlockText(transfer.Block));
        }

        return Limit(lines, MostDetails * 2);
    }

    /// <summary>Writes a block's height and hash, or that the transaction is unconfirmed.</summary>
    /// <param name="block">The block.</param>
    /// <returns>For example <c>Block 921344 · 0000…</c>, the height as other tools write it.</returns>
    private static string BlockText(BlockReference? block) => block is null
        ? "Obekräftad: inte i något block än"
        : string.Create(CultureInfo.InvariantCulture, $"Block {block.Height} · {block.Hash.Value}");

    /// <summary>Keeps the first lines and counts the rest.</summary>
    /// <param name="lines">The lines.</param>
    /// <param name="most">How many to keep.</param>
    /// <returns>The kept lines, and <c>och 3 till</c> when some were left out.</returns>
    private static IReadOnlyList<string> Limit(IReadOnlyList<string> lines, int most = MostDetails) =>
        lines.Count <= most
            ? lines
            : [.. lines.Take(most), string.Create(CultureInfo.InvariantCulture, $"och {lines.Count - most} till")];

    /// <summary>Answers <em>Varför?</em> for a node: its start, labels, ends, traced money and expansion.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="node">The node.</param>
    /// <returns>The answers; the cluster's are added when Clustering has answered.</returns>
    private static IReadOnlyList<WhyLine> NodeWhy(GraphScene scene, NodeView node)
    {
        var model = node.Node;
        var why = new List<WhyLine>();
        if (model.IsStartPoint)
        {
            why.Add(new WhyLine(
                "Startpunkt i ärendet. Spåret räknas från mynten som startpunkten tog emot, eller från en "
                + "startpunkts-tx:s betalningar till andra än betalarna; det är ett val, inte en slutsats. "
                + "Startpunkten säger inget om vem som äger den: den kan lika gärna vara den misstänktes som offrets.",
                GuideTopic.StartPoints));
        }

        why.AddRange(node.Labels.Select(label => new WhyLine(label.Explain(), GuideTopic.Labels)));
        if (node.Labels.Count == 0 && model.Kind is GraphNodeKind.Address or GraphNodeKind.Cluster)
        {
            why.Add(new WhyLine("Ingen etikett: ärendet vet inte vem som äger adressen.", GuideTopic.Labels));
        }

        why.AddRange(model.Ends.Select(end =>
            EndLine($"{end.Reason.Describe()}: {end.Explanation} ({end.Source})", end.Reason)));
        why.AddRange(TraceWhy(scene, node));
        why.AddRange(ExpansionWhy(model));
        if (model.Hop is { } hop)
        {
            why.Add(new WhyLine(
                $"Hopp {HopLayout.NameOf(hop)} från startpunkten: grafens ordning, inte ett faktum om ägarna.", null));
        }

        return why;
    }

    /// <summary>
    /// Answers why a trail ends at a node, linked to the Guide: a CoinJoin to its own section, any other end to the
    /// heading for its kind of end.
    /// </summary>
    /// <param name="text">The answer.</param>
    /// <param name="reason">The end's reason.</param>
    /// <returns>The answer with its section.</returns>
    private static WhyLine EndLine(string text, TrailEndReason reason) => reason == TrailEndReason.CoinJoin
        ? new WhyLine(text, GuideTopic.CoinJoin)
        : new WhyLine(text, GuideTopic.TrailEnds, GuideTopics.EndHeading(reason));

    /// <summary>Answers what the trace says about a node: how much reached it and where the money stops there.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="node">The node.</param>
    /// <returns>The answers.</returns>
    private static IEnumerable<WhyLine> TraceWhy(GraphScene scene, NodeView node)
    {
        if (scene.Snapshot.Trace is not { } trace)
        {
            if (scene.Snapshot.TraceProblem is { } problem)
            {
                yield return new WhyLine(problem, GuideTopic.Methods);
            }

            yield break;
        }

        if (node.TracedIn.Value > 0)
        {
            yield return new WhyLine(
                $"Spårat hit: {AmountText.BtcWithUnit(node.TracedIn)} enligt {trace.Method}. Det är en bedömning "
                + "enligt metoden, inte ett faktum på kedjan.",
                GuideTopic.Methods);
        }

        foreach (var end in trace.Ends.Where(end => end.NodeKey == node.Key).GroupBy(end => end.Reason))
        {
            var amount = end.Aggregate(Satoshi.Zero, (sum, item) => sum + item.Amount.Amount);
            var outdated = end.Any(item => item.MayBeOutdated) ? " Det kan ha ändrats sedan ärendet frågade." : "";
            yield return new WhyLine(
                $"{end.Key.Describe()}: {AmountText.BtcWithUnit(amount)} stannar här. {end.First().Explanation}"
                + outdated,
                GuideTopic.TrailEnds,
                GuideTopics.EndHeading(end.Key));
        }
    }

    /// <summary>Answers what the trace says about a line, or why there is no trace.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="edge">The line.</param>
    /// <returns>The answer.</returns>
    private static WhyLine TracedWhy(GraphScene scene, EdgeView edge)
    {
        if (edge.TracedEdge is { } traced)
        {
            return new WhyLine(traced.Amount.Explain(), GuideTopic.Methods);
        }

        if (scene.Snapshot.Trace is null)
        {
            return new WhyLine(scene.Snapshot.TraceProblem ?? NoTraceText + ".", GuideTopic.Methods);
        }

        return new WhyLine($"Inga av de spårade pengarna gick här enligt {scene.MethodText}.", GuideTopic.Methods);
    }

    /// <summary>Says how far a node is expanded, when not all the way.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The answer, or nothing.</returns>
    private static IEnumerable<WhyLine> ExpansionWhy(GraphNode node)
    {
        if (node.Kind is not (GraphNodeKind.Address or GraphNodeKind.Cluster or GraphNodeKind.Transaction))
        {
            yield break;
        }

        var text = node.Expansion switch
        {
            NodeExpansion.NotExpanded => "Inte expanderad ännu: det som går vidare härifrån finns inte i grafen.",
            NodeExpansion.Partly =>
                "Delvis expanderad: en del av historiken är inte hämtad (gränsen nåddes eller ett uppslag "
                + "misslyckades).",
            _ => null,
        };
        if (text is not null)
        {
            yield return new WhyLine(text, null);
        }
    }

    /// <summary>Answers <em>Varför?</em> for a line: the facts it sums, the traced part and what is uncertain.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="edge">The line.</param>
    /// <returns>The answers.</returns>
    private static IReadOnlyList<WhyLine> EdgeWhy(GraphScene scene, EdgeView edge)
    {
        var model = edge.Edge;
        var why = new List<WhyLine>();
        if (model.Kind == GraphEdgeKind.ContinuesOnOtherChain)
        {
            var coins = model.Transfers.Select(transfer => transfer.Coin).ToHashSet();
            why.AddRange(scene.Snapshot.Graph.CrossChainLinks
                .Where(link => coins.Contains(link.From))
                .Select(link => new WhyLine(
                    $"Fortsätter på {link.To.Chain}: användarens bedömning "
                    + $"{link.RecordedUtc.UtcDateTime:yyyy-MM-dd HH:mm} UTC: {link.Reason}. Beloppet är myntets på "
                    + "Bitcoin, inte det som kom fram på den andra kedjan.",
                    GuideTopic.TrailEnds)));
        }
        else
        {
            var clustered = scene.Node(model.From)?.Node.Kind == GraphNodeKind.Cluster
                || scene.Node(model.To)?.Node.Kind == GraphNodeKind.Cluster;
            var facts = "Beloppet är summan av " + model.Transfers.Count.ToString(CultureInfo.InvariantCulture)
                + " överföringar på kedjan, som är fakta.";
            why.Add(new WhyLine(
                clustered ? facts + " Att adresserna hör till klustret är klustringens slutsats." : facts,
                GuideTopic.Utxo));
        }

        why.Add(TracedWhy(scene, edge));
        if (edge.Dash == EdgeDash.Uncertain)
        {
            why.Add(new WhyLine(
                "Streckad: något linjen vilar på kan ändras, till exempel en obekräftad tx eller ett svar som inte "
                + "kunde frågas om igen.",
                null));
        }

        if (model.Direction == EdgeDirection.Back)
        {
            why.Add(new WhyLine("Pengarna vänder tillbaka till ett tidigare hopp, och ritas som en båge.", null));
        }

        return why;
    }
}
