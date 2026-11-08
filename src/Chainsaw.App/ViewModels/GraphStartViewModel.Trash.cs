namespace Chainsaw.App.ViewModels;

/// <summary>
/// The start page's trash (sketch SO1a): a lifted case grows the bin in the lower left corner, and dropped on it the
/// case leaves the start page. It is kept on this computer like the piles, never in the case, and the file stays on
/// the disk.
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

    /// <summary>Notes where the pointer is while a case is lifted, so that the bin grows as it comes near.</summary>
    /// <param name="distanceToBin">The pointer's distance from the bin's centre, in pixels.</param>
    public void LiftNear(double distanceToBin)
    {
        if (IsLifting && _caseTrash is not null)
        {
            Bin.DragAt(distanceToBin);
        }
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

    /// <summary>Says whether a case lies in the trash, so that the ring leaves it out.</summary>
    /// <param name="caseNumber">The case.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private bool IsThrown(string caseNumber) => _caseTrash?.Contains(caseNumber) == true;

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
