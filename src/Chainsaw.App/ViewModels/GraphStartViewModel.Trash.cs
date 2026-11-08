using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The start page's trash (sketch SO1a): a lifted case or pile grows the bin in the lower left corner, and dropped on
/// it the case, or the whole pile, leaves the start page; so do cards dragged out of an open pile onto the bin, or
/// chosen in it and Delete. It is kept on this computer like the piles, never in the case, and the file stays on the
/// disk. A pile and a case taken from a pile come back where they lay.
/// </summary>
public sealed partial class GraphStartViewModel
{
    /// <summary>The cases thrown, shared with the trash's view; none until the graph connects it.</summary>
    private CaseTrash? _caseTrash;

    /// <summary>The cases that lay in the trash at the last look, so that one put back is shown on the ring again.</summary>
    private HashSet<string> _inTrash = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised when the bin is clicked, so that the trash's view opens.</summary>
    public event EventHandler? TrashWanted;

    /// <summary>Raised when a case is thrown, with its number, so that the window can say it.</summary>
    public event EventHandler<string>? CaseThrown;

    /// <summary>Raised when cards of an open pile are thrown, with their numbers, so that the window can say it.</summary>
    public event EventHandler<IReadOnlyList<string>>? CardsThrown;

    /// <summary>Raised when a whole pile is thrown, with the pile as it stood, so that the window can say it.</summary>
    public event EventHandler<CasePileItem>? PileThrown;

    /// <summary>Gets the bin in the lower left corner, which grows while a case is lifted.</summary>
    public TrashBinViewModel Bin { get; } = new();

    /// <summary>Connects the cases thrown in the trash.</summary>
    /// <param name="trash">The cases thrown.</param>
    public void UseCaseTrash(CaseTrash trash)
    {
        ArgumentNullException.ThrowIfNull(trash);
        _caseTrash = trash;
        trash.Changed += (_, _) => OnTrashChanged();
        OnTrashChanged();
    }

    /// <summary>Opens the trash's view (a click on the bin).</summary>
    public void OpenTrash() => TrashWanted?.Invoke(this, EventArgs.Empty);

    /// <summary>Notes where the pointer is while a case or a pile is lifted, so that the bin grows as it comes near.</summary>
    /// <param name="distanceToBin">The pointer's distance from the bin's centre, in pixels.</param>
    public void LiftNear(double distanceToBin)
    {
        if (IsLifting && _caseTrash is not null)
        {
            Bin.DragAt(distanceToBin);
        }
    }

    /// <summary>Notes where the pointer is while cards are dragged out of the open pile, so that the bin grows.</summary>
    /// <param name="distanceToBin">The pointer's distance from the bin's centre, in pixels.</param>
    public void CardsNear(double distanceToBin)
    {
        if (IsPileOpen && _caseTrash is not null)
        {
            Bin.DragAt(distanceToBin);
        }
    }

    /// <summary>Puts the lifted pile down: on the bin the whole pile is thrown.</summary>
    /// <param name="pileId">The lifted pile.</param>
    /// <returns><see langword="true"/> when it went in the trash, so that nothing else takes the drop.</returns>
    public bool DropPileOnBin(string pileId)
    {
        if (!Bin.EndDrag() || _caseTrash is null || PileOf(pileId) is not { } pile)
        {
            return false;
        }

        var item = ItemOf(pile);
        var cases = ExistingCases(pile).Select(file => file.CaseNumber).ToList();
        if (_caseTrash.ThrowPile(pile.Id, pile.Name, cases).Count == 0)
        {
            return false;
        }

        if (OpenPile?.Id == pile.Id)
        {
            ClosePile();
        }

        PileThrown?.Invoke(this, item);
        return true;
    }

    /// <summary>Lets go of cards dragged out of the open pile: on the bin they are thrown, each on its own.</summary>
    /// <param name="caseNumbers">The cards' cases.</param>
    /// <returns><see langword="true"/> when they went in the trash, so that they are not taken out of the pile.</returns>
    public bool DropCardsOnBin(IReadOnlyCollection<string> caseNumbers)
    {
        ArgumentNullException.ThrowIfNull(caseNumbers);
        return Bin.EndDrag() && ThrowCards(caseNumbers);
    }

