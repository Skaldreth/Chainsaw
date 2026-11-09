using System.Collections.ObjectModel;
using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// One heuristic's row under <em>Varför är de en plånbok?</em> in the inspector (sketch KV1a): the analysis and its
/// version, how many of the cluster's addresses it merged and the transaction it rests on, and its menu.
/// </summary>
/// <param name="heuristic">The analysis and version.</param>
/// <param name="detail">The quiet line under: the addresses and the transaction.</param>
public sealed partial class HeuristicRowViewModel(HeuristicId heuristic, string detail) : ObservableObject
{
    /// <summary>Gets the analysis and version.</summary>
    public HeuristicId Heuristic { get; } = heuristic;

    /// <summary>Gets the row's name, for example <c>change-address v2</c>.</summary>
    public string Name { get; } = heuristic.ToString();

    /// <summary>Gets the quiet line under the name.</summary>
    public string Detail { get; } = detail;

    /// <summary>Gets or sets whether the version is drawn back: struck through, with <em>tillbakadragen</em>.</summary>
    public bool IsWithdrawn { get; init; }

    /// <summary>Gets or sets whether it counts again because a newer version is drawn back.</summary>
    public bool CountsAgain { get; init; }

    /// <summary>Gets the small tag after the name: <em>tillbakadragen</em>, <em>räknas igen</em> or nothing.</summary>
    public string Tag => (IsWithdrawn, CountsAgain) switch
    {
        (true, _) => ClusterWhyViewModel.WithdrawnTag,
        (false, true) => ClusterWhyViewModel.AgainTag,
        _ => string.Empty,
    };

    /// <summary>Gets whether the row has a tag.</summary>
    public bool HasTag => Tag.Length > 0;

    /// <summary>
    /// Gets or sets whether the version can be drawn back: another version of the analysis is still in force; the
    /// menu shows <em>Dra tillbaka …</em> only then (principle 7).
    /// </summary>
    public bool CanWithdraw { get; init; }

    /// <summary>Gets whether the row has a menu at all.</summary>
    public bool HasMenu => !IsWithdrawn;

    /// <summary>Gets the menu's guide row, for example <c>change-address i Guiden</c>.</summary>
    public string GuideText => Heuristic.Name + " i Guiden";

    /// <summary>Gets the menu's withdrawal row, for example <c>Dra tillbaka change-address v2 …</c>.</summary>
    public string WithdrawText => "Dra tillbaka " + Name + " …";

    /// <summary>Gets or sets whether the row's menu is open.</summary>
    [ObservableProperty]
    public partial bool IsMenuOpen { get; set; }
}

/// <summary>One cluster before and after in the confirmation of a withdrawal, as a bar (sketch KV1a).</summary>
/// <param name="Name">The cluster's name: its node's name, or the anchor's address shortened.</param>
/// <param name="Before">How many addresses before.</param>
/// <param name="After">How many after.</param>
/// <param name="BeforeShare">The part of the widest bar the cluster had before, from 0 to 1.</param>
/// <param name="AfterShare">The part of the widest bar the cluster has after.</param>
public sealed record ChangeBar(string Name, int Before, int After, double BeforeShare, double AfterShare)
{
    /// <summary>Gets whether the cluster grows: its change is green, a loss is yellow and dashed.</summary>
    public bool Grows => After > Before;

    /// <summary>Gets the count as the bar writes it, for example <c>14 → 11</c>.</summary>
    public string Text => string.Create(CultureInfo.InvariantCulture, $"{Before} → {After}");

    /// <summary>Gets the part of the bar that stays, from 0 to 1.</summary>
    public double KeptShare => Math.Min(BeforeShare, AfterShare);

    /// <summary>Gets the part of the bar that changes, from 0 to 1.</summary>
    public double ChangedShare => Math.Abs(AfterShare - BeforeShare);
}

