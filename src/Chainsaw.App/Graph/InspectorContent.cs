using System.Globalization;
using Chainsaw.Modules.Attribution.Contracts;
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

/// <summary>
/// One id the inspector writes out in full in its dark box (sketch IN2b), in Cascadia Mono with a copy button, so that
/// it can be checked against the chain with any tool (principle 1).
/// </summary>
/// <param name="Value">The id: an address, a txid, a coin or the other chain's id.</param>
/// <param name="Note">
/// What stands small under it, for a transaction its block's height and hash; <see langword="null"/> for none.
/// </param>
public sealed record InspectorDetail(string Value, string? Note = null)
{
    /// <summary>Gets whether a note stands under the id.</summary>
    public bool HasNote => Note is not null;
}

/// <summary>
/// What the inspector shows for what is chosen (sketches IN2b, GR5, TK1a and AN1b): the heading with the sort's icon,
/// the name and a line under it, the facts and the ids in full. The inspector only reads; the actions stand at the
/// node.
/// </summary>
/// <param name="Title">The heading: the node's name, or the line's two ends.</param>
/// <param name="Icon">The heading's icon's resource key.</param>
/// <param name="Ink">The heading icon's colour.</param>
/// <param name="Shape">The heading icon's frame.</param>
/// <param name="Facts">The facts in the grid.</param>
/// <param name="Ids">
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
    IReadOnlyList<InspectorDetail> Ids,
    IReadOnlyList<WhyLine> Why,
    IReadOnlyList<ExpansionRequest> Expansion)
{
    /// <summary>The most addresses, transactions or blocks written out; the rest are counted.</summary>
    public const int MostDetails = 12;

    /// <summary>What the line under the heading says for a start point (sketch IN2b).</summary>
    public const string StartPointWord = "startpunkt";

    /// <summary>What the line under a chosen line's heading starts with.</summary>
    public const string LineWord = "Linje";

    /// <summary>The icon of a chosen line.</summary>
    private const string EdgeIcon = "Icon.Kind.Swap";

    /// <summary>The Swedish culture numbers are written in, with a space between the thousands.</summary>
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>
    /// Gets the line under the heading (sketch IN2b): the sort, the chain and, for a start point, that the trace
    /// starts there, for example <c>Okänd · Bitcoin · startpunkt</c>. The node keeps its own sort (sketch GR5).
    /// </summary>
    public string Subtitle { get; init; } = string.Empty;

    /// <summary>
    /// Gets whether the heading is a name (a label's or the user's own), drawn in the text's colour; a node without a
    /// name is headed by its address shortened, in the muted grey (sketch GR5).
    /// </summary>
    public bool IsNamed { get; init; } = true;

    /// <summary>Gets whether the heading icon's frame is dashed, as the node's is (an uncertain sort).</summary>
    public bool IsDashed { get; init; }

    /// <summary>
    /// Gets what is said after the ids when more were left out, for example <c>och 3 till</c>; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public string? MoreIds { get; init; }

    /// <summary>Gets whether some ids were left out.</summary>
    public bool HasMoreIds => MoreIds is not null;

    /// <summary>Gets whether there are ids to write out.</summary>
    public bool HasIds => Ids.Count > 0;

    /// <summary>
    /// Gets the ids as lines: each id, then its note. A plain view of <see cref="Ids"/>, for reading and for tests.
    /// </summary>
    public IReadOnlyList<string> Details =>
    [
        .. Ids.SelectMany(detail => detail.Note is { } note ? [detail.Value, note] : new[] { detail.Value }),
    ];

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
    /// Gets whether the chosen node is a start point, which the line under the heading ends with (sketch IN2b);
    /// the node keeps its own sort (sketch GR5).
    /// </summary>
    public bool IsStartPoint { get; init; }

    /// <summary>
    /// Gets whether a comment is chosen (sketch KN1a): it has no <em>Varför?</em>, since it is the user's own words and
    /// rests on nothing Chainsaw worked out, and a button that cannot be used is not shown (principle 7).
    /// </summary>
    public bool IsComment { get; init; }

    /// <summary>Gets whether the heading shows <em>Varför?</em>.</summary>
    public bool HasWhy => !IsComment;

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
            }
            : new List<InspectorFact> { new("Spårat in", TracedPerAsset(scene, node.TracedIns)) };
        var lines = scene.Edges.Where(edge => edge.Edge.From == node.Key || edge.Edge.To == node.Key)
            .SelectMany(edge => edge.Edge.Transfers)
            .ToList();
        if (lines.Count > 0)
        {
            var transactions = lines.Select(transfer => transfer.Transaction).Distinct().Count();
            facts.Add(new InspectorFact("Tx", Count(transactions)));
        }

        if (model.Hop is { } hop)
        {
            facts.Add(new InspectorFact("Hopp", HopLayout.NameOf(hop)));
        }

        if (model.FirstReceived is { } first)
        {
            facts.Add(new InspectorFact("Först in", Time(first)));
        }

        if (lines.Select(transfer => transfer.Height).Max() is { } latest)
        {
            facts.Add(new InspectorFact("Senast", "block " + Count(latest)));
        }

        if (node.Badges.Contains(NodeBadge.FormerlySanctioned)
            && node.Labels.FirstOrDefault(label => label.Kind == LabelKind.FormerlySanctioned) is { } former)
        {
            // The dates stand in the fact; the notices are in Varför?.
            facts.Add(new InspectorFact(FormerSanctionFact, $"Tidigare sanktionerad, {former.Name}"));
        }

        var (ids, more) = Limit(NodeDetails(model));
        return new InspectorContent(
            node.Name,
            node.Look.Icon,
            node.Look.Ink,
            node.Look.Shape,
            facts,
            ids,
            NodeWhy(scene, node),
            ExpansionOf(model))
        {
            IsStartPoint = model.IsStartPoint,
            Balances = node.Balances,
            Subtitle = NodeSubtitle(node),
            IsNamed = node.IsNamed,
            IsDashed = node.Look.IsDashed,
            MoreIds = more,
        };
    }

    /// <summary>
    /// Writes the line under a node's heading (sketch IN2b): its sort, its chain, how many addresses its cluster has,
    /// and <see cref="StartPointWord"/> when the trace starts there.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>For example <c>Okänd · Bitcoin · startpunkt</c> or <c>Kluster · Bitcoin · 6 adresser</c>.</returns>
    public static string NodeSubtitle(NodeView node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var parts = new List<string> { node.Look.Name, node.Node.Chain.ToString() };
        if (node.Count.Length > 0)
        {
            parts.Add(node.Count + " adresser");
        }

        if (node.Node.IsStartPoint)
        {
            parts.Add(StartPointWord);
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Writes a whole number as Swedish does, with a space between the thousands.</summary>
    /// <param name="number">The number.</param>
    /// <returns>For example <c>974 990</c>.</returns>
    private static string Count(long number) => number.ToString("#,##0", Swedish);

    /// <summary>What the line under a chosen comment's heading says (sketch KN1a).</summary>
    public const string CommentWord = "Kommentar";

    /// <summary>What a comment's thread says when it goes to no node.</summary>
    public const string NoThreadText = "Ingen nod";

    /// <summary>
    /// Gives the inspector's content for a chosen comment (sketch KN1a): its first line as the heading, who wrote it and
    /// when, who changed it last, and the node its thread goes to. The inspector only reads; the note is written in on
    /// the surface, and its history is under <em>Historik</em>.
    /// </summary>
    /// <param name="comment">The comment.</param>
    /// <param name="thread">The name of the node its thread goes to, or <see langword="null"/> when none is shown.</param>
    /// <returns>The content.</returns>
    public static InspectorContent ForComment(GraphComment comment, string? thread)
    {
        ArgumentNullException.ThrowIfNull(comment);
        var title = CommentRules.Title(comment.Text);
        var facts = new List<InspectorFact>
        {
            new("Skriven av", comment.CreatedBy.Name),
            new("Skriven", Time(comment.CreatedUtc)),
            new("Senast ändrad av", comment.ChangedBy.Name),
            new("Senast ändrad", Time(comment.ChangedUtc)),
            new("Tråd till", thread ?? NoThreadText),
        };
        return new InspectorContent(
            title.Length > 0 ? title : CommentWord, "Icon.Note", GraphInk.Unknown, NodeShape.Square, facts, [], [], [])
        {
            Subtitle = CommentWord,
            IsComment = true,
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
        facts.Insert(1, new InspectorFact("Värde", edge.Value?.Text ?? NoRateText));
        if (model.Amount.Asset.Chain == Chain.Bitcoin)
        {
            facts.Add(new InspectorFact("Kontroll", Checked(scene, BitcoinTransactions(model))));
        }

        if (edge.Swap is { } swap)
        {
            facts.Add(new InspectorFact("Swap", swap));
        }

        var title = $"{scene.Node(model.From)?.Name ?? model.From} → {scene.Node(model.To)?.Name ?? model.To}";
        var (ids, more) = Limit(EdgeDetails(model));
        return new InspectorContent(title, EdgeIcon, edge.Ink, NodeShape.Round, facts, ids, EdgeWhy(scene, edge), [])
        {
            Subtitle = EdgeSubtitle(scene, edge),
            IsDashed = edge.Dash != EdgeDash.None,
            MoreIds = more,
        };
    }

    /// <summary>
    /// Writes the line under a chosen line's heading: <see cref="LineWord"/> and its chain, and the other chain when
    /// the money continues there.
    /// </summary>
    /// <param name="scene">The scene.</param>
    /// <param name="edge">The line.</param>
    /// <returns>For example <c>Linje · Bitcoin</c> or <c>Linje · Bitcoin → Ethereum</c>.</returns>
    public static string EdgeSubtitle(GraphScene scene, EdgeView edge)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(edge);
        var subtitle = $"{LineWord} · {edge.Edge.Amount.Asset.Chain}";
        return edge.Edge.Kind == GraphEdgeKind.ContinuesOnOtherChain
            && scene.Node(edge.Edge.To)?.Node.OtherChain is { } other
            ? $"{subtitle} → {other.Chain}"
            : subtitle;
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

    /// <summary>The name of the fact that says an actor was on a sanction list and has been taken off it.</summary>
    public const string FormerSanctionFact = "Sanktion";

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
    /// <returns>The ids.</returns>
    private static List<InspectorDetail> NodeDetails(GraphNode node)
    {
        IEnumerable<string> lines = node.Kind switch
        {
            GraphNodeKind.Transaction when node.Transaction is { } txid => [txid.Value],
            GraphNodeKind.Coin when node.Coin is { } coin => [coin.ToString()],
            GraphNodeKind.OtherChain when node.OtherChain is { } other => [other.ToString()],
            _ when node.Accounts.Count > 0 => node.Accounts.Select(GraphScene.Written),
            _ => node.GraphAddresses.Select(address => address.Value),
        };
        return [.. lines.Select(line => new InspectorDetail(line))];
    }

    /// <summary>Gives a line's transactions and their blocks, so that each can be checked against the chain.</summary>
    /// <param name="edge">The line.</param>
    /// <returns>The ids: each txid with its block's height and hash under it.</returns>
    private static List<InspectorDetail> EdgeDetails(GraphEdge edge) =>
    [
        .. edge.Transfers.DistinctBy(transfer => transfer.Transaction).Select(transfer => new InspectorDetail(
            transfer.Transaction.Id,
            transfer.Block is null && transfer.AccountBlock is { } known
                ? string.Create(
                    CultureInfo.InvariantCulture, $"Block {known.Height} · hashen saknas i ärendet; expandera igen")
                : BlockText(transfer.Block))),
    ];

    /// <summary>Writes a block's height and hash, or that the transaction is unconfirmed.</summary>
    /// <param name="block">The block.</param>
    /// <returns>For example <c>Block 921344 · 0000…</c>, the height as other tools write it.</returns>
    private static string BlockText(BlockReference? block) => block is null
        ? "Obekräftad: inte i något block än"
        : string.Create(CultureInfo.InvariantCulture, $"Block {block.Height} · {block.Hash.Value}");

    /// <summary>Keeps the first <see cref="MostDetails"/> ids and counts the rest.</summary>
    /// <param name="ids">The ids.</param>
    /// <returns>The kept ids, and <c>och 3 till</c> when some were left out, otherwise <see langword="null"/>.</returns>
    private static (IReadOnlyList<InspectorDetail> Kept, string? More) Limit(List<InspectorDetail> ids) =>
        ids.Count <= MostDetails
            ? (ids, null)
            : ([.. ids.Take(MostDetails)], "och " + Count(ids.Count - MostDetails) + " till");

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
        why.AddRange(RefusedSwapWhy(scene, refusal => model.Accounts.Contains(refusal.Swap.Account)
            || model.Accounts.Contains(refusal.Counterparty)));
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

        var viaSwap = scene.Edges
            .Where(edge => edge.Edge.To == node.Key && edge.TracedEdge?.Amount.RestsOnSwap == true)
            .Select(edge => edge.Traced.Asset)
            .ToHashSet();
        foreach (var amount in node.TracedIns)
        {
            yield return viaSwap.Contains(amount.Asset)
                ? new WhyLine(
                    $"Spårat hit: {AmountWithMark(amount)} enligt {method}, av ett värde som följt med genom en swap "
                    + $"enligt {SwapValue.Analysis}. Det är en bedömning, inte ett faktum på kedjan.",
                    GuideTopic.Swaps)
                : new WhyLine(
                    $"Spårat hit: {AmountWithMark(amount)} enligt {method}, räknat för sig per tillgång i "
                    + "kontomodellen. Det är en bedömning enligt metoden, inte ett faktum på kedjan.",
                    GuideTopic.AccountModel);
        }

        var ends = (scene.Snapshot.Trace?.Ends ?? []).Concat(scene.Snapshot.AccountTrace?.Ends ?? []).ToList();
        var swapped = ends.Count(end => end.NodeKey == node.Key && end.Reason == TrailEndReason.Swapped);
        if (swapped > 0)
        {
            // The same money can go through several swaps here, back and forth, so the amounts are never added.
            yield return new WhyLine(
                $"Gick in i {swapped} swap(par) här och följer med i den nya tillgången enligt {SwapValue.Analysis}; "
                + "samma pengar kan gå genom flera swappar, så beloppen läggs inte ihop. Det är inget slut på spåret.",
                GuideTopic.Swaps);
        }

        foreach (var end in ends.Where(end => end.NodeKey == node.Key && end.Reason != TrailEndReason.Swapped)
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

    /// <summary>Answers what is particular to a line on Ethereum: the token and the swap.</summary>
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
                + $"{AmountText.WithUnit(swap.Bought)} tillbaka i samma tx{VenueText(swap)}. Pengarna bytte token men "
                + "inte ägare. Att det var en swap är en läsning av rörelserna. " + SwapChoiceWhy(edge, swap),
                GuideTopic.Swaps);
        }
    }

    /// <summary>
    /// Says why a transaction that would be a swap is none (SW1a): a counterparty is one the trail must reach, by a
    /// label or Chainsaw's list, so the money goes on to it as a payment and no choice is asked for.
    /// </summary>
    /// <param name="scene">The scene.</param>
    /// <param name="concerns">Which refusals the chosen thing concerns.</param>
    /// <returns>The answers.</returns>
    private static IEnumerable<WhyLine> RefusedSwapWhy(GraphScene scene, Func<SwapRefusal, bool> concerns) =>
        scene.Snapshot.Graph.Account.RefusedSwaps.Where(concerns).Select(refusal => new WhyLine(
            $"Ingen swap i tx {refusal.Swap.Transaction.Value}: motparten "
                + $"{GraphScene.Shorten(refusal.Counterparty.Value)}"
                + $" har {refusal.Why}, och dit ska spåret nå. Pengarna följer med som en vanlig betalning.",
            GuideTopic.Swaps));

    /// <summary>Says whether a refused swap's transaction is on a line from or to the account that would swap.</summary>
    /// <param name="refusal">The refused swap.</param>
    /// <param name="edge">The line.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool IsOnLine(SwapRefusal refusal, EdgeView edge)
    {
        var account = GraphScene.AccountKey(refusal.Swap.Account);
        return (edge.Edge.From == account || edge.Edge.To == account)
            && edge.Edge.Transfers.Any(transfer => transfer.Transaction.Id == refusal.Swap.Transaction.Value);
    }

    /// <summary>
    /// Says what the rule <see cref="SwapValue"/> did at a swap (sketch SW1a): the steps when the traced value
    /// follows, the user's choice when it does not, or that no traced money reached it.
    /// </summary>
    /// <param name="edge">The line.</param>
    /// <param name="swap">One of its swaps.</param>
    /// <returns>The answer.</returns>
    private static string SwapChoiceWhy(EdgeView edge, TokenSwap swap)
    {
        var price = edge.SwapPrice is { } check && check.Swap.Equals(swap) ? " " + check.Explain() : string.Empty;
        if (edge.SwapPrice is { IsUnreasonable: true } absurd && absurd.Swap.Equals(swap))
        {
            return absurd.Explain();
        }

        return ChoiceWhy(edge, swap) + price;
    }

    /// <summary>Says what the user chose at a swap, or what the rule did there.</summary>
    /// <param name="edge">The line.</param>
    /// <param name="swap">One of its swaps.</param>
    /// <returns>The answer.</returns>
    private static string ChoiceWhy(EdgeView edge, TokenSwap swap)
    {
        if (edge.SwapStops.FirstOrDefault(stop => stop.Is(swap)) is { } stop)
        {
            var reason = stop.Reason is null ? string.Empty : $" ({stop.Reason})";
            return $"Spåret slutar i swappen: du valde {NodeHistory.Short(stop.RecordedUtc)} att värdet inte följer "
                + $"med{reason}. Valet kan tas tillbaka i pillret eller under Historik.";
        }

        return edge.SwapConversions.FirstOrDefault(found => found.Swap == swap) is { } conversion
            ? conversion.Explain()
            : $"Värdet följer med enligt {SwapValue.Analysis} (förval), men inga spårade pengar nådde swappen.";
    }

    /// <summary>Says which known exchange a swap went through, when Chainsaw knows the contract.</summary>
    /// <param name="swap">The swap.</param>
    /// <returns>For example <c>, genom Uniswap V2 Router02 (kontraktet 0x…, enligt …)</c>, or nothing.</returns>
    private static string VenueText(TokenSwap swap) => swap.Venue is { } venue
        ? $", genom {venue.Name} (kontraktet {venue.Address}, {venue.Provenance()} i {KnownContracts.Name} "
            + $"{KnownContracts.Version})"
        : string.Empty;

    /// <summary>
    /// Answers what a line was worth (sketch KU1a): the value with its steps, or why there is none; never a value
    /// made up.
    /// </summary>
    /// <param name="edge">The line.</param>
    /// <returns>The answer.</returns>
    private static WhyLine ValueWhy(EdgeView edge)
    {
        if (edge.Value is { } value)
        {
            return new WhyLine(
                $"Värdet {value.Text} gäller hela linjens belopp på kedjan, inte bara den spårade delen, vid "
                + "varje tx:s tid; klicka på beloppet för stegen: " + string.Join(" ", value.Steps),
                GuideTopic.Prices);
        }

        var why = edge.ValueGap?.Tip ?? "Ärendet har inte kursen för varje rörelse på linjen vid dess tid.";
        return new WhyLine(
            "Ingen kurs: " + why + " Inget värde visas hellre än ett påhittat.", GuideTopic.Prices);
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
                why.AddRange(RefusedSwapWhy(scene, refusal => IsOnLine(refusal, edge)));
            }

            why.Add(ValueWhy(edge));
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