    /// <summary>Throws the cards chosen in the open pile (Delete), each on its own; they stay in the pile, hidden.</summary>
    /// <returns><see langword="true"/> when any was thrown.</returns>
    public bool ThrowChosenCards() =>
        ThrowCards([.. OpenCards.Where(card => card.IsSelected).Select(card => card.CaseNumber)]);

    /// <summary>Puts a thrown pile back on the start page, whole (the toast's <em>Ångra</em>).</summary>
    /// <param name="pileId">The pile.</param>
    public void RestorePile(string pileId) => _caseTrash?.RestorePile(pileId);

    /// <summary>Puts cards thrown from a pile back in it (the toast's <em>Ångra</em>).</summary>
    /// <param name="caseNumbers">The cards' cases.</param>
    public void RestoreCases(IReadOnlyCollection<string> caseNumbers)
    {
        ArgumentNullException.ThrowIfNull(caseNumbers);
        foreach (var caseNumber in caseNumbers)
        {
            _caseTrash?.Restore(caseNumber);
        }
    }

    /// <summary>
    /// Throws cards of the open pile: they stay in the pile, hidden, and the fan folds; a pile left with none leaves the
    /// start page with them.
    /// </summary>
    /// <param name="caseNumbers">The cards' cases.</param>
    /// <returns><see langword="true"/> when any was thrown.</returns>
    private bool ThrowCards(IReadOnlyCollection<string> caseNumbers)
    {
        if (_caseTrash is null || !IsPileOpen || caseNumbers.Count == 0)
        {
            return false;
        }

        var thrown = _caseTrash.ThrowAll(caseNumbers);
        if (thrown.Count == 0)
        {
            return false;
        }

        ClosePile();
        CardsThrown?.Invoke(this, thrown);
        return true;
    }

    /// <summary>Puts the lifted case down: on the bin it is thrown.</summary>
    /// <param name="caseNumber">The lifted case.</param>
    /// <returns><see langword="true"/> when it went in the trash, so that nothing else takes the drop.</returns>
    public bool DropOnBin(string caseNumber)
    {
        if (!Bin.EndDrag() || _caseTrash is null || !_caseTrash.Throw(caseNumber))
        {
            return false;
        }

        _released.Remove(caseNumber);
        CaseThrown?.Invoke(this, caseNumber);
        return true;
    }

    /// <summary>Puts a thrown case back (the toast's <em>Ångra</em>).</summary>
    /// <param name="caseNumber">The case.</param>
    public void RestoreCase(string caseNumber) => _caseTrash?.Restore(caseNumber);

    /// <summary>
    /// Says whether a case file is hidden by the trash, so that the ring leaves it out; a new case with a thrown number
    /// is not (<see cref="CaseTrash.Hides"/>).
    /// </summary>
    /// <param name="file">The case file.</param>
    /// <returns><see langword="true"/> when it is hidden.</returns>
    private bool IsThrown(CaseFileInfo file) => _caseTrash?.Hides(file) == true;

    /// <summary>
    /// Takes out of the trash the cases written after they were thrown, once the folder has been read, so that the
    /// trash's view does not list a case that stands on the ring.
    /// </summary>
    /// <param name="files">The case files.</param>
    private void ForgetRewrittenThrows(IEnumerable<CaseFileInfo> files) => _caseTrash?.ForgetRewritten(files);

    /// <summary>Shows the ring again, with a case put back among the loose ones whatever its age.</summary>
    private void OnTrashChanged()
    {
        var now = (_caseTrash?.Cases ?? []).Select(each => each.CaseNumber)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _released.UnionWith(_inTrash.Where(number => !now.Contains(number)));
        _inTrash = now;
        Bin.HasThrows = now.Count > 0;
        ShowRing();
    }
}
