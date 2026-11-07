using Chainsaw.App.Settings;
using Chainsaw.Modules.Cases.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>Something that stands on the ring around Nytt ärende: a loose case or a pile (sketch GR8a).</summary>
public interface IStartRingItem
{
    /// <summary>Gets what tells it apart on the ring: the case number or the pile's id.</summary>
    string Key { get; }
}

/// <summary>A pile on the start page, drawn as the graph's collection node, a deck of cards (sketches GR8a and EX2b).</summary>
/// <param name="Id">The pile's id.</param>
/// <param name="Name">The name the user wrote; empty while it has none.</param>
/// <param name="Count">How many cases lie in it.</param>
/// <param name="ChangedText">When its latest case was saved, for example <c>senast ändrad i dag 09:12</c>.</param>
/// <param name="IsNaming">Whether its name is being written, in the pile itself.</param>
public sealed record CasePileItem(string Id, string Name, int Count, string ChangedText, bool IsNaming)
    : IStartRingItem
{
    /// <inheritdoc />
    public string Key => Id;

    /// <summary>Gets the name to show: the one written, or <see cref="GraphStartViewModel.UnnamedPileText"/>.</summary>
    public string ShownName => Name.Length > 0 ? Name : GraphStartViewModel.UnnamedPileText;
}

/// <summary>A case in an open pile, as a card in the ring around it (sketch GR8a).</summary>
/// <param name="caseNumber">The case number.</param>
/// <param name="changedText">When it was last saved, in words.</param>
public sealed partial class PileCardViewModel(string caseNumber, string changedText) : ObservableObject
{
    /// <summary>Gets the case number.</summary>
    public string CaseNumber { get; } = caseNumber;

    /// <summary>Gets when it was last saved, in words.</summary>
    public string ChangedText { get; } = changedText;

    /// <summary>Gets the case as a recent case, so that a click opens it the way the ring does.</summary>
    public RecentCaseItem Case => new(CaseNumber, ChangedText);

    /// <summary>Gets or sets whether the card is chosen (Shift-click), to be dragged out with the others chosen.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// The start page's piles (sketch GR8a): a case dropped on another makes a pile, named at once in the pile itself; more
/// cases are dropped on a pile; a click fans it out over a dimmed surface, where cards are chosen with Shift-click and
/// dragged out to lie loose on the start page again. The piles are kept on this computer (<see cref="ICasePileStore"/>),
/// never in the cases, and a case that is no longer in the folder leaves its pile.
/// </summary>
public sealed partial class GraphStartViewModel
{
    /// <summary>What a pile without a name is called.</summary>
    public const string UnnamedPileText = "Namnlös";

    /// <summary>What a new pile's name says while nothing is written.</summary>
    public const string PileNamePlaceholder = "Namnge högen";

    /// <summary>The longest name a pile takes, so that it fits on the deck.</summary>
    public const int MaximumPileName = 40;

    /// <summary>What the open pile says while no card is chosen.</summary>
    public const string FanHintText = "Dra ut ett kort · Shift-klick väljer flera";

    /// <summary>The start of the line under a pile's name, before when its latest case was saved.</summary>
    private const string PileChangedPrefix = "senast ";

    /// <summary>The piles, in the order they were made.</summary>
    private List<CasePile> _piles = [];

    /// <summary>
    /// The cases taken out of a pile while Chainsaw runs, which stand loose on the ring even when they are older than
    /// the latest cases, so that a card dragged out never vanishes.
    /// </summary>
    private readonly HashSet<string> _released = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where the piles are kept; none until <see cref="UsePilesAsync"/>, and then they live only in memory.</summary>
    private ICasePileStore? _pileStore;

    /// <summary>Whether the piles were changed before the saved ones were read, so that the reading does not undo it.</summary>
    private bool _pilesTouched;

    /// <summary>Gets what stands on the ring around Nytt ärende: the shown loose cases and every pile, newest first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFirstTime), nameof(HasRecent))]
    public partial IReadOnlyList<IStartRingItem> Ring { get; private set; } = [];