/// <summary>
/// The heuristics behind a chosen cluster in the inspector, <em>Varför är de en plånbok?</em>, and drawing a version
/// back from the row's menu (sketch KV1a): the confirmation unfolds at the bottom of the inspector with the clusters
/// before and after, the graph shows what leaves and comes back, and the withdrawal is sealed by Clustering. It is
/// taken back in the history (AN1b) or with <em>Ångra</em> on the toast.
/// </summary>
public sealed partial class ClusterWhyViewModel : ObservableObject
{
    /// <summary>The tag of a version drawn back.</summary>
    public const string WithdrawnTag = "tillbakadragen";

    /// <summary>The tag of a version that counts again.</summary>
    public const string AgainTag = "räknas igen";

    /// <summary>What the confirmation says when the version cannot be drawn back after all.</summary>
    public const string CannotText = "Versionen kan inte dras tillbaka: ingen annan version av analysen står kvar.";

    /// <summary>What the confirmation says while the preview is read.</summary>
    public const string ReadingText = "Räknar ut vad som ändras …";

    /// <summary>
    /// What the preview cannot know (KV1a): it counts on the stored conclusions, so what the version that counts
    /// instead concludes about transactions it never analysed shows only after the next run.
    /// </summary>
    public const string UnknownText = "Tx som {0} aldrig har analyserat syns först efter nästa körning.";

    /// <summary>What the toast's undo says when the withdrawal was already taken back.</summary>
    public const string AlreadyText = "Tillbakadragandet var redan taget tillbaka.";

    /// <summary>The words of the toast's undo.</summary>
    public const string UndoText = "Ångra";

    /// <summary>The most clusters the confirmation lists; the rest are counted.</summary>
    public const int MostBars = 6;

    /// <summary>The open case and its session.</summary>
    private readonly CaseSession _session;

    /// <summary>The way into the modules.</summary>
    private readonly IGraphEditor _editor;

    /// <summary>The chosen node, or <see langword="null"/>.</summary>
    private NodeView? _node;

    /// <summary>The scene, for the clusters' names.</summary>
    private GraphScene? _scene;

    /// <summary>Counts the reads, so that an answer for an earlier node is dropped.</summary>
    private int _reads;

    /// <summary>Counts the previews, so that an answer for an earlier confirmation is dropped.</summary>
    private int _previews;

    /// <summary>The row whose withdrawal is being confirmed, or <see langword="null"/>.</summary>
    private HeuristicRowViewModel? _confirming;

