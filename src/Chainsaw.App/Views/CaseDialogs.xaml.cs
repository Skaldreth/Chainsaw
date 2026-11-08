using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Chainsaw.App.Controls;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The case's dialogs of sketch ÄR1b. The view only lays them out, wires the keys and hands the password fields to the
/// view models as <see cref="ISecretEntry"/>, so that a password is never bound to a property; what happens lives in
/// <see cref="CaseDialogsViewModel"/>.
/// </summary>
public partial class CaseDialogs : UserControl
{
    /// <summary>The dialogs' view model, once it is given.</summary>
    private CaseDialogsViewModel? _dialogs;

    /// <summary>Creates the dialogs, all closed.</summary>
    public CaseDialogs()
    {
        InitializeComponent();
    }

    /// <summary>Gives the dialogs their view model, and puts the caret where each dialog starts.</summary>
    /// <param name="dialogs">The view model.</param>
    public void Attach(CaseDialogsViewModel dialogs)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        _dialogs = dialogs;
        DataContext = dialogs;
        dialogs.Password.PropertyChanged += OnPasswordChanged;
        dialogs.Properties.PropertyChanged += OnPropertiesChanged;
        dialogs.Branch.PropertyChanged += OnBranchChanged;
    }

    /// <summary>Puts the caret in the password field when the dialog opens, and on Till grafen when done.</summary>
    /// <param name="sender">The view model.</param>
    /// <param name="e">The property that changed.</param>
    private void OnPasswordChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_dialogs is not { } dialogs)
        {
            return;
        }

        if (e.PropertyName == nameof(CasePasswordViewModel.IsOpen))
        {
            CasePasswordBox.Clear();
        }

        if (e.PropertyName is nameof(CasePasswordViewModel.IsOpen) or nameof(CasePasswordViewModel.IsWrong)
            && dialogs.Password is { IsOpen: true, IsDone: false })
        {
            FocusLater(CasePasswordBox);
        }
        else if (e.PropertyName == nameof(CasePasswordViewModel.IsDone) && dialogs.Password.IsDone)
        {
            FocusLater(ToGraph);
        }
    }

    /// <summary>Puts the caret in the title when the properties open on the tab Egenskaper.</summary>
    /// <param name="sender">The view model.</param>
    /// <param name="e">The property that changed.</param>
    private void OnPropertiesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CasePropertiesViewModel.IsOpen) && _dialogs?.Properties.IsOpen == true)
        {
            FocusLater(PropertiesLayer);
        }
    }

    /// <summary>Puts the caret in the new case number when the choice of branch opens.</summary>
    /// <param name="sender">The view model.</param>
    /// <param name="e">The property that changed.</param>
    private void OnBranchChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CaseBranchViewModel.IsOpen) && _dialogs?.Branch.IsOpen == true)
        {
            FocusLater(BranchNumberBox);
        }
    }

    /// <summary>Moves the keyboard to an element once it is laid out and shown.</summary>
    /// <param name="target">The element.</param>
    private void FocusLater(UIElement target) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            target.Focusable = true;
            target.Focus();
        });

    /// <summary>Öppna: hands the password field to the view model, which reads and clears it.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private void OnOpenWithPassword(object sender, RoutedEventArgs e) => OpenWithPassword();

    /// <summary>Opens the colleague's file with what the password field holds.</summary>
    private void OpenWithPassword()
    {
        if (_dialogs is { } dialogs)
        {
            _ = dialogs.Password.OpenAsync(new PasswordEntry(CasePasswordBox));
        }
    }

    /// <summary>Spara in the card of the case password: hands its two fields to the view model.</summary>
    /// <param name="sender">The button inside the card.</param>
    /// <param name="e">The click.</param>
    private void OnSetPassword(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject inside)
        {
            SetPasswordFrom(inside);
        }
    }

    /// <summary>
    /// Finds the two password fields of the card an element lies in and hands them to the view model.
    /// </summary>
    /// <param name="inside">An element in the card.</param>
    private void SetPasswordFrom(DependencyObject inside)
    {
        if (_dialogs is not { } dialogs || FindAncestor<StackPanel>(inside, "PasswordForm") is not { } form)
        {
            return;
        }

        var boxes = FindDescendants<PasswordBox>(form).ToList();
        if (boxes.Count == 2)
        {
            _ = dialogs.Properties.SetPasswordAsync(new PasswordEntry(boxes[0]), new PasswordEntry(boxes[1]));
        }
    }

    /// <summary>Puts the caret in the first password field when the card folds them out.</summary>
    /// <param name="sender">The field.</param>
    /// <param name="e">The event.</param>
    private void OnPasswordFormLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
        {
            box.IsVisibleChanged += (_, _) =>
            {
                if (box.IsVisible)
                {
                    FocusLater(box);
                }
            };
        }
    }

    /// <summary>Saves the title when its field is left.</summary>
    /// <param name="sender">The field.</param>
    /// <param name="e">The event.</param>
    private void OnTitleLeft(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_dialogs?.Properties is { IsOpen: true } properties)
        {
            properties.SaveTitleCommand.Execute(null);
        }
    }

    /// <summary>A click on the working copy's card chooses it (its number field is inside it).</summary>
    /// <param name="sender">The card.</param>
    /// <param name="e">The click.</param>
    private void OnCopyBranchClicked(object sender, MouseButtonEventArgs e) => _dialogs?.Branch.PickCopy();

    /// <summary>A click on the darkened background closes the dialog over it, as Esc does.</summary>
    /// <param name="sender">The background.</param>
    /// <param name="e">The click.</param>
    private void OnDimClicked(object sender, MouseButtonEventArgs e)
    {
        CloseShown();
        e.Handled = true;
    }

    /// <summary>
    /// Esc closes the dialog shown; Enter opens the colleague's file, saves the case password written in its card or
    /// the title, or carries out the choice of branch.
    /// </summary>
    /// <param name="sender">The dialogs.</param>
    /// <param name="e">The key.</param>
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_dialogs is null || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = CloseShown();
        }
        else if (e.Key == Key.Enter && e.OriginalSource is not ButtonBase)
        {
            e.Handled = Enter(e.OriginalSource as DependencyObject);
        }
    }

    /// <summary>Carries out what Enter does in the dialog shown.</summary>
    /// <param name="source">Where the key was pressed.</param>
    /// <returns><see langword="true"/> when something was done.</returns>
    private bool Enter(DependencyObject? source)
    {
        var dialogs = _dialogs!;
        if (dialogs.Password.IsOpen)
        {
            if (dialogs.Password.IsDone)
            {
                dialogs.Password.Close();
            }
            else
            {
                OpenWithPassword();
            }

            return true;
        }

        if (dialogs.Branch.IsOpen)
        {
            dialogs.Branch.GoCommand.Execute(null);
            return true;
        }

        if (dialogs.Properties.IsOpen && source is PasswordBox box)
        {
            SetPasswordFrom(box);
            return true;
        }

        if (dialogs.Properties.IsOpen && ReferenceEquals(source, TitleBox))
        {
            dialogs.Properties.SaveTitleCommand.Execute(null);
            return true;
        }

        return false;
    }

    /// <summary>Closes the dialog that is shown, if any.</summary>
    /// <returns><see langword="true"/> when one was shown.</returns>
    private bool CloseShown()
    {
        if (_dialogs is not { IsAnyOpen: true } dialogs)
        {
            return false;
        }

        if (dialogs.Properties.EditingCard is not null)
        {
            dialogs.Properties.CancelPassword();
            return true;
        }

        dialogs.CloseAll();
        return true;
    }

    /// <summary>Finds the nearest ancestor of a type with a name.</summary>
    /// <typeparam name="T">The type.</typeparam>
    /// <param name="start">Where to start.</param>
    /// <param name="name">The name.</param>
    /// <returns>The ancestor, or <see langword="null"/>.</returns>
    private static T? FindAncestor<T>(DependencyObject start, string name)
        where T : FrameworkElement
    {
        for (var current = start; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T element && element.Name == name)
            {
                return element;
            }
        }

        return null;
    }

    /// <summary>Gives the descendants of a type, in the order they are laid out.</summary>
    /// <typeparam name="T">The type.</typeparam>
    /// <param name="root">Where to look.</param>
    /// <returns>The descendants.</returns>
    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
                continue;
            }

            foreach (var descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
