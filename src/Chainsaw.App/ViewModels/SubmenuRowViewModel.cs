using Chainsaw.App.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// A row of an open submenu: whether it can be chosen now, whether it is marked, and its honest tooltip.
/// </summary>
public sealed partial class SubmenuRowViewModel : ObservableObject
{
    /// <summary>Creates the row, toned down until the menu says what it may do.</summary>
    /// <param name="row">The row's entry in <see cref="SubmenuTable"/>.</param>
    public SubmenuRowViewModel(SubmenuRow row) => Row = row;

    /// <summary>Gets the row's entry: what it says and does, and its key.</summary>
    public SubmenuRow Row { get; }

    /// <summary>Gets whether the row can be chosen now; a row that cannot is toned down.</summary>
    [ObservableProperty]
    public partial bool IsEnabled { get; private set; }

    /// <summary>Gets whether the row is marked, by the keyboard or the pointer; Enter chooses the marked row.</summary>
    [ObservableProperty]
    public partial bool IsMarked { get; set; }

    /// <summary>
    /// Gets why a toned-down row cannot be chosen, shown on hover; <see langword="null"/> for a row that can, which
    /// needs no explanation (principle 7).
    /// </summary>
    [ObservableProperty]
    public partial string? ToolTip { get; private set; }

    /// <summary>Says whether the row can be chosen, from what it needs and whether a case is open.</summary>
    /// <param name="hasCase">Whether a case is open.</param>
    internal void Update(bool hasCase)
    {
        (IsEnabled, ToolTip) = Row.Need switch
        {
            SubmenuNeed.OpenCase when !hasCase => (false, SubmenuTable.NeedsCaseText),
            _ => (!Row.IsLine, null),
        };
        if (!IsEnabled)
        {
            IsMarked = false;
        }
    }
}