    /// <summary>Creates the view model.</summary>
    /// <param name="session">The open case.</param>
    /// <param name="editor">The way into the modules.</param>
    public ClusterWhyViewModel(CaseSession session, IGraphEditor editor)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(editor);
        _session = session;
        _editor = editor;
    }

    /// <summary>Raised when a version was drawn back or taken back, so that the graph is read again.</summary>
    public event EventHandler? Saved;

    /// <summary>Raised when the preview changes, so that the graph draws what leaves and comes back.</summary>
    public event EventHandler? PreviewChanged;

    /// <summary>Raised with a toast to show: the withdrawal with <em>Ångra</em>, or what went wrong.</summary>
    public event EventHandler<ToastRequest>? ToastWanted;

    /// <summary>Raised when the guide for a heuristic is wanted.</summary>
    public event EventHandler<GuideRequest>? GuideWanted;

    /// <summary>Raised when the cluster is to be broken up (the form Bryt upp, U).</summary>
    public event EventHandler? SplitWanted;

    /// <summary>Gets the heuristics' rows.</summary>
    public ObservableCollection<HeuristicRowViewModel> Rows { get; } = [];

    /// <summary>Gets whether there are rows; the section is hidden otherwise (principle 7).</summary>
    [ObservableProperty]
    public partial bool HasRows { get; private set; }

    /// <summary>Gets the footer: when the latest version in force was drawn back, or empty.</summary>
    [ObservableProperty]
    public partial string Footer { get; private set; } = string.Empty;

    /// <summary>Gets whether the confirmation is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial bool IsConfirmOpen { get; private set; }

    /// <summary>Gets the confirmation's heading, for example <c>Dra tillbaka change-address v2?</c>.</summary>
    [ObservableProperty]
    public partial string ConfirmTitle { get; private set; } = string.Empty;

    /// <summary>Gets the confirmation's sentence: what stops counting and what counts instead.</summary>
    [ObservableProperty]
    public partial string ConfirmText { get; private set; } = string.Empty;

    /// <summary>Gets what the preview cannot know, said in the confirmation under the bars.</summary>
    [ObservableProperty]
    public partial string UnknownNote { get; private set; } = string.Empty;

    /// <summary>Gets the clusters before and after.</summary>
    public ObservableCollection<ChangeBar> Bars { get; } = [];

    /// <summary>Gets how many more clusters change than are listed, or empty.</summary>
    [ObservableProperty]
    public partial string MoreText { get; private set; } = string.Empty;

    /// <summary>Gets or sets the reason; it is optional (the user's decision 2026-11-07).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial string Reason { get; set; } = string.Empty;

    /// <summary>Gets what went wrong or what the confirmation waits for; empty otherwise.</summary>
    [ObservableProperty]
    public partial string Notice { get; private set; } = string.Empty;

    /// <summary>Gets whether something is being stored.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial bool IsSaving { get; private set; }

    /// <summary>Gets the preview the graph draws, or <see langword="null"/> when no confirmation is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(HasPreview))]
    public partial WithdrawPreview? Preview { get; private set; }

    /// <summary>Gets whether the preview is read, so that what it cannot know is said under the bars.</summary>
    public bool HasPreview => Preview is not null;

    /// <summary>Gets whether the withdrawal can be stored: the preview is read and the reason fits.</summary>
    public bool CanSubmit => IsConfirmOpen && Preview is not null && !IsSaving && _session.Current is not null
        && Reason.Trim().Length <= InspectorForms.MostReasonLength;

    /// <summary>
    /// Follows the chosen node: a cluster's heuristics are read off the UI thread; anything else clears.
    /// </summary>
    /// <param name="scene">The scene, or <see langword="null"/>.</param>
    /// <param name="node">The chosen node, or <see langword="null"/>.</param>
    public void Follow(GraphScene? scene, NodeView? node)
    {
        var same = node is not null && _node?.Key == node.Key;
        (_scene, _node) = (scene, node);
        if (!same)
        {
            Cancel();
        }

        _ = ReadAsync();
    }

    /// <summary>Builds the rows from a cluster and the case's withdrawals; pure, for the tests.</summary>
    /// <param name="cluster">The cluster.</param>
    /// <param name="withdrawals">Every withdrawal in the case.</param>
    /// <returns>The rows: the heuristics that merge, then the versions drawn back of the same analyses.</returns>
    public static IReadOnlyList<HeuristicRowViewModel> RowsOf(
        Cluster cluster, IReadOnlyList<VersionWithdrawal> withdrawals)
    {
        ArgumentNullException.ThrowIfNull(cluster);
        ArgumentNullException.ThrowIfNull(withdrawals);
        var members = cluster.Addresses.ToHashSet();
        var inForce = withdrawals.Where(withdrawal => withdrawal.IsInForce).ToList();
        var newest = cluster.Links.Select(link => link.Evidence.Heuristic)
            .Concat(withdrawals.Select(withdrawal => withdrawal.Heuristic))
            .GroupBy(heuristic => heuristic.Name)
            .ToDictionary(group => group.Key, group => group.Max(heuristic => heuristic.Version));
        var rows = new List<HeuristicRowViewModel>();
        foreach (var group in cluster.Links
            .Where(link => link.State == LinkState.Merged && link.Evidence.Heuristic.Name != UserMerges.Name)
            .GroupBy(link => link.Evidence.Heuristic)
            .OrderBy(group => group.Min(link => link.Evidence.Sequence)))
        {
            var count = group.SelectMany(link => link.Evidence.Addresses).Where(members.Contains).Distinct().Count();
            var tx = GraphScene.Shorten(group.First().Evidence.Txid.Value);
            var detail = string.Create(CultureInfo.InvariantCulture, $"{count} adresser · {tx}");
            rows.Add(new HeuristicRowViewModel(group.Key, detail)
            {
                CanWithdraw = HasOtherInForce(group.Key, newest[group.Key.Name], inForce),
                CountsAgain = inForce.Any(withdrawal => withdrawal.Heuristic.Name == group.Key.Name
                    && withdrawal.Heuristic.Version > group.Key.Version),
            });
        }

        var names = rows.Select(row => row.Heuristic.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var withdrawal in inForce.Where(withdrawal => names.Contains(withdrawal.Heuristic.Name)))
        {
            var when = NodeHistory.Short(withdrawal.RecordedUtc);
            rows.Insert(
                rows.FindIndex(row => row.Heuristic.Name == withdrawal.Heuristic.Name),
                new HeuristicRowViewModel(withdrawal.Heuristic, WithdrawnTag + " " + when) { IsWithdrawn = true });
        }

        return rows;
    }

    /// <summary>
    /// Says whether another version of an analysis would still count if this one were drawn back: one of the versions
    /// from 1 to the newest the case knows that is not drawn back. Without one the menu does not offer it, since
    /// Clustering refuses to leave an analysis without a version.
    /// </summary>
    /// <param name="heuristic">The version.</param>
    /// <param name="newest">The newest version of the analysis the case knows.</param>
    /// <param name="inForce">The withdrawals in force.</param>
    /// <returns><see langword="true"/> when another version is left.</returns>
    private static bool HasOtherInForce(HeuristicId heuristic, int newest, IReadOnlyList<VersionWithdrawal> inForce) =>
        Enumerable.Range(1, Math.Max(newest, heuristic.Version)).Any(version => version != heuristic.Version
            && inForce.All(withdrawal => withdrawal.Heuristic != new HeuristicId(heuristic.Name, version)));

    /// <summary>
    /// Builds the bars of the confirmation from a preview; pure, for the tests. The widest cluster is the whole bar.
    /// </summary>
    /// <param name="preview">The preview.</param>
    /// <param name="nameOf">Gives a cluster's name from its anchor.</param>
    /// <returns>The bars, at most <see cref="MostBars"/>.</returns>
    public static IReadOnlyList<ChangeBar> BarsOf(WithdrawPreview preview, Func<BitcoinAddress, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(nameOf);
        var widest = Math.Max(1, preview.Changes.Select(change => Math.Max(change.Before.Count, change.After.Count))
            .DefaultIfEmpty(1).Max());
        return
        [
            .. preview.Changes.Take(MostBars).Select(change => new ChangeBar(
                nameOf(change.Anchor),
                change.Before.Count,
                change.After.Count,
                (double)change.Before.Count / widest,
                (double)change.After.Count / widest)),
        ];
    }

    /// <summary>Opens a row's menu, or closes it when it is open; one menu at a time.</summary>
    /// <param name="row">The row.</param>
    [RelayCommand]
    public void ToggleMenu(HeuristicRowViewModel? row)
    {
        if (row is null || !row.HasMenu)
        {
            return;
        }

        var open = !row.IsMenuOpen;
        CloseMenus();
        row.IsMenuOpen = open;
    }

    /// <summary>Opens the guide at the row's heuristic (F1).</summary>
    /// <param name="row">The row.</param>
    [RelayCommand]
    public void AskGuide(HeuristicRowViewModel? row)
    {
        CloseMenus();
        if (row is not null && GuideTopics.ForHeuristic(row.Heuristic.Name) is { } topic)
        {
            GuideWanted?.Invoke(this, new GuideRequest(topic, null, null, row.Name) { IsChoice = true });
        }
    }

    /// <summary>Opens the form Bryt upp at the node (U).</summary>
    [RelayCommand]
    public void AskSplit()
    {
        CloseMenus();
        SplitWanted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens the confirmation of drawing the row's version back, and reads what it would change off the UI thread.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>A task that completes when the preview is read or could not be.</returns>
    [RelayCommand]
    public async Task AskWithdrawAsync(HeuristicRowViewModel? row)
    {
        CloseMenus();
        if (row is not { CanWithdraw: true } || _session.Current is not { } openCase)
        {
            return;
        }

        var read = ++_previews;
        _confirming = row;
        Preview = null;
        Bars.Clear();
        MoreText = string.Empty;
        Reason = string.Empty;
        ConfirmTitle = "Dra tillbaka " + row.Name + "?";
        ConfirmText = string.Empty;
        Notice = ReadingText;
        IsConfirmOpen = true;
        try
        {
            var preview = await _editor.PreviewWithdrawAsync(openCase, row.Heuristic, CancellationToken.None);
            if (read != _previews || _confirming != row)
            {
                return;
            }

            if (preview is null)
            {
                Notice = CannotText;
                return;
            }

            Notice = string.Empty;
            ConfirmText = SentenceOf(preview);
            UnknownNote = string.Format(CultureInfo.InvariantCulture, UnknownText, preview.CountsInstead);
            foreach (var bar in BarsOf(preview, NameOf))
            {
                Bars.Add(bar);
            }

            var more = preview.Changes.Count - Bars.Count;
            MoreText = more > 0 ? string.Create(CultureInfo.InvariantCulture, $"+{more} kluster till") : string.Empty;
            Preview = preview;
            PreviewChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (read == _previews)
            {
                Notice = InspectorFormsViewModel.Failure(exception);
            }
        }
    }

    /// <summary>Closes the confirmation without storing anything (Esc), and the graph forgets the preview.</summary>
    [RelayCommand]
    public void Cancel()
    {
        CloseMenus();
        _confirming = null;
        _previews++;
        IsConfirmOpen = false;
        Notice = string.Empty;
        Bars.Clear();
        if (Preview is not null)
        {
            Preview = null;
            PreviewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Draws the version back (Enter), sealed by Clustering; then the graph is read again and a toast offers
    /// <em>Ångra</em>, which takes the withdrawal back.
    /// </summary>
    /// <returns>A task that completes when it is stored or could not be.</returns>
    [RelayCommand]
    public async Task SubmitAsync()
    {
        if (!CanSubmit || _session.Current is not { } openCase || Preview is not { } preview)
        {
            return;
        }

        IsSaving = true;
        try
        {
            var reason = Reason.Trim().Length == 0 ? null : Reason.Trim();
            var withdrawal = await _editor.WithdrawAsync(openCase, preview.Heuristic, reason);
            Cancel();
            Saved?.Invoke(this, EventArgs.Empty);
            var changed = preview.Changes.Count.ToString(CultureInfo.InvariantCulture);
            ToastWanted?.Invoke(this, new ToastRequest(
                ToastKind.Success,
                withdrawal.Heuristic + " tillbakadragen",
                changed + " kluster ändrades · journalfört · fler kan följa vid nästa körning",
                UndoText,
                () => _ = TakeBackAsync(openCase, withdrawal)));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Notice = InspectorFormsViewModel.Failure(exception);
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>Takes a withdrawal back from the toast's <em>Ångra</em>, sealed as a record of its own.</summary>
    /// <param name="openCase">The case it was drawn back in.</param>
    /// <param name="withdrawal">The withdrawal.</param>
    /// <returns>A task that completes when it is stored or could not be.</returns>
    public async Task TakeBackAsync(ICase openCase, VersionWithdrawal withdrawal)
    {
        ArgumentNullException.ThrowIfNull(openCase);
        ArgumentNullException.ThrowIfNull(withdrawal);
        if (_session.Current != openCase)
        {
            return;
        }

        try
        {
            var outcome = await _editor.TakeBackWithdrawalAsync(openCase, withdrawal.Sequence, null);
            if (!outcome.Done)
            {
                ToastWanted?.Invoke(this, new ToastRequest(ToastKind.Information, AlreadyText));
            }

            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ToastWanted?.Invoke(this, new ToastRequest(ToastKind.Error, InspectorFormsViewModel.Failure(exception)));
        }
    }

    /// <summary>
    /// Gives the change the graph draws at a node: what leaves its cluster and what comes back; none without a
    /// preview.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The change of the node's cluster, or <see langword="null"/>.</returns>
    public ClusterChange? ChangeOf(NodeView node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (Preview is not { } preview)
        {
            return null;
        }

        var addresses = node.Node.GraphAddresses.ToHashSet();
        return preview.Changes.FirstOrDefault(change => change.Before.Any(addresses.Contains)
            || change.After.Any(addresses.Contains));
    }

    /// <summary>Writes the confirmation's sentence.</summary>
    /// <param name="preview">The preview.</param>
    /// <returns>The sentence.</returns>
    private static string SentenceOf(WithdrawPreview preview) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Slutsatserna slutar räknas och v{preview.CountsInstead.Version} räknas i stället. Inget tas bort: ")
        + string.Create(
            CultureInfo.InvariantCulture,
            $"v{preview.Heuristic.Version}:s slutsatser står kvar överstrukna i Historik.");

    /// <summary>Gives a cluster's name: the name of its node in the graph, or its anchor's address shortened.</summary>
    /// <param name="anchor">The cluster's anchor.</param>
    /// <returns>The name.</returns>
    private string NameOf(BitcoinAddress anchor) =>
        _scene?.Nodes.FirstOrDefault(node => node.Node.GraphAddresses.Contains(anchor))?.Name
        ?? GraphScene.Shorten(anchor.Value);

    /// <summary>Closes every row's menu.</summary>
    private void CloseMenus()
    {
        foreach (var row in Rows)
        {
            row.IsMenuOpen = false;
        }
    }

    /// <summary>Reads the chosen cluster and the withdrawals off the UI thread, and fills the rows.</summary>
    /// <returns>A task that completes when the rows are shown or could not be read.</returns>
    private async Task ReadAsync()
    {
        var read = ++_reads;
        if (_session.Current is not { } openCase
            || _node is not { Node: { Kind: GraphNodeKind.Cluster, GraphAddresses: [var first, ..] } })
        {
            Fill([]);
            return;
        }

        try
        {
            var cluster = await _editor.ReadClusterAsync(openCase, first, CancellationToken.None);
            var withdrawals = await _editor.ReadWithdrawalsAsync(openCase, CancellationToken.None);
            if (read == _reads)
            {
                Fill(RowsOf(cluster, withdrawals));
                var latest = withdrawals.Where(withdrawal => withdrawal.IsInForce
                        && Rows.Any(row => row.IsWithdrawn && row.Heuristic == withdrawal.Heuristic))
                    .MaxBy(withdrawal => withdrawal.RecordedUtc);
                Footer = latest is null ? string.Empty : "Tillbakadragen " + NodeHistory.Short(latest.RecordedUtc);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The case may have closed; the section stays hidden rather than showing something half read.
            if (read == _reads)
            {
                Fill([]);
            }
        }
    }

    /// <summary>Shows rows in place of the ones shown.</summary>
    /// <param name="rows">The rows.</param>
    private void Fill(IReadOnlyList<HeuristicRowViewModel> rows)
    {
        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }

        HasRows = Rows.Count > 0;
        if (!HasRows)
        {
            Footer = string.Empty;
        }
    }
}
