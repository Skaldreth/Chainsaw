using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Search;

/// <summary>
/// Reads what the search can find in the open case's graph (sketch KR1a): every label, address, transaction and
/// cluster, each with where in the graph it is shown. Only what the case already holds is read; nothing is fetched.
/// </summary>
internal sealed class SceneEntries
{
    /// <summary>The words for a node that lies in a pile (sketch EX2b).</summary>
    public const string InPileText = "i en hög";

    /// <summary>The words for an actor that lies in the trash (sketch SO1a).</summary>
    public const string InTrashText = "I soptunnan";

    /// <summary>The words for a transaction the case has fetched that no line in the graph shows.</summary>
    public const string NotInGraphText = "Hämtad, inte i grafen";

    /// <summary>The words for a transaction not yet in a block.</summary>
    public const string UnconfirmedText = "obekräftad";

    /// <summary>The Swedish culture, for heights and times.</summary>
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>The graph.</summary>
    private readonly GraphScene _scene;

    /// <summary>Every actor that can be shown, by key: drawn, or as a card in a pile.</summary>
    private readonly Dictionary<string, (NodeView Node, string Focus, bool InPile)> _actors =
        new(StringComparer.Ordinal);

    /// <summary>The entries read so far.</summary>
    private readonly List<SearchEntry> _entries = [];

    /// <summary>Starts reading a graph.</summary>
    /// <param name="scene">The graph.</param>
    private SceneEntries(GraphScene scene)
    {
        _scene = scene;
        foreach (var node in scene.Nodes)
        {
            _actors[node.Key] = (node, node.Key, false);
        }

        foreach (var pile in scene.Piles.Values)
        {
            foreach (var card in pile.Cards)
            {
                _actors.TryAdd(card.Key, (card.Node, pile.Key, true));
            }
        }
    }

    /// <summary>Reads everything the search can find in a graph.</summary>
    /// <param name="scene">The graph.</param>
    /// <returns>The entries.</returns>
    public static List<SearchEntry> Read(GraphScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var reader = new SceneEntries(scene);
        reader.ReadActors();
        reader.ReadTrashed();
        reader.ReadLabels();
        reader.ReadTransactions();
        return reader._entries;
    }

    /// <summary>Reads the actors: each address, and each cluster as a whole.</summary>
    private void ReadActors()
    {
        foreach (var (node, focus, inPile) in _actors.Values)
        {
            if (node.Node.Kind == GraphNodeKind.Cluster)
            {
                _entries.Add(Cluster(node, focus, inPile));
            }

            foreach (var address in node.Node.Addresses)
            {
                _entries.Add(Address(node, address, focus, inPile));
            }
        }
    }

    /// <summary>
    /// Reads the actors that lie in the trash: the trash is only a filter, so they are still in the case and are found,
    /// each with the row <em>I soptunnan</em> instead of a place in the graph.
    /// </summary>
    private void ReadTrashed()
    {
        foreach (var node in _scene.Snapshot.Graph.Nodes)
        {
            if (_actors.ContainsKey(node.Key) || !_scene.Trash.Hides(node))
            {
                continue;
            }

            var labels = _scene.Snapshot.Labels.GetValueOrDefault(node.Key) ?? [];
            var look = VisualLanguage.Of(GraphScene.SortOf(node, labels));
            var owner = labels.FirstOrDefault(label => label.Name is not null)?.Name ?? look.Name;
            if (node.Kind == GraphNodeKind.Cluster)
            {
                _entries.Add(Trashed(SearchKind.Cluster, owner, $"Kluster · {node.ClusterSize} adresser", look) with
                {
                    Aliases = [.. node.Addresses.Select(address => address.Value)],
                });
            }

            foreach (var address in node.Addresses)
            {
                _entries.Add(Trashed(SearchKind.Address, address.Value, owner, look) with { IsMono = true });
            }
        }
    }

    /// <summary>Makes the entry of something that lies in the trash: not shown in the graph, and said so.</summary>
    /// <param name="kind">What it is.</param>
    /// <param name="text">What it is called.</param>
    /// <param name="what">What it is, in a few words.</param>
    /// <param name="look">Its sort, as the graph would draw it.</param>
    /// <returns>The entry.</returns>
    private static SearchEntry Trashed(SearchKind kind, string text, string what, SortLook look)
    {
        var sub = Join(what, InTrashText);
        var lead = $"{what}. Ligger i soptunnan och visas inte i grafen; den kan läggas tillbaka därifrån.";
        return new SearchEntry(kind, text, sub, string.Empty, lead, SearchLook.Of(look), null, null)
        {
            IsInTrash = true,
        };
    }

    /// <summary>Makes the entry of a cluster, found by its name or by any of its addresses.</summary>
    /// <param name="node">The cluster.</param>
    /// <param name="focus">Where it is shown.</param>
    /// <param name="inPile">Whether it lies in a pile.</param>
    /// <returns>The entry.</returns>
    private static SearchEntry Cluster(NodeView node, string focus, bool inPile)
    {
        var count = node.Node.ClusterSize;
        var sub = Join($"Kluster · {count} adresser", inPile ? InPileText : null);
        var lead = $"{count} adresser som klustringen bedömer har samma ägare. En bedömning, inte ett faktum.";
        return new SearchEntry(
            SearchKind.Cluster, node.Name, sub, AmountOf(node), lead, SearchLook.Of(node.Look), focus, null)
        {
            Aliases = [.. node.Node.Addresses.Select(address => address.Value)],
        };
    }