    /// <summary>Gets the pile whose name is being written, or <see langword="null"/>.</summary>
    [ObservableProperty]
    public partial string? NamingPileId { get; private set; }

    /// <summary>Gets the pile that is fanned out, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPileOpen))]
    public partial CasePileItem? OpenPile { get; private set; }

    /// <summary>Gets whether a pile is fanned out.</summary>
    public bool IsPileOpen => OpenPile is not null;

    /// <summary>Gets the open pile's cases as cards, in the pile's order.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<PileCardViewModel> OpenCards { get; private set; } = [];

    /// <summary>Gets what the open pile says under its cards: how to drag out, or how many are chosen.</summary>
    [ObservableProperty]
    public partial string FanHint { get; private set; } = FanHintText;

    /// <summary>
    /// Gets the case that is lifted (dragged) on the ring, or <see langword="null"/>. While a case is lifted, the trash
    /// grows in the lower left corner to be dropped on (sketch SO1a, not built yet: see <c>docs/TODO.md</c>).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLifting))]
    public partial string? LiftedCase { get; private set; }

    /// <summary>Gets whether a case is lifted on the ring.</summary>
    public bool IsLifting => LiftedCase is not null;

    /// <summary>Gets the latest write of the piles, so that the window and the tests can wait for it.</summary>
    public Task PilesSaved { get; private set; } = Task.CompletedTask;

    /// <summary>Gets the piles as they are now, in the order they were made.</summary>
    public IReadOnlyList<CasePile> Piles => _piles;

    /// <summary>Reads the saved piles off the UI thread and keeps them there from now on.</summary>
    /// <param name="store">Where the piles are kept.</param>
    /// <returns>A task that completes when they are read and shown.</returns>
    public async Task UsePilesAsync(ICasePileStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _pileStore = store;
        var saved = await Task.Run(store.Load);
        _ui.Post(() =>
        {
            if (!_pilesTouched)
            {
                _piles = WithoutDoubles(saved);
            }

            ShowRing();
        });
    }

    /// <summary>Lifts a loose case on the ring, while it is dragged.</summary>
    /// <param name="caseNumber">The case.</param>
    public void Lift(string caseNumber) => LiftedCase = caseNumber;

    /// <summary>Puts the lifted case down, wherever it was dropped.</summary>
    public void Drop() => LiftedCase = null;

    /// <summary>Whether a case can be dropped on another to make a pile: both loose, and not the same.</summary>
    /// <param name="dragged">The case dragged.</param>
    /// <param name="target">The case it is dropped on.</param>
    /// <returns><see langword="true"/> when they make a pile.</returns>
    public bool CanStack(string dragged, string target) =>
        !SameCase(dragged, target) && IsLoose(dragged) && IsLoose(target) && Exists(dragged) && Exists(target);

    /// <summary>Makes a pile of a case dropped on another, and starts writing its name in it.</summary>
    /// <param name="dragged">The case dragged; it lies on top.</param>
    /// <param name="target">The case it was dropped on.</param>
    /// <returns><see langword="true"/> when the pile was made.</returns>
    public bool Stack(string dragged, string target)
    {
        if (!CanStack(dragged, target))
        {
            return false;
        }

        var pile = new CasePile(Guid.NewGuid().ToString("N"), string.Empty, [dragged, target]);
        ChangePiles([.. _piles, pile]);
        NamingPileId = pile.Id;
        ShowRing();
        return true;
    }

    /// <summary>Whether a case can be dropped on a pile: loose, and the pile there.</summary>
    /// <param name="caseNumber">The case dragged.</param>
    /// <param name="pileId">The pile.</param>
    /// <returns><see langword="true"/> when it can be put in the pile.</returns>
    public bool CanAddToPile(string caseNumber, string pileId) =>
        IsLoose(caseNumber) && Exists(caseNumber) && PileOf(pileId) is not null;

