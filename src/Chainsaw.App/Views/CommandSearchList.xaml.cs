using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The list under the command bar's field (sketch KR1a): the best hit, the groups, the start point's row and the
/// foot with the keys. The view only draws and keeps the chosen row in sight; what happens lives in
/// <see cref="CommandSearchViewModel"/>.
/// </summary>
public partial class CommandSearchList : UserControl
{
    /// <summary>Creates the list.</summary>
    public CommandSearchList()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the start point's row is clicked, so that the bar adds what is written.</summary>
    public event EventHandler? StartPointWanted;

    /// <summary>Gives the list its view model, and follows it to keep the chosen row in sight.</summary>
    /// <param name="search">The view model.</param>
    public void Attach(CommandSearchViewModel search)
    {
        ArgumentNullException.ThrowIfNull(search);
        DataContext = search;
        search.PropertyChanged += OnSearchChanged;
    }

    /// <summary>Scrolls the chosen row into sight once it is drawn.</summary>
    /// <param name="sender">The view model.</param>
    /// <param name="e">What changed.</param>
    private void OnSearchChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CommandSearchViewModel.Selected) || sender is not CommandSearchViewModel search)
        {
            return;
        }

        if (search.Selected is not { } chosen)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => FindRow(Scroller, chosen)?.BringIntoView());
    }

    /// <summary>Finds the button drawn for a row.</summary>
    /// <param name="parent">Where to look.</param>
    /// <param name="row">The row.</param>
    /// <returns>The button, or <see langword="null"/> when it is not drawn.</returns>
    private static FrameworkElement? FindRow(DependencyObject parent, SearchRowViewModel row)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button { DataContext: SearchRowViewModel shown } button && ReferenceEquals(shown, row))
            {
                return button;
            }

            if (FindRow(child, row) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>A click on the start point's row adds what is written, as Enter does.</summary>
    /// <param name="sender">The row.</param>
    /// <param name="e">The click.</param>
    private void OnAddClicked(object sender, MouseButtonEventArgs e)
    {
        StartPointWanted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}
