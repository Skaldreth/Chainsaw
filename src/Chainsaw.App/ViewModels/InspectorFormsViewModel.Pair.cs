using System.Collections.ObjectModel;
using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// One link on the way in the form Bryt upp (sketch BU1a), with the scissors when it is the one to cut.
/// </summary>
/// <param name="step">The step.</param>
public sealed partial class PathLinkViewModel(PathStep step) : ObservableObject
{
    /// <summary>Gets the step.</summary>
    public PathStep Step { get; } = step;

    /// <summary>Gets the address the link starts at, shortened, as the chain's dot names it.</summary>
    public string From { get; } = SubjectText.Fit(step.From.Value, 22);

    /// <summary>Gets the analysis and version, for example <c>change-address v2</c>.</summary>
    public string Heuristic { get; } = step.Link.Evidence.Heuristic.ToString();

    /// <summary>Gets the transaction it rests on, shortened.</summary>
    public string Tx { get; } = GraphScene.Shorten(step.Link.Evidence.Txid.Value);

    /// <summary>Gets whether the link is dashed: a sign that is weaker than common inputs.</summary>
    public bool IsDashed { get; } = PairPath.Strength(step.Link.Evidence.Heuristic) < 3;

    /// <summary>Gets or sets whether the form suggests it as the weakest (<em>svagast</em>).</summary>
    public bool IsWeakest { get; init; }

    /// <summary>Gets or sets whether the scissors are on it.</summary>
    [ObservableProperty]
    public partial bool IsCut { get; set; }
}

/// <summary>
/// Breaking up a pair in the form Bryt upp (sketch BU1a): with one address picked, Ctrl+click on the one that is not
/// the same owner draws the way between them as a chain, with the scissors on the weakest link; a click on another
/// link moves them. When the two are not in the same cluster, the form says so, with only <em>Stäng</em>.
/// </summary>
public sealed partial class InspectorFormsViewModel
{
    /// <summary>What the form says when the two are not in the same cluster.</summary>
    public const string ApartText =
        "De är inte i samma kluster, så det finns ingen länk att bryta. Ingen heuristik har slagit ihop dem.";

    /// <summary>What the form says when the cut does not part the two.</summary>
    public const string StillJoinedText = "Andra länkar håller ihop dem. Välj en annan länk.";

    /// <summary>Counts the ways read, so that an answer for an earlier pair is dropped.</summary>
    private int _pairReads;

    /// <summary>The first address of the pair: the one that stays.</summary>
    private BitcoinAddress? _pairFrom;

    /// <summary>The second address of the pair: the one that is not the same owner.</summary>
    private BitcoinAddress? _pairTo;

    /// <summary>Gets the links on the way between the pair, in order from the first.</summary>
    public ObservableCollection<PathLinkViewModel> PathLinks { get; } = [];

    /// <summary>Gets whether the form shows a pair's way in place of the cluster's addresses.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(SubmitText))]
    public partial bool IsPairMode { get; private set; }

    /// <summary>Gets whether the pair is in two clusters: the warning with only <em>Stäng</em>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CancelText))]
    public partial bool IsApart { get; private set; }

    /// <summary>Gets the closing button's words: <em>Stäng</em> for the warning, where nothing can be stored.</summary>
    public string CancelText => IsApart ? "Stäng" : "Avbryt";

    /// <summary>Gets the last address of the way, shortened, under the chain's last link.</summary>
    [ObservableProperty]
    public partial string PairLast { get; private set; } = string.Empty;

    /// <summary>Gets the first address of the pair, shortened, for the warning.</summary>
    [ObservableProperty]
    public partial string PairFromText { get; private set; } = string.Empty;

    /// <summary>Gets the second address of the pair, shortened, for the warning.</summary>
    [ObservableProperty]
    public partial string PairToText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets what happens when the chosen link is cut, for example which addresses become a wallet of their own.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial string PairResult { get; private set; } = string.Empty;

    /// <summary>Gets the addresses that leave when the chosen link is cut; empty when it does not part the pair.</summary>
    public IReadOnlyList<BitcoinAddress> PairLeaving { get; private set; } = [];

    /// <summary>
    /// Takes a Ctrl+click on a member's row: with another address picked, it becomes the pair and the way between them
    /// is read; with none, it is picked like a plain click.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <returns>A task that completes when the way is read or could not be.</returns>
    [RelayCommand]
    public async Task PickPairAsync(MemberChoice? member)
    {
        if (member is null)
        {
            return;
        }

        if (Members.LastOrDefault(row => row.IsPicked && row != member) is not { } first)
        {
            ToggleMember(member);
            return;
        }

        foreach (var row in Members)
        {
            row.IsPicked = row == first;
        }

        UpdatePicked();
        await ShowPairAsync(first.Address, member.Address);
    }

