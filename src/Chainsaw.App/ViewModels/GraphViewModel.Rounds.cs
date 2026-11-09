using Chainsaw.App.Graph;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The graph's part in round four's surfaces: drawing a version back from the inspector (sketch KV1a), and the quiet
/// mark of a conclusion that is held back (sketch HB1a).
/// </summary>
public sealed partial class GraphViewModel
{
    /// <summary>What hovering the mark of a held back conclusion says first.</summary>
    public const string HeldBackTitle = "En slutsats hålls tillbaka";

    /// <summary>The conclusions the latest run held back that wait for the user.</summary>
    private IReadOnlyList<HeldBackConclusion> _heldBack = [];

    /// <summary>Counts the readings of what is held back, so that an older answer is dropped.</summary>
    private int _heldReads;

    /// <summary>The analysis whose likely services can become labels (sketch TJ1b).</summary>
    public const string ServiceAnalysis = "service-behaviour";

    /// <summary>The prefix of a finding's key that names a hot wallet swept into (<c>sweep:</c>).</summary>
    public const string SweepKeyPrefix = "sweep:";

    /// <summary>
    /// Gets the check that found the chosen node likely to be a service, so that it can become a label (sketch
    /// TJ1b), or <see langword="null"/>: none, or the node already has a label of the user's.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasServiceCheck), nameof(ServiceCheckTip))]
    public partial StoredCheck? ServiceCheck { get; private set; }

    /// <summary>Gets whether <em>Som etikett …</em> shows in the inspector (principle 7).</summary>
    public bool HasServiceCheck => ServiceCheck is not null;

    /// <summary>Gets the tooltip of <em>Som etikett …</em>: the check and what it found.</summary>
    public string ServiceCheckTip => ServiceCheck is { } check
        ? $"{check.Finding.Title} enligt {check.Analysis} · sätt som etikett vid noden"
        : string.Empty;

    /// <summary>Opens the label form at the node from the check that found a likely service (sketch TJ1b).</summary>
    [RelayCommand]
    public void LabelService()
    {
        if (ServiceCheck is { } check)
        {
            Forms.OpenLabelFromCheck(check);
        }
    }

    /// <summary>
    /// Finds the check that found a node likely to be a service: a sweep into one of its addresses as the hot wallet,
    /// when the node has no label of the user's yet; pure, for the tests.
    /// </summary>
    /// <param name="checks">The case's stored checks.</param>
    /// <param name="node">The node, or <see langword="null"/>.</param>
    /// <returns>The check, or <see langword="null"/>.</returns>
    public static StoredCheck? ServiceCheckOf(IReadOnlyList<StoredCheck> checks, NodeView? node)
    {
        ArgumentNullException.ThrowIfNull(checks);
        if (node is null || node.Labels.Count > 0 || node.Node.GraphAddresses.Count == 0)
        {
            return null;
        }

        var addresses = node.Node.GraphAddresses.ToHashSet();
        return checks.LastOrDefault(check => check.Analysis.Name == ServiceAnalysis
            && check.Finding.Key.StartsWith(SweepKeyPrefix, StringComparison.Ordinal)
            && check.Finding.Addresses is [var hot, ..]
            && addresses.Contains(hot));
    }

    /// <summary>
    /// Says whether a node is toned down while a withdrawal's confirmation is open: every node but those whose
    /// cluster changes (sketch KV1a).
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns><see langword="true"/> when it is toned down.</returns>
    public bool IsDimmedByWithdrawal(NodeView node) =>
        ClusterWhy.Preview is not null && ClusterWhy.ChangeOf(node) is null;

    /// <summary>
    /// Gives the conclusion held back on a node's addresses that waits for the user, so that the node gets its quiet
    /// mark (sketch HB1a), or <see langword="null"/>.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The conclusion, or <see langword="null"/>.</returns>
    public HeldBackConclusion? HeldBackOf(NodeView node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (_heldBack.Count == 0 || Scene is not { } scene)
        {
            return null;
        }

        return _heldBack.FirstOrDefault(held => HeldBackMatch.Touches(scene, node, held.Addresses, held.Txid));
    }

