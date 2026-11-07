using Chainsaw.App.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// A submenu of the pill (sketch PN1): its rows from <see cref="SubmenuTable"/>, whether it is open, and the keyboard.
/// The arrows move the mark among the rows that can be chosen, Enter chooses the marked row, the letters in the key
/// column choose directly, and Esc closes. No WPF, so that it is tested in Chainsaw.App.Tests.
/// </summary>
public sealed partial class SubmenuViewModel : ObservableObject
{
    /// <summary>Creates the submenu of a button, closed.</summary>
    /// <param name="rows">The rows, in their order.</param>
    public SubmenuViewModel(IReadOnlyList<SubmenuRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        Rows = rows.Select(row => new SubmenuRowViewModel(row)).ToList();
        Height = SubmenuShape.MenuHeight(rows);
        Outline = SubmenuShape.Outline(Height);
        Update(hasCase: false);
    }

    /// <summary>Raised when a row is chosen, after the menu has closed.</summary>
    public event EventHandler<SubmenuAction>? Chosen;

    /// <summary>
    /// Raised when the menu closes. The argument says whether the keyboard goes back to the button: it does after Esc,
    /// a click outside or a choice, and not when the keyboard has already gone elsewhere.
    /// </summary>
    public event EventHandler<bool>? Closed;

    /// <summary>Gets the rows, lines included.</summary>
    public IReadOnlyList<SubmenuRowViewModel> Rows { get; }

    /// <summary>Gets the menu's height, from its rows.</summary>
    public double Height { get; }

    /// <summary>Gets the outline of the tab and the menu as one shape, as path data.</summary>
    public string Outline { get; }

    /// <summary>Gets whether the menu is open.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    /// <summary>Gets the marked row, or <see langword="null"/> when none is.</summary>
    public SubmenuRowViewModel? Marked => Rows.FirstOrDefault(row => row.IsMarked);

    /// <summary>Opens the menu, or closes it when it is open, as a click or Enter on the button does.</summary>
    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    /// <summary>Opens the menu with the first row that can be chosen marked, so that the arrows start there.</summary>
    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        Mark(Choosable().FirstOrDefault());
        IsOpen = true;
    }

    /// <summary>Closes the menu and gives the keyboard back to the button (Esc, a click outside).</summary>
    public void Close() => Shut(returnFocus: true);

    /// <summary>
    /// Closes the menu without taking the keyboard back, when it has already gone elsewhere: another view, the command
    /// bar, the lock.
    /// </summary>
    public void Dismiss() => Shut(returnFocus: false);

    /// <summary>Moves the mark to the next row that can be chosen, round to the first after the last (↓).</summary>
    public void MoveNext() => Step(+1);

    /// <summary>Moves the mark to the row before that can be chosen, round to the last before the first (↑).</summary>
    public void MovePrevious() => Step(-1);

    /// <summary>Marks the first row that can be chosen (Home).</summary>
    public void MoveFirst() => Mark(Choosable().FirstOrDefault());

    /// <summary>Marks the last row that can be chosen (End).</summary>
    public void MoveLast() => Mark(Choosable().LastOrDefault());

    /// <summary>
    /// Marks a row, as the pointer does when it rests on it; a row that cannot be chosen is not marked.
    /// </summary>
    /// <param name="row">The row.</param>
    public void Point(SubmenuRowViewModel row)
    {
        if (row.IsEnabled)
        {
            Mark(row);
        }
    }

    /// <summary>Chooses the marked row (Enter); nothing happens when none is.</summary>
    public void ChooseMarked()
    {
        if (Marked is { } row)
        {
            Choose(row);
        }
    }

    /// <summary>
    /// Chooses a row (a click): the menu closes and gives the keyboard back to the button, and then the row's action
    /// is raised, so that an action that moves the keyboard elsewhere has the last word. A row that cannot be chosen
    /// does nothing, and the menu stays open.
    /// </summary>
    /// <param name="row">The row.</param>
    public void Choose(SubmenuRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!IsOpen || !row.IsEnabled)
        {
            return;
        }

        Close();
        Chosen?.Invoke(this, row.Row.Action);
    }

    /// <summary>
    /// Chooses the row whose key is a letter, as the letters in the key column do while the menu is open.
    /// </summary>
    /// <param name="letter">The letter typed.</param>
    /// <returns>
    /// <see langword="true"/> when a row has the letter, chosen or not (a toned-down row swallows its letter, so
    /// that it never reaches anything behind the menu); <see langword="false"/> when none has it.
    /// </returns>
    public bool ChooseLetter(char letter)
    {
        var wanted = char.ToUpperInvariant(letter);
        var row = Rows.FirstOrDefault(item => item.Row.Letter == wanted);
        if (row is null)
        {
            return false;
        }

        Choose(row);
        return true;
    }

    /// <summary>Says which rows can be chosen, whenever a case is opened or closed.</summary>
    /// <param name="hasCase">Whether a case is open.</param>
    public void Update(bool hasCase)
    {
        foreach (var row in Rows)
        {
            row.Update(hasCase);
        }

        if (IsOpen && Marked is null)
        {
            Mark(Choosable().FirstOrDefault());
        }
    }

    /// <summary>Gets the rows that can be chosen, in their order.</summary>
    /// <returns>The rows.</returns>
    private IEnumerable<SubmenuRowViewModel> Choosable() => Rows.Where(row => row.IsEnabled);

    /// <summary>Marks one row and no other.</summary>
    /// <param name="chosen">The row, or <see langword="null"/> to mark none.</param>
    private void Mark(SubmenuRowViewModel? chosen)
    {
        foreach (var row in Rows)
        {
            row.IsMarked = ReferenceEquals(row, chosen);
        }
    }

    /// <summary>Moves the mark a step among the rows that can be chosen, round at the ends.</summary>
    /// <param name="step">+1 down, -1 up.</param>
    private void Step(int step)
    {
        var rows = Choosable().ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var at = Marked is { } marked ? rows.IndexOf(marked) : -1;
        if (at < 0)
        {
            // Nothing marked yet: down starts at the top, up at the bottom.
            at = step > 0 ? -1 : 0;
        }

        Mark(rows[(at + step + rows.Count) % rows.Count]);
    }

    /// <summary>Closes the menu, if it is open, and says whether the keyboard goes back to the button.</summary>
    /// <param name="returnFocus">Whether the keyboard goes back to the button.</param>
    private void Shut(bool returnFocus)
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        Mark(null);
        Closed?.Invoke(this, returnFocus);
    }
}
