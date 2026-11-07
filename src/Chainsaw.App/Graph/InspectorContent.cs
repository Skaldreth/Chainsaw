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
    /// Gets the balance per token of a chosen account on Ethereum (sketch TK1a), with its issuer and the snowflake;
    /// empty otherwise.
    /// </summary>
    public IReadOnlyList<TokenBalanceRow> Balances { get; init; } = [];

    /// <summary>Gets whether the chosen node has a balance per token to show.</summary>
    public bool HasBalances => Balances.Count > 0;

    /// <summary>Gets what the snowflake's box says, when something at the account can be frozen.</summary>
    public string? FreezeText => Balances.Select(row => row.FreezeText).OfType<string>().FirstOrDefault();

    /// <summary>Gets whether the snowflake's box is shown.</summary>
    public bool HasFreezeText => FreezeText is not null;

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
        var facts = model.Chain == Chain.Bitcoin
            ? new List<InspectorFact>
            {
                new("In", AmountText.WithUnit(node.In)),
                new("Ut", AmountText.WithUnit(node.Out)),
                new("Spårat in", Traced(scene, node.TracedIn)),
                new("Sort", node.Look.Name),
            }
            : new List<InspectorFact>
            {
                new("Kedja", model.Chain.ToString()),
                new("Spårat in", TracedPerAsset(scene, node.TracedIns)),
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
            Balances = node.Balances,
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
        var transactions = model.Transfers.Select(transfer => transfer.Transaction).Distinct().Count();
        var facts = new List<InspectorFact>
        {
            new("Belopp", AmountWithMark(model.Amount)),
            new("Spårat", Traced(scene, edge.Traced)),
            new("Tid", model.FirstTime is { } time ? Time(time) : "Obekräftad"),
            new("Tx", transactions.ToString(CultureInfo.InvariantCulture)),
        };
        if (model.Amount.Asset.Chain == Chain.Bitcoin)
        {
            facts.Add(new InspectorFact("Kontroll", Checked(scene, BitcoinTransactions(model))));
        }
        else
        {
            facts.Insert(1, new InspectorFact("Värde", edge.Value?.Text ?? NoRateText));
        }

        if (edge.Swap is { } swap)
        {
            facts.Add(new InspectorFact("Swap", swap));
        }

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

    /// <summary>What a line on Ethereum says when the case has no rate for every movement on it.</summary>
    public const string NoRateText = "Ingen kurs";

    /// <summary>Writes an amount with the token's mark first (sketch TK1a), for example <c>₮ 600,0000 USDT</c>.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns>The text.</returns>
    public static string AmountWithMark(AssetAmount amount) =>
        TokenMarks.Glyph(amount.Asset) is { Length: > 0 } glyph
            ? glyph + " " + AmountText.WithUnit(amount)
            : AmountText.WithUnit(amount);

    /// <summary>Writes the traced amounts that came in per asset, or that there is no trace.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="amounts">The traced amounts, one per asset.</param>
    /// <returns>For example <c>₮ 600,0000 USDT · Ξ 0,1000 ETH</c>, <c>0</c> or <see cref="NoTraceText"/>.</returns>
    private static string TracedPerAsset(GraphScene scene, IReadOnlyList<AssetAmount> amounts)
    {
        if (!scene.Snapshot.HasTrace)
        {
            return NoTraceText;
        }

        return amounts.Count == 0 ? "0" : string.Join(" · ", amounts.Select(AmountWithMark));
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
    private static string Traced(GraphScene scene, AssetAmount amount) =>
        scene.Snapshot.HasTrace ? AmountText.WithUnit(amount) : NoTraceText;

    /// <summary>Gives a line's Bitcoin transactions, which can be checked against their own bytes.</summary>
    /// <param name="edge">The line.</param>
    /// <returns>The transactions, each once.</returns>
    private static List<Txid> BitcoinTransactions(GraphEdge edge) =>
    [
        .. edge.Transfers
            .Select(transfer => transfer.Transaction)
            .Where(transaction => transaction.Chain == Chain.Bitcoin)
            .Select(transaction => Txid.Parse(transaction.Id))
            .Distinct(),
    ];

    /// <summary>Gives what expanding a node looks up.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The lookups; empty when it cannot be expanded.</returns>
    private static IReadOnlyList<ExpansionRequest> ExpansionOf(GraphNode node) => node.Kind switch
    {
        GraphNodeKind.Address when node.Accounts.Count > 0 =>
        [
            .. node.Accounts.Where(account => account.Chain == Chain.Ethereum)
                .Select(account => ExpansionRequest.ForEthereumAddress(EthereumAddress.Parse(account.Value))),
        ],
        GraphNodeKind.Address or GraphNodeKind.Cluster =>
            [.. node.GraphAddresses.Select(address => ExpansionRequest.ForAddress(address))],
        GraphNodeKind.Transaction when node.Transaction is { } txid => [ExpansionRequest.ForTransaction(txid)],
        // A coin that can never be spent (OP_RETURN) is all there is: expanding it would ask again for the transaction
        // the graph has and could only say that there was nothing new, so it is not offered (principle 7).
        GraphNodeKind.Coin when node.Coin is { } coin && node.Expansion != NodeExpansion.Expanded =>
            [ExpansionRequest.ForTransaction(coin.Txid)],
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
            _ when node.Accounts.Count > 0 => node.Accounts.Select(GraphScene.Written),
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
        foreach (var transfer in edge.Transfers.DistinctBy(transfer => transfer.Transaction))
        {
            lines.Add(transfer.Transaction.Id);
            lines.Add(transfer.Block is null && transfer.AccountBlock is { } known
                ? string.Create(
                    CultureInfo.InvariantCulture, $"Block {known.Height} · hashen saknas i ärendet; expandera igen")
                : BlockText(transfer.Block));
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

        if (node.Balances.FirstOrDefault(row => row.CanBeFrozen) is { FreezeText: { } freeze })
        {
            why.Add(new WhyLine("Kan frysas: " + freeze, GuideTopic.CanBeFrozen));
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
        if (!scene.Snapshot.HasTrace)
        {
            if (scene.Snapshot.TraceProblem is { } problem)
            {
                yield return new WhyLine(problem, GuideTopic.Methods);
            }

            yield break;
        }

        var method = scene.MethodText;
        if (node.TracedIn.Units.Sign > 0)
        {
            yield return new WhyLine(
                $"Spårat hit: {AmountText.WithUnit(node.TracedIn)} enligt {method}. Det är en bedömning "
                + "enligt metoden, inte ett faktum på kedjan.",
                GuideTopic.Methods);
        }

        foreach (var amount in node.TracedIns)
        {
            yield return new WhyLine(
                $"Spårat hit: {AmountWithMark(amount)} enligt {method}, räknat för sig per tillgång i "
                + "kontomodellen. Det är en bedömning enligt metoden, inte ett faktum på kedjan.",
                GuideTopic.AccountModel);
        }

        var ends = (scene.Snapshot.Trace?.Ends ?? []).Concat(scene.Snapshot.AccountTrace?.Ends ?? []);
        foreach (var end in ends.Where(end => end.NodeKey == node.Key)
            .GroupBy(end => (end.Reason, end.Amount.Amount.Asset)))
        {
            var amount = end.Skip(1).Aggregate(end.First().Amount.Amount, (sum, item) => sum + item.Amount.Amount);
            var outdated = end.Any(item => item.MayBeOutdated) ? " Det kan ha ändrats sedan ärendet frågade." : "";
            yield return new WhyLine(
                $"{end.Key.Reason.Describe()}: {AmountText.WithUnit(amount)} stannar här. {end.First().Explanation}"
                + outdated,
                GuideTopic.TrailEnds,
                GuideTopics.EndHeading(end.Key.Reason));
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

        if (!scene.Snapshot.HasTrace)
        {
            return new WhyLine(scene.Snapshot.TraceProblem ?? NoTraceText + ".", GuideTopic.Methods);
        }

        return new WhyLine($"Inga av de spårade pengarna gick här enligt {scene.MethodText}.", GuideTopic.Methods);
    }

    /// <summary>Answers what is particular to a line on Ethereum: the token, the swap and its value.</summary>
    /// <param name="edge">The line.</param>
    /// <returns>The answers.</returns>
    private static IEnumerable<WhyLine> EthereumWhy(EdgeView edge)
    {
        var asset = edge.Edge.Amount.Asset;
        if (asset.Kind == AssetKind.Token)
        {
            var issuer = KnownTokens.IssuerOf(asset);
            yield return new WhyLine(
                issuer is null
                    ? $"Token {asset.Symbol} med kontraktet {asset.Contract}; Chainsaw känner inte kontraktet, och "
                        + "symbolen kan vem som helst välja. Överföringarna är kontraktets egna Transfer-händelser."
                    : $"{asset.Symbol} från {issuer.Name}, igenkänd på kontraktet {asset.Contract}, aldrig på "
                        + "symbolen.",
                GuideTopic.Tokens);
        }

        foreach (var swap in edge.Swaps)
        {
            yield return new WhyLine(
                $"Swap i tx {swap.Transaction}: adressen skickade {AmountText.WithUnit(swap.Sold)} och fick "
                + $"{AmountText.WithUnit(swap.Bought)} tillbaka i samma tx. Pengarna bytte token men inte ägare, så "
                + "det är inget slut på spåret. Att det var en swap är en läsning av rörelserna; spårningen följer "
                + "inte bytet än, utan räknar varje tillgång för sig.",
                GuideTopic.Tokens);
        }

        yield return edge.Value is { } value
            ? new WhyLine(
                $"Värdet {value.Text} gäller hela linjens belopp på kedjan, inte bara den spårade delen, vid "
                + "varje tx:s tid: " + string.Join(" ", value.Steps),
                GuideTopic.Prices)
            : new WhyLine(
                "Ingen kurs: ärendet har inte kursen för varje rörelse på linjen vid dess tid, så inget värde "
                + "visas hellre än ett påhittat. Kurserna hämtas när grafen expanderas.",
                GuideTopic.Prices);
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
            why.AddRange(scene.Snapshot.Graph.CrossChainLinks
                .Where(link => InspectorForms.Carries(model, link))
                .Select(link => new WhyLine(
                    $"Fortsätter på {link.To.Chain}: användarens bedömning "
                    + $"{link.RecordedUtc.UtcDateTime:yyyy-MM-dd HH:mm} UTC: {link.Reason}. Beloppet är det som "
                    + $"lämnade {link.FromChain}, inte det som kom fram på den andra kedjan.",
                    GuideTopic.TrailEnds)));
        }
        else
        {
            var clustered = scene.Node(model.From)?.Node.Kind == GraphNodeKind.Cluster
                || scene.Node(model.To)?.Node.Kind == GraphNodeKind.Cluster;
            var facts = "Beloppet är summan av " + model.Transfers.Count.ToString(CultureInfo.InvariantCulture)
                + " överföringar på kedjan, som är fakta.";
            var account = model.Amount.Asset.Chain != Chain.Bitcoin;
            why.Add(new WhyLine(
                clustered ? facts + " Att adresserna hör till klustret är klustringens slutsats." : facts,
                account ? GuideTopic.AccountModel : GuideTopic.Utxo));
            if (account)
            {
                why.AddRange(EthereumWhy(edge));
            }
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