    /// <summary>Makes the entry of one address, named after the actor it belongs to.</summary>
    /// <param name="node">The actor.</param>
    /// <param name="address">The address.</param>
    /// <param name="focus">Where it is shown.</param>
    /// <param name="inPile">Whether it lies in a pile.</param>
    /// <returns>The entry.</returns>
    private static SearchEntry Address(NodeView node, ChainAddress address, string focus, bool inPile)
    {
        var owner = node.IsNamed ? node.Name : node.Look.Name;
        if (node.Node.Kind == GraphNodeKind.Cluster && !node.IsNamed)
        {
            owner = "I klustret " + node.Name;
        }

        var sub = Join(owner, node.IsStartPoint ? "startpunkt" : null, inPile ? InPileText : null);
        var single = node.Node.Addresses.Count == 1;
        var lead = Join(sub, CaseTexts.ChainName(address.Chain), node.IsDim ? "inte expanderad" : null);
        return new SearchEntry(
            SearchKind.Address, address.Value, sub, single ? AmountOf(node) : string.Empty, lead,
            SearchLook.Of(node.Look), focus, null)
        {
            IsMono = true,
            IsFromOtherCase = node.Labels.Count > 0 && node.Labels.All(label => label.IsOnlyFromOtherCases),
        };
    }