    /// <summary>Puts a loose case on top of a pile.</summary>
    /// <param name="caseNumber">The case dragged.</param>
    /// <param name="pileId">The pile it was dropped on.</param>
    /// <returns><see langword="true"/> when it was put there.</returns>
    public bool AddToPile(string caseNumber, string pileId)
    {
        if (!CanAddToPile(caseNumber, pileId))
        {
            return false;
        }

        ChangePiles([.. _piles.Select(pile => pile.Id == pileId
            ? pile with { CaseNumbers = [caseNumber, .. pile.CaseNumbers] }
            : pile)]);
        _released.Remove(caseNumber);
        ShowRing();
        return true;
    }

    /// <summary>Starts writing a pile's name in the pile.</summary>
    /// <param name="pileId">The pile.</param>
    public void BeginNaming(string pileId)
    {
        if (PileOf(pileId) is null)
        {
            return;
        }

        NamingPileId = pileId;
        ShowRing();
        ShowOpenPile();
    }

    /// <summary>Gives a pile the name written in it, or keeps it without one, and stops writing.</summary>
    /// <param name="pileId">The pile.</param>
    /// <param name="name">What was written; empty or <see langword="null"/> leaves the pile without a name.</param>
    public void NamePile(string pileId, string? name)
    {
        var written = Shorten((name ?? string.Empty).Trim());
        if (PileOf(pileId) is { } pile && pile.Name != written)
        {
            ChangePiles([.. _piles.Select(each => each.Id == pileId ? each with { Name = written } : each)]);
        }

        if (NamingPileId == pileId)
        {
            NamingPileId = null;
        }

        ShowRing();
        ShowOpenPile();
    }

    /// <summary>Fans a pile out: its cases become cards in a ring around it, none chosen.</summary>
    /// <param name="pileId">The pile.</param>
    public void OpenPileOf(string pileId)
    {
        if (PileOf(pileId) is null)
        {
            return;
        }

        OpenPile = ItemOf(PileOf(pileId)!);
        OpenCards = [.. ExistingCases(PileOf(pileId)!).Select(file => new PileCardViewModel(file.CaseNumber, Changed(file)))];
        ShowFanHint();
    }

    /// <summary>Folds the open pile again, with nothing taken out.</summary>
    public void ClosePile()
    {
        OpenPile = null;
        OpenCards = [];
        ShowFanHint();
    }

    /// <summary>Chooses a card in the open pile, or lets it go (Shift-click).</summary>
    /// <param name="caseNumber">The card's case.</param>
    public void ToggleCard(string caseNumber)
    {
        if (CardOf(caseNumber) is { } card)
        {
            card.IsSelected = !card.IsSelected;
            ShowFanHint();
        }
    }

    /// <summary>
    /// Gives the cards that go with a card when it is dragged: every chosen card when it is one of them, else itself.
    /// </summary>
    /// <param name="grabbed">The card's case.</param>
    /// <returns>The cases to drag, the grabbed one first.</returns>
    public IReadOnlyList<string> CardsToDrag(string grabbed)
    {
        if (CardOf(grabbed) is not { IsSelected: true })
        {
            return [grabbed];
        }

        return [grabbed, .. OpenCards.Where(card => card.IsSelected && !SameCase(card.CaseNumber, grabbed))
            .Select(card => card.CaseNumber)];
    }

    /// <summary>
    /// Takes cards out of the open pile onto the start page, where they lie loose, and folds the pile. A pile left
    /// empty is gone.
    /// </summary>
    /// <param name="caseNumbers">The cards' cases.</param>
    public void TakeOut(IReadOnlyCollection<string> caseNumbers)
    {
        ArgumentNullException.ThrowIfNull(caseNumbers);
        if (OpenPile is not { } open || caseNumbers.Count == 0)
        {
            ClosePile();
            return;
        }

        var taken = new HashSet<string>(caseNumbers, StringComparer.OrdinalIgnoreCase);
        ChangePiles([.. _piles
            .Select(pile => pile.Id == open.Id
                ? pile with { CaseNumbers = [.. pile.CaseNumbers.Where(number => !taken.Contains(number))] }
                : pile)
            .Where(pile => pile.CaseNumbers.Count > 0)]);
        _released.UnionWith(taken);
        ClosePile();
        ShowRing();
    }

