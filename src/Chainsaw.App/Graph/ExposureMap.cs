using System.Globalization;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>What the colour of a service means in the view Exponering (sketches EP1b and GR7a).</summary>
public enum ExposureGroup
{
    /// <summary>The accent: a victim.</summary>
    Victim,

    /// <summary>Blue: a cash-out.</summary>
    CashOut,

    /// <summary>Yellow: where money is spent.</summary>
    Spent,

    /// <summary>Red: risk and the end of the trail.</summary>
    Risk,

    /// <summary>Purple: another chain.</summary>
    OtherChain,

    /// <summary>Grey: what reached no service.</summary>
    Unknown,
}

/// <summary>A cluster or an address in the graph whose exposure can be read.</summary>
/// <param name="Node">The actor as the graph draws it.</param>
/// <param name="Assets">The assets it paid or got along the graph's lines, the native coin first.</param>
public sealed record ExposureSubject(NodeView Node, IReadOnlyList<Asset> Assets)
{
    /// <summary>Gets the node's key.</summary>
    public string Key => Node.Key;

    /// <summary>Gets the name the graph gives it.</summary>
    public string Name => Node.Name;

    /// <summary>
    /// Gets the second line (sketch EP1b): how many addresses and the first one shortened, for example
    /// <c>14 adresser · bc1q…h3dd</c>.
    /// </summary>
    public string Line
    {
        get
        {
            var address = Node.Node.Addresses.Count > 0 ? GraphScene.Shorten(Node.Node.Addresses[0].Value) : string.Empty;
            var line = string.Join(" · ", new[] { Node.Count, address }.Where(part => part.Length > 0 && part != Name));
            return line.Length > 0 ? line : Node.Look.Name;
        }
    }
}

/// <summary>
/// Reads the graph shown for the view Exponering (sketch EP1b), without WPF: which actors can be its subject, which are
/// services where money stops, and what each service's colour means. The analysis itself is Tracing's
/// <see cref="IExposureAnalysis"/>; what is a service is the graph's attribution (labels and sorts), a judgement.
/// </summary>
public static class ExposureMap
{
    /// <summary>The groups in the order the money matters for the case (sketch EP1b), <em>Okänt</em> last.</summary>
    public static IReadOnlyList<ExposureGroup> Order { get; } =
    [
        ExposureGroup.Victim, ExposureGroup.CashOut, ExposureGroup.Spent, ExposureGroup.Risk,
        ExposureGroup.OtherChain, ExposureGroup.Unknown,
    ];

    /// <summary>Gives a group's name, as the side panel writes it.</summary>
    /// <param name="group">The group.</param>
    /// <returns>The name, in Swedish.</returns>
    public static string NameOf(ExposureGroup group) => group switch
    {
        ExposureGroup.Victim => "Offrens pengar",
        ExposureGroup.CashOut => "Uttag",
        ExposureGroup.Spent => "Spenderas",
        ExposureGroup.Risk => "Risk och slut",
        ExposureGroup.OtherChain => "Annan kedja",
        _ => "Okänt",
    };

    /// <summary>Gives a group's colour.</summary>
    /// <param name="group">The group.</param>
    /// <returns>The ink.</returns>
    public static GraphInk InkOf(ExposureGroup group) => group switch
    {
        ExposureGroup.Victim => GraphInk.Accent,
        ExposureGroup.CashOut => GraphInk.CashOut,
        ExposureGroup.Spent => GraphInk.Spent,
        ExposureGroup.Risk => GraphInk.Risk,
        ExposureGroup.OtherChain => GraphInk.OtherChain,
        _ => GraphInk.Unknown,
    };

    /// <summary>Gives the group a sort's colour puts it in; the greys are <em>Okänt</em>.</summary>
    /// <param name="look">The sort's look.</param>
    /// <returns>The group.</returns>
    public static ExposureGroup GroupOf(SortLook look)
    {
        ArgumentNullException.ThrowIfNull(look);
        return look.Ink switch
        {
            GraphInk.Accent => ExposureGroup.Victim,
            GraphInk.CashOut => ExposureGroup.CashOut,
            GraphInk.Spent => ExposureGroup.Spent,
            GraphInk.Risk or GraphInk.Sanctioned => ExposureGroup.Risk,
            GraphInk.OtherChain => ExposureGroup.OtherChain,
            _ => ExposureGroup.Unknown,
        };
    }

    /// <summary>Gives the actors drawn, also those lying in piles, by key.</summary>
    /// <param name="scene">The graph.</param>
    /// <returns>The actors.</returns>
    public static IReadOnlyDictionary<string, NodeView> Actors(GraphScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var nodes = new Dictionary<string, NodeView>(StringComparer.Ordinal);
        foreach (var node in scene.Nodes.Concat(scene.Piles.Values.SelectMany(pile => pile.Cards.Select(c => c.Node))))
        {
            if (node.Node.Kind is not (GraphNodeKind.Pile or GraphNodeKind.HeldBack))
            {
                nodes.TryAdd(node.Key, node);
            }
        }

        return nodes;
    }