    /// <summary>Reads the labels, one entry per name, however many actors carry it.</summary>
    private void ReadLabels()
    {
        var carriers = new Dictionary<string, List<(Label Label, string NodeKey)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, labels) in _scene.Snapshot.Labels)
        {
            foreach (var label in labels)
            {
                var name = NameOf(label);
                if (!carriers.TryGetValue(name, out var list))
                {
                    carriers[name] = list = [];
                }

                list.Add((label, key));
            }
        }

        foreach (var (name, list) in carriers)
        {
            _entries.Add(LabelEntry(name, list));
        }
    }

    /// <summary>Makes the entry of a label: where it stands, its sources and the first actor carrying it.</summary>
    /// <param name="name">The label's name.</param>
    /// <param name="carriers">The labels by that name and the nodes they stand on.</param>
    /// <returns>The entry.</returns>
    private SearchEntry LabelEntry(string name, List<(Label Label, string NodeKey)> carriers)
    {
        var labels = carriers.Select(carrier => carrier.Label).ToList();
        var first = labels[0];
        var origins = labels.SelectMany(label => label.Origins).ToList();
        var independent = origins.Select(origin => origin.IndependenceKey).Distinct(StringComparer.Ordinal).Count();
        var sources = string.Join(", ", origins.Select(SourceOf).Distinct(StringComparer.Ordinal));
        var where = Places(labels);
        var shown = carriers.Select(carrier => _actors.GetValueOrDefault(carrier.NodeKey))
            .FirstOrDefault(actor => actor.Node is not null);
        var look = SearchLook.Of(shown.Node is { } node ? node.Look : VisualLanguage.Of(NodeSort.Unknown));
        var kindText = first.Name is null ? "Bedömning" : first.Kind.Describe();
        var sub = $"{kindText} · {independent} {(independent == 1 ? "källa" : "källor")}";
        var lead = $"Etiketten står på {where} i ärendet. Källor: {sources}.";
        var focus = shown.Node is null ? null : shown.Focus;
        return new SearchEntry(SearchKind.Label, name, sub, where, lead, look, focus, null)
        {
            IsFromOtherCase = labels.All(label => label.IsOnlyFromOtherCases),
        };
    }

    /// <summary>Reads the transactions: those on the graph's lines, and those fetched but not drawn.</summary>
    private void ReadTransactions()
    {
        var lines = new Dictionary<string, List<(GraphEdge Edge, AssetTransfer Transfer)>>(StringComparer.Ordinal);
        foreach (var edge in _scene.Snapshot.Graph.Edges)
        {
            foreach (var transfer in edge.Transfers)
            {
                if (!lines.TryGetValue(transfer.Transaction.Id, out var list))
                {
                    lines[transfer.Transaction.Id] = list = [];
                }

                list.Add((edge, transfer));
            }
        }

        var traced = _scene.Edges.Where(edge => edge.IsTraced)
            .SelectMany(edge => edge.Edge.Transfers.Select(transfer => transfer.Transaction.Id))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (id, list) in lines)
        {
            _entries.Add(OnLine(id, list, traced.Contains(id)));
        }

        foreach (var (txid, fetched) in _scene.Snapshot.Graph.Transactions)
        {
            if (!lines.ContainsKey(txid.Value))
            {
                _entries.Add(Fetched(txid.Value, fetched.Transaction.Block));
            }
        }
    }

    /// <summary>Makes the entry of a transaction on a line, shown at the line's receiver.</summary>
    /// <param name="id">The transaction's id.</param>
    /// <param name="list">The line's transfers in it.</param>
    /// <param name="isTraced">Whether it carries traced money.</param>
    /// <returns>The entry.</returns>
    private SearchEntry OnLine(string id, List<(GraphEdge Edge, AssetTransfer Transfer)> list, bool isTraced)
    {
        var (edge, transfer) = list[0];
        var asset = transfer.Amount.Asset;
        var amount = list.Where(pair => pair.Transfer.Amount.Asset == asset)
            .Aggregate(AssetAmount.Zero(asset), (sum, pair) => sum + pair.Transfer.Amount);
        var from = ActorName(edge.From);
        var to = ActorName(edge.To);
        var block = BlockText(transfer.Block);
        var focus = TransactionNode(id) ?? _actors.GetValueOrDefault(edge.To).Focus;
        var when = transfer.Block is { } at ? at.Time.ToLocalTime().ToString("d MMM HH:mm", Swedish) : null;
        var look = isTraced ? SearchLook.TracedTransaction : SearchLook.Transaction;
        return new SearchEntry(
            SearchKind.Transaction, id, $"{from} → {to} · {block}", AmountText.WithUnit(amount),
            Join($"{from} → {to}", when, block), look, focus, null)
        {
            IsMono = true,
        };
    }

    /// <summary>Makes the entry of a transaction the case has fetched that no line shows.</summary>
    /// <param name="id">The transaction's id.</param>
    /// <param name="block">Its block, or <see langword="null"/> while unconfirmed.</param>
    /// <returns>The entry.</returns>
    private SearchEntry Fetched(string id, BlockReference? block)
    {
        var sub = Join(NotInGraphText, BlockText(block));
        return new SearchEntry(
            SearchKind.Transaction, id, sub, string.Empty, sub, SearchLook.Transaction, TransactionNode(id), null)
        {
            IsMono = true,
        };
    }

    /// <summary>Gives the node a transaction is drawn as itself (a CoinJoin), if it is.</summary>
    /// <param name="id">The transaction's id.</param>
    /// <returns>The node's key, or <see langword="null"/>.</returns>
    private string? TransactionNode(string id) => _actors.Values
        .Where(actor => actor.Node.Node.Transaction?.Value == id)
        .Select(actor => actor.Focus)
        .FirstOrDefault();

    /// <summary>Gives an actor's name as the graph writes it, or its key shortened when it is not shown.</summary>
    /// <param name="key">The actor's key.</param>
    /// <returns>The name.</returns>
    private string ActorName(string key) =>
        _actors.TryGetValue(key, out var actor) ? actor.Node.Name : GraphScene.Shorten(key);

    /// <summary>Says where a label stands, for example <c>2 adresser</c> or <c>1 kluster</c>.</summary>
    /// <param name="labels">The labels by one name.</param>
    /// <returns>The words.</returns>
    private static string Places(IReadOnlyList<Label> labels)
    {
        var subjects = labels.Select(label => label.Subject).Distinct().ToList();
        var clusters = subjects.Count(subject => subject.Kind == LabelSubjectKind.Cluster);
        var addresses = subjects.Count - clusters;
        var addressText = addresses == 1 ? "1 adress" : $"{addresses} adresser";
        return (addresses, clusters) switch
        {
            (_, 0) => addressText,
            (0, _) => $"{clusters} kluster",
            _ => $"{addressText} och {clusters} kluster",
        };
    }

    /// <summary>Gives a label's name: its own, or else what kind it is.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The name.</returns>
    private static string NameOf(Label label) => label.Name ?? label.Kind.Describe();

    /// <summary>Names a label's source in a word or two.</summary>
    /// <param name="origin">The origin.</param>
    /// <returns>For example <c>egen</c>, <c>Chainabuse</c> or <c>ett annat ärende</c>.</returns>
    private static string SourceOf(LabelOrigin origin) => origin switch
    {
        { IsFromOtherCase: true } => "ett annat ärende",
        { OriginalSource: LabelSource.Own } => "egen",
        _ => origin.SourceName,
    };

    /// <summary>Gives what came in to an actor, or an empty text when nothing did.</summary>
    /// <param name="node">The actor.</param>
    /// <returns>The amount with its unit.</returns>
    private static string AmountOf(NodeView node) => node.In.IsZero ? string.Empty : AmountText.WithUnit(node.In);

    /// <summary>Gives a block's height, or that it is unconfirmed.</summary>
    /// <param name="block">The block.</param>
    /// <returns>For example <c>block 921 210</c>.</returns>
    private static string BlockText(BlockReference? block) =>
        block is null ? UnconfirmedText : "block " + block.Height.ToString("N0", Swedish);

    /// <summary>Joins the parts that are there with a middle dot.</summary>
    /// <param name="parts">The parts; empty and missing ones are left out.</param>
    /// <returns>The text.</returns>
    private static string Join(params string?[] parts) =>
        string.Join(" · ", parts.Where(part => !string.IsNullOrEmpty(part)));
}