    /// <summary>
    /// Puts the loose cases and the piles on the ring, newest first: the latest <see cref="RecentCount"/> loose
    /// cases, those taken out of a pile while Chainsaw runs, and every pile with a case in the folder. Once the folder
    /// has been read, a case that is no longer there leaves its pile.
    /// </summary>
    private void ShowRing()
    {
        var files = FilesByNumber();
        if (_workflow.IsListed)
        {
            ForgetMissing(files);
        }

        var piled = new HashSet<string>(_piles.SelectMany(pile => pile.CaseNumbers), StringComparer.OrdinalIgnoreCase);
        var loose = files.Values
            .Where(file => !piled.Contains(file.CaseNumber))
            .OrderByDescending(file => file.LastWrittenUtc)
            .Where((file, index) => index < RecentCount || _released.Contains(file.CaseNumber))
            .ToList();
        Recent = [.. loose.Select(file => new RecentCaseItem(file.CaseNumber, Changed(file)))];
        var looseOnRing = loose.Select(file => (Latest: file.LastWrittenUtc, Item: (IStartRingItem)Recent
            .First(item => SameCase(item.CaseNumber, file.CaseNumber))));
        var pilesOnRing = _piles
            .Select(pile => (Pile: pile, Cases: ExistingCases(pile)))
            .Where(each => each.Cases.Count > 0)
            .Select(each => (Latest: each.Cases.Max(file => file.LastWrittenUtc), Item: (IStartRingItem)ItemOf(each.Pile)));
        Ring = [.. looseOnRing.Concat(pilesOnRing).OrderByDescending(each => each.Latest).Select(each => each.Item)];
    }

    /// <summary>Says how to drag out of the open pile, or how many cards are chosen.</summary>
    private void ShowFanHint()
    {
        var chosen = OpenCards.Count(card => card.IsSelected);
        FanHint = chosen == 0 ? FanHintText : $"{chosen} valda · dra ut dem tillsammans";
    }

    /// <summary>Shows the open pile anew after its name changed, keeping the cards and what is chosen.</summary>
    private void ShowOpenPile()
    {
        if (OpenPile is { } open && PileOf(open.Id) is { } pile)
        {
            OpenPile = ItemOf(pile);
        }
    }

    /// <summary>Takes out of every pile the cases that are no longer in the folder, and drops the piles left empty.</summary>
    /// <param name="files">The cases in the folder.</param>
    private void ForgetMissing(IReadOnlyDictionary<string, CaseFileInfo> files)
    {
        var kept = _piles
            .Select(pile => pile with { CaseNumbers = [.. pile.CaseNumbers.Where(files.ContainsKey)] })
            .Where(pile => pile.CaseNumbers.Count > 0)
            .ToList();
        if (kept.Count != _piles.Count || kept.Zip(_piles).Any(pair => pair.First.CaseNumbers.Count != pair.Second.CaseNumbers.Count))
        {
            ChangePiles(kept);
        }
    }

    /// <summary>Replaces the piles and writes them off the UI thread, one write after the other.</summary>
    /// <param name="piles">The piles.</param>
    private void ChangePiles(List<CasePile> piles)
    {
        _piles = piles;
        _pilesTouched = true;
        if (_pileStore is not { } store)
        {
            return;
        }

        var before = PilesSaved;
        IReadOnlyList<CasePile> snapshot = [.. piles];

        // A pile that cannot be written still stands until Chainsaw closes; nothing about a case is lost with it.
        PilesSaved = Task.Run(async () =>
        {
            await before;
            store.Save(snapshot);
        });
    }