    /// <summary>
    /// Gives the actors whose exposure can be read: the clusters and addresses drawn that paid or got something along
    /// the graph's lines; the start points first, then the clusters with the most addresses, then the rest. The assets
    /// are gathered from the lines in one sweep.
    /// </summary>
    /// <param name="scene">The graph.</param>
    /// <returns>The subjects.</returns>
    public static IReadOnlyList<ExposureSubject> Subjects(GraphScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var assets = new Dictionary<string, HashSet<Asset>>(StringComparer.Ordinal);
        foreach (var edge in scene.Snapshot.Graph.Edges.Where(e => e.Kind == GraphEdgeKind.Transfer && e.From != e.To))
        {
            foreach (var key in new[] { edge.From, edge.To })
            {
                if (!assets.TryGetValue(key, out var set))
                {
                    set = [];
                    assets[key] = set;
                }

                set.Add(edge.Amount.Asset);
            }
        }

        return
        [
            .. Actors(scene).Values
                .Where(node => node.Node.Kind is GraphNodeKind.Address or GraphNodeKind.Cluster)
                .Where(node => assets.ContainsKey(node.Key))
                .Select(node => new ExposureSubject(node, Ordered(assets[node.Key])))
                .OrderByDescending(subject => subject.Node.IsStartPoint)
                .ThenByDescending(subject => subject.Node.Node.ClusterSize)
                .ThenBy(subject => subject.Name, StringComparer.CurrentCulture),
        ];
    }

    /// <summary>
    /// Gives how every node of the graph is drawn: as the scene draws it, or for a node the scene does not draw (one in
    /// the trash) by its kind and labels, as the scene would. The trash is only a filter, so what it hides is still the
    /// sort it is (CLAUDE.md, the trash).
    /// </summary>
    /// <param name="scene">The graph.</param>
    /// <returns>The looks by node.</returns>
    public static IReadOnlyDictionary<string, SortLook> Looks(GraphScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var drawn = Actors(scene);
        var looks = new Dictionary<string, SortLook>(StringComparer.Ordinal);
        foreach (var node in scene.Snapshot.Graph.Nodes)
        {
            looks[node.Key] = drawn.TryGetValue(node.Key, out var view)
                ? view.Look
                : VisualLanguage.Of(GraphScene.SortOf(node, scene.Snapshot.Labels.GetValueOrDefault(node.Key) ?? []));
        }

        return looks;
    }

    /// <summary>
    /// Gives the services of the graph, where money stops: every node whose sort means something (a victim, a cash-out,
    /// spending, risk, another chain), from the graph and its labels, whatever lies in the trash.
    /// </summary>
    /// <param name="scene">The graph.</param>
    /// <returns>The services' keys.</returns>
    public static IReadOnlySet<string> Services(GraphScene scene) =>
        Looks(scene).Where(pair => GroupOf(pair.Value) != ExposureGroup.Unknown).Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Makes the analysis's request for a subject.</summary>
    /// <param name="scene">The graph.</param>
    /// <param name="subject">The subject.</param>
    /// <param name="asset">The asset followed.</param>
    /// <param name="direction">Paid out or got.</param>
    /// <param name="depth">How many hops.</param>
    /// <returns>The request, with the graph's own tracing method.</returns>
    public static ExposureRequest Request(
        GraphScene scene, string subject, Asset asset, ExposureDirection direction, int depth)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var services = Services(scene).Where(key => key != subject).ToHashSet(StringComparer.Ordinal);
        return new ExposureRequest(scene.Snapshot.Graph, subject, asset, direction, depth, scene.Snapshot.Method, services)
        {
            Pricing = scene.Snapshot.SwapPricing,
        };
    }

    /// <summary>Writes a share as the view does: one decimal and a space before the sign.</summary>
    /// <param name="share">The share, from 0 to 1.</param>
    /// <returns>For example <c>33,3 %</c>.</returns>
    public static string Percent(double share) =>
        (Math.Round(share * 1000) / 10).ToString("0.0", CultureInfo.GetCultureInfo("sv-SE")) + " %";

    /// <summary>Orders assets: the native coin first, then by symbol.</summary>
    /// <param name="assets">The assets.</param>
    /// <returns>The assets in order.</returns>
    private static List<Asset> Ordered(IEnumerable<Asset> assets) =>
        [.. assets.OrderBy(asset => asset.Kind != AssetKind.Native).ThenBy(asset => asset.Symbol, StringComparer.Ordinal)];
}