    /// <summary>
    /// Shows the way between two addresses in the open form Bryt upp, read off the UI thread; two addresses in
    /// different clusters give the warning.
    /// </summary>
    /// <param name="from">The address that stays.</param>
    /// <param name="to">The address that is not the same owner.</param>
    /// <returns>A task that completes when the way is read or could not be.</returns>
    public async Task ShowPairAsync(BitcoinAddress from, BitcoinAddress to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (Form != InspectorForm.Split || _session.Current is not { } openCase)
        {
            return;
        }

        var read = ++_pairReads;
        var opened = _opened;
        (_pairFrom, _pairTo) = (from, to);
        PairFromText = SubjectText.Fit(from.Value, 22);
        PairToText = SubjectText.Fit(to.Value, 22);
        try
        {
            var links = await _editor.ExplainConnectionAsync(openCase, from, to, CancellationToken.None);
            if (read != _pairReads || opened != _opened)
            {
                return;
            }

            ShowPath(links is null ? null : PairPath.Steps(from, to, links));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (read == _pairReads && opened == _opened)
            {
                Error = Failure(exception);
            }
        }
    }

    /// <summary>Moves the scissors to another link (a click on it).</summary>
    /// <param name="link">The link.</param>
    [RelayCommand]
    public void ChooseCut(PathLinkViewModel? link)
    {
        if (link is null || !PathLinks.Contains(link))
        {
            return;
        }

        foreach (var row in PathLinks)
        {
            row.IsCut = row == link;
        }

        UpdatePairResult();
    }

    /// <summary>Leaves the pair's way and goes back to the cluster's addresses.</summary>
    [RelayCommand]
    public void LeavePair()
    {
        _pairReads++;
        (_pairFrom, _pairTo) = (null, null);
        PathLinks.Clear();
        IsPairMode = false;
        IsApart = false;
        PairResult = string.Empty;
        PairLeaving = [];
        PairLast = string.Empty;
    }

    /// <summary>Shows a way, or the warning when there is none.</summary>
    /// <param name="steps">The steps, or <see langword="null"/> when the pair is in two clusters.</param>
    private void ShowPath(IReadOnlyList<PathStep>? steps)
    {
        PathLinks.Clear();
        IsApart = steps is null;
        IsPairMode = true;
        if (steps is null)
        {
            PairResult = string.Empty;
            PairLeaving = [];
            return;
        }

        var weakest = PairPath.Weakest(steps);
        for (var index = 0; index < steps.Count; index++)
        {
            var weak = index == weakest;
            PathLinks.Add(new PathLinkViewModel(steps[index]) { IsWeakest = weak, IsCut = weak });
        }

        PairLast = steps.Count > 0 ? SubjectText.Fit(steps[^1].To.Value, 22) : string.Empty;
        UpdatePairResult();
    }

    /// <summary>Works out what cutting the chosen link breaks out, and says it.</summary>
    private void UpdatePairResult()
    {
        var cut = PathLinks.FirstOrDefault(link => link.IsCut);
        if (_cluster is not { } cluster || cut is null || _pairFrom is null || _pairTo is null)
        {
            PairLeaving = [];
            PairResult = string.Empty;
            return;
        }

        PairLeaving = PairPath.Leaving(cluster, _pairFrom, _pairTo, cut.Step.Link);
        PairResult = PairLeaving.Count == 0 ? StillJoinedText : ResultText(cluster, PairLeaving);
        OnPropertyChanged(nameof(CanSubmit));
    }

    /// <summary>
    /// Writes what a cut does, for example <c>K1 behåller 4 · bc1q…t4vw och bc1q…2c7u blir en egen plånbok</c>.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    /// <param name="leaving">The addresses that leave.</param>
    /// <returns>The sentence.</returns>
    private string ResultText(Cluster cluster, IReadOnlyList<BitcoinAddress> leaving)
    {
        var name = _node?.Name ?? "Klustret";
        var stays = (cluster.Members.Count - leaving.Count).ToString(CultureInfo.InvariantCulture);
        var shown = string.Join(" och ", leaving.Take(2).Select(address => GraphScene.Shorten(address.Value)));
        var more = leaving.Count > 2
            ? string.Create(CultureInfo.InvariantCulture, $" och {leaving.Count - 2} till")
            : string.Empty;
        var own = leaving.Count == 1 ? "blir en egen nod" : "blir en egen plånbok";
        return $"{name} behåller {stays} · {shown}{more} {own}";
    }

    /// <summary>
    /// Gives the splits that cut the chosen link: every link that crosses between what leaves and what stays.
    /// </summary>
    /// <returns>The splits; empty while the cut does not part the pair.</returns>
    private IReadOnlyList<SplitRequest> PairRequests() =>
        _cluster is { } cluster && PairLeaving.Count > 0 && HasReason(Reason)
            ? InspectorForms.BreakOut(cluster, PairLeaving, Reason)
            : [];
}
