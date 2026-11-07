using Chainsaw.App.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The shell (sketch SH1b): the rail of views, the view shown, the command bar at the bottom and the status line. It
/// knows nothing of WPF, so that it is tested in Chainsaw.App.Tests.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    /// <summary>What the status line says while no source is connected; never a made-up source or block.</summary>
    public const string NoSourceText = "Ingen källa ansluten";

    /// <summary>What the command bar answers while no commands are built.</summary>
    public const string NoCommandsText = "Kommandon och sökning är inte byggda än";

    /// <summary>Creates the shell with every view of <see cref="ViewTable"/> and the start view shown.</summary>
    public ShellViewModel()
    {
        Top = ViewTable.In(RailGroup.Top).Select(e => new RailItemViewModel(e)).ToList();
        Work = ViewTable.In(RailGroup.Work).Select(e => new RailItemViewModel(e)).ToList();
        Bottom = ViewTable.In(RailGroup.Bottom).Select(e => new RailItemViewModel(e)).ToList();
        Show(ViewTable.Start);
    }

    /// <summary>Gets the views at the top of the rail.</summary>
    public IReadOnlyList<RailItemViewModel> Top { get; }

    /// <summary>Gets the views that follow the money.</summary>
    public IReadOnlyList<RailItemViewModel> Work { get; }

    /// <summary>Gets the views at the bottom of the rail.</summary>
    public IReadOnlyList<RailItemViewModel> Bottom { get; }

    /// <summary>Gets every view of the rail, from the top down.</summary>
    public IEnumerable<RailItemViewModel> Items => Top.Concat(Work).Concat(Bottom);

    /// <summary>Gets the view shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentEntry))]
    public partial AppView Current { get; private set; }

    /// <summary>Gets the entry of the view shown: its name and icon for the header and the empty page.</summary>
    public ViewEntry CurrentEntry => ViewTable.For(Current);

    /// <summary>Gets or sets the text in the command bar.</summary>
    [ObservableProperty]
    public partial string CommandText { get; set; } = string.Empty;

    /// <summary>Gets the short answer under the command bar, or an empty text when there is none.</summary>
    [ObservableProperty]
    public partial string CommandAnswer { get; private set; } = string.Empty;

    /// <summary>Gets the case in the title bar. No case is open until cases are built.</summary>
    public string CaseTitle => "Inget ärende öppet";

    /// <summary>Gets what the status line says about the source.</summary>
    public string SourceText => NoSourceText;

    /// <summary>Shows a view and marks its icon in the rail.</summary>
    /// <param name="view">The view to show.</param>
    [RelayCommand]
    public void Show(AppView view)
    {
        Current = view;
        foreach (var item in Items)
        {
            item.IsSelected = item.Entry.View == view;
        }
    }

    /// <summary>Runs what is written in the command bar. Nothing is built yet, so it answers honestly.</summary>
    [RelayCommand]
    public void Submit() => CommandAnswer = string.IsNullOrWhiteSpace(CommandText) ? string.Empty : NoCommandsText;

    /// <summary>Clears the command bar and its answer, as Esc does.</summary>
    [RelayCommand]
    public void ClearCommand()
    {
        CommandText = string.Empty;
        CommandAnswer = string.Empty;
    }

    /// <summary>Clears an old answer as soon as the text changes, so that it never answers something else.</summary>
    /// <param name="value">The new text.</param>
    partial void OnCommandTextChanged(string value) => CommandAnswer = string.Empty;
}