    /// <summary>Makes a pile's item for the ring, from the cases of it that are in the folder.</summary>
    /// <param name="pile">The pile.</param>
    /// <returns>The item.</returns>
    private CasePileItem ItemOf(CasePile pile)
    {
        var cases = ExistingCases(pile);
        var changed = cases.Count == 0 ? string.Empty : PileChangedPrefix + Changed(cases.MaxBy(file => file.LastWrittenUtc)!);
        return new CasePileItem(pile.Id, pile.Name, cases.Count, changed, NamingPileId == pile.Id);
    }

    /// <summary>Gives the cases of a pile that are in the folder, in the pile's order.</summary>
    /// <param name="pile">The pile.</param>
    /// <returns>The cases.</returns>
    private List<CaseFileInfo> ExistingCases(CasePile pile)
    {
        var files = FilesByNumber();
        return [.. pile.CaseNumbers.Where(files.ContainsKey).Select(number => files[number])];
    }

    /// <summary>Gives the case files by number, the latest written when a number is listed twice.</summary>
    /// <returns>The files.</returns>
    private Dictionary<string, CaseFileInfo> FilesByNumber() => _workflow.Files
        .GroupBy(file => file.CaseNumber, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.MaxBy(file => file.LastWrittenUtc)!, StringComparer.OrdinalIgnoreCase);

    /// <summary>Says when a case was last saved, in words, now.</summary>
    /// <param name="file">The case's file.</param>
    /// <returns>The text.</returns>
    private string Changed(CaseFileInfo file) => ChangedText(file.LastWrittenUtc, _clock.UtcNow, _zone);

    /// <summary>Finds a pile.</summary>
    /// <param name="pileId">The pile's id.</param>
    /// <returns>The pile, or <see langword="null"/>.</returns>
    private CasePile? PileOf(string pileId) => _piles.Find(pile => pile.Id == pileId);

    /// <summary>Finds a card in the open pile.</summary>
    /// <param name="caseNumber">The card's case.</param>
    /// <returns>The card, or <see langword="null"/>.</returns>
    private PileCardViewModel? CardOf(string caseNumber) =>
        OpenCards.FirstOrDefault(card => SameCase(card.CaseNumber, caseNumber));

    /// <summary>Whether a case lies in no pile.</summary>
    /// <param name="caseNumber">The case.</param>
    /// <returns><see langword="true"/> when it is loose.</returns>
    private bool IsLoose(string caseNumber) =>
        !_piles.Any(pile => pile.CaseNumbers.Any(number => SameCase(number, caseNumber)));

    /// <summary>Whether a case is in the folder as last listed.</summary>
    /// <param name="caseNumber">The case.</param>
    /// <returns><see langword="true"/> when it is there.</returns>
    private bool Exists(string caseNumber) => _workflow.Files.Any(file => SameCase(file.CaseNumber, caseNumber));

    /// <summary>Whether two case numbers name the same case; the folder does not tell case apart.</summary>
    /// <param name="first">One number.</param>
    /// <param name="second">The other.</param>
    /// <returns><see langword="true"/> when they are the same.</returns>
    private static bool SameCase(string first, string second) =>
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    /// <summary>Cuts a name to <see cref="MaximumPileName"/> characters.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The name, at most that long.</returns>
    private static string Shorten(string name) => name.Length <= MaximumPileName ? name : name[..MaximumPileName].TrimEnd();

    /// <summary>
    /// Keeps a case in the first pile it is in, when a hand-edited file has it in two, so that a case lies in one
    /// pile only; a pile left empty is dropped.
    /// </summary>
    /// <param name="piles">The piles as read.</param>
    /// <returns>The piles.</returns>
    private static List<CasePile> WithoutDoubles(IReadOnlyList<CasePile> piles)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return [.. piles
            .Select(pile => pile with { CaseNumbers = [.. pile.CaseNumbers.Where(seen.Add)] })
            .Where(pile => pile.CaseNumbers.Count > 0)];
    }
}
