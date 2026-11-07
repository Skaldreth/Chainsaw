using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Chainsaw.App.Navigation;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// An open submenu of the pill (sketch PN1). Any icon of the pill with a submenu in <see cref="SubmenuTable"/> is
/// shown with it: the window gives the icon and where its button is, and the control draws the outline from
/// <see cref="SubmenuShape"/> and the rows. The keys are passed to <see cref="SubmenuViewModel"/>, where the logic is.
/// </summary>
public partial class PillSubmenu : UserControl
{
    /// <summary>How wide the menu starts as it folds out, as a share of its width.</summary>
    private const double FoldFrom = 0.9;

    /// <summary>How far the rows glide in from the left as the menu folds out.</summary>
    private const double RowsGlide = 8;

    /// <summary>How long the menu takes to fold out.</summary>
    private const double FoldMilliseconds = 180;

    /// <summary>Creates the submenu, hidden.</summary>
    public PillSubmenu()
    {
        InitializeComponent();
        Rows.Margin = new Thickness(SubmenuShape.TabWidth, SubmenuShape.Padding, 0, SubmenuShape.Padding);
        Rows.Width = SubmenuShape.MenuWidth;
        Rows.Padding = new Thickness(SubmenuShape.Padding, 0, SubmenuShape.Padding, 0);
        PreviewKeyDown += OnKey;
    }

    /// <summary>Gets the submenu shown, or <see langword="null"/> when none is.</summary>
    private SubmenuViewModel? Menu => (DataContext as RailItemViewModel)?.Submenu;

    /// <summary>
    /// Shows an icon's submenu with the tab over its button, or hides the control. The keyboard goes into the menu once
    /// it is laid out.
    /// </summary>
    /// <param name="item">The icon whose submenu is open, or <see langword="null"/> to hide.</param>
    /// <param name="at">The top left of the icon's button, in the control's parent's coordinates.</param>
    public void Show(RailItemViewModel? item, Point at)
    {
        if (item?.Submenu is not { } menu)
        {
            DataContext = null;
            Visibility = Visibility.Collapsed;
            return;
        }

        var wasHidden = Visibility != Visibility.Visible || DataContext != item;
        DataContext = item;
        var outline = Geometry.Parse(menu.Outline);
        outline.Freeze();
        Outline.Data = outline;
        Shadow.Data = outline;
        Canvas.SetLeft(this, at.X);
        Canvas.SetTop(this, at.Y);
        Visibility = Visibility.Visible;
        if (wasHidden)
        {
            FoldOut();
        }

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            if (Menu?.IsOpen == true)
            {
                Focus();
            }
        });
    }

    /// <summary>
    /// Folds the menu out of its button (the user's wish 2026-11-07): it grows from the tab towards the right and fades
    /// in, while the rows glide the last bit into place. With Windows' animations off it just appears.
    /// </summary>
    private void FoldOut()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var time = new Duration(TimeSpan.FromMilliseconds(FoldMilliseconds));
        var ease = new System.Windows.Media.Animation.CubicEase
        {
            EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
        };
        var grow = new ScaleTransform(FoldFrom, 1);
        RenderTransformOrigin = new Point(0, 0);
        RenderTransform = grow;
        grow.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new System.Windows.Media.Animation.DoubleAnimation(FoldFrom, 1, time) { EasingFunction = ease });
        BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, time));
        var glide = new TranslateTransform();
        Rows.RenderTransform = glide;
        glide.BeginAnimation(
            TranslateTransform.XProperty,
            new System.Windows.Media.Animation.DoubleAnimation(-RowsGlide, 0, time) { EasingFunction = ease });
    }

    /// <summary>
    /// Closes the menu when the keyboard leaves it for something else in the window, without taking the keyboard back.
    /// When it leaves for nowhere, the window is being left, and the window closes the menu and gives the keyboard
    /// back to the button when it is active again.
    /// </summary>
    /// <param name="e">Where the keyboard went.</param>
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (e.NewFocus is DependencyObject next && next != this && !IsAncestorOf(next))
        {
            Menu?.Dismiss();
        }
    }

    /// <summary>
    /// The menu's keys: the arrows, Home, End, Page Up and Page Down move the mark, Enter and Space choose it (not when
    /// held from opening the menu), a letter in the key column chooses its row, and Esc or ← closes. Tab, Shift+Tab
    /// and → stay in the menu. Shortcuts with Ctrl or Alt go on to the window, where Ctrl+N opens Nytt ärende and every
    /// view's shortcut closes the menu.
    /// </summary>
    /// <param name="sender">The control.</param>
    /// <param name="e">The key.</param>
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Menu is not { IsOpen: true } menu)
        {
            return;
        }

        if (e.Key == Key.Tab && (Keyboard.Modifiers & ~ModifierKeys.Shift) == ModifierKeys.None)
        {
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        e.Handled = true;
        switch (e.Key)
        {
            case Key.Down:
                menu.MoveNext();
                break;
            case Key.Up:
                menu.MovePrevious();
                break;
            case Key.Home or Key.PageUp:
                menu.MoveFirst();
                break;
            case Key.End or Key.PageDown:
                menu.MoveLast();
                break;
            case Key.Enter or Key.Space when !e.IsRepeat:
                menu.ChooseMarked();
                break;
            case Key.Escape or Key.Left:
                menu.Close();
                break;
            case Key.Right or Key.Enter or Key.Space:
                break;
            case >= Key.A and <= Key.Z:
                e.Handled = menu.ChooseLetter((char)('A' + (e.Key - Key.A)));
                break;
            default:
                e.Handled = false;
                break;
        }
    }

    /// <summary>Folds the menu in when its tab is clicked, as a click on the button does.</summary>
    /// <param name="sender">The tab.</param>
    /// <param name="e">The click.</param>
    private void OnTabClicked(object sender, MouseButtonEventArgs e)
    {
        Menu?.Close();
        e.Handled = true;
    }

    /// <summary>Marks the row the pointer rests on.</summary>
    /// <param name="sender">The row.</param>
    /// <param name="e">The move.</param>
    private void OnRowPointed(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SubmenuRowViewModel row })
        {
            Menu?.Point(row);
        }
    }

    /// <summary>Chooses the row clicked; a toned-down row does nothing.</summary>
    /// <param name="sender">The row.</param>
    /// <param name="e">The click.</param>
    private void OnRowClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SubmenuRowViewModel row })
        {
            Menu?.Choose(row);
            e.Handled = true;
        }
    }
}