    /// <summary>
    /// Writes what hovering a held back mark tells: what is held back, what it was built on, and that the old
    /// conclusion stands until the user chooses (sketch HB1a).
    /// </summary>
    /// <param name="held">The conclusion.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> HeldBackLines(HeldBackConclusion held)
    {
        ArgumentNullException.ThrowIfNull(held);
        var heuristics = string.Join(
            ", ",
            held.Evidence.Select(evidence => evidence.Heuristic)
                .Concat(held.Findings.Select(found => found.Heuristic))
                .Select(heuristic => heuristic.ToString())
                .Distinct());
        var missing = string.Join(", ", held.Missing.Select(txid => GraphScene.Shorten(txid.Value)));
        return
        [
            HeldBackTitle,
            heuristics + " drog slutsatser på en tx som inte finns i senaste körningen.",
            "Byggde på " + missing,
            "Den gamla slutsatsen står kvar tills du väljer. Klicka för Historik.",
        ];
    }

    /// <summary>Chooses a node with a held back mark and opens its history, where the decision is made (HB1a).</summary>
    /// <param name="nodeKey">The node.</param>
    public void OpenHeldBack(string nodeKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(nodeKey);
        Choose(nodeKey, null);
        History.ShowHistory();
    }

    /// <summary>
    /// Wires the heuristics' rows in the inspector: the graph is read again after a withdrawal, redrawn while its
    /// preview shows what leaves and comes back, and the menu's other rows open the guide and the form Bryt upp.
    /// </summary>
    private void WireClusterWhy()
    {
        ClusterWhy.Saved += (_, _) => _ = RefreshAsync();
        ClusterWhy.PreviewChanged += (_, _) => Redraw?.Invoke(this, EventArgs.Empty);
        ClusterWhy.ToastWanted += (_, toast) => ToastWanted?.Invoke(this, toast);
        ClusterWhy.GuideWanted += (_, request) => GuideWanted?.Invoke(this, request);
        ClusterWhy.SplitWanted += (_, _) => Forms.Open(InspectorForm.Split);
        Forms.PreviewChanged += (_, _) => Redraw?.Invoke(this, EventArgs.Empty);
        Checks.Changed += (_, _) => UpdateObfuscation();
        Forms.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InspectorFormsViewModel.Form) && Forms.Form == InspectorForm.Split
                && PickedPair() is { } pair)
            {
                _ = Forms.ShowPairAsync(pair.From, pair.To);
            }
        };
    }

    /// <summary>
    /// Gives the pair picked in the graph with Ctrl+click when the form Bryt upp opens (sketch BU1a): two nodes, the
    /// first address of each, so that the form shows the way between them or that they are in two clusters.
    /// </summary>
    /// <returns>The pair, or <see langword="null"/> unless exactly two nodes with addresses are picked.</returns>
    private (BitcoinAddress From, BitcoinAddress To)? PickedPair()
    {
        if (Checks.Picked is not [var first, var second] || Scene is not { } scene)
        {
            return null;
        }

        return (scene.Node(first)?.Node.GraphAddresses, scene.Node(second)?.Node.GraphAddresses) switch
        {
            ([var from, ..], [var to, ..]) when from != to => (from, to),
            _ => null,
        };
    }

    /// <summary>
    /// Reads off the UI thread what the latest run held back, after the graph was read, and redraws when it changed.
    /// </summary>
    /// <returns>A task that completes when it is read or could not be.</returns>
    private async Task ReadHeldBackAsync()
    {
        var read = ++_heldReads;
        if (_session.Current is not { } openCase || Scene is null)
        {
            _heldBack = [];
            return;
        }

        try
        {
            var held = await _editor.ReadHeldBackAsync(openCase, CancellationToken.None);
            if (read == _heldReads)
            {
                _heldBack = [.. held.Where(conclusion => conclusion.IsWaiting)];
                Redraw?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The case may have closed; without an answer there is no mark, which is never a false alarm.
            if (read == _heldReads)
            {
                _heldBack = [];
            }
        }
    }
}
