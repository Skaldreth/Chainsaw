using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The graph's page (sketch GR4): the surface, its corner's buttons, the legend and the inspector. Only wiring; what
/// happens lives in <see cref="GraphViewModel"/>.
/// </summary>
public partial class GraphView : UserControl
{
    /// <summary>The corner's space to the inspector when nothing lies over the page.</summary>
    private static readonly Thickness CornerMargin = new(0, 12, 16, 0);

    /// <summary>The node's menu's height, for keeping it inside the page.</summary>
    private const double MenuHeight = 220;

    /// <summary>The space between the chosen node and its actions over it (sketch IN2b).</summary>
    private const double ToolGap = 10;

    /// <summary>The space between the chosen node and its form beside it (sketch IN1b).</summary>
    private const double FormGap = 16;

    /// <summary>How far what stands over a node keeps from the page's edges.</summary>
    private const double EdgeSpace = 8;

    /// <summary>The smallest the node's actions are drawn, as a share of their own size.</summary>
    private const double SmallestTools = 0.75;

    /// <summary>
    /// The zoom under which the node's actions are hidden, since the nodes are then too small to act on.
    /// </summary>
    private const double ToolsHiddenBelow = 0.45;

    /// <summary>
    /// How far above the node's middle the form's heading stands, so that the arrow meets the heading.
    /// </summary>
    private const double FormLift = 26;

    /// <summary>Half the arrow's side, and the space it keeps from the form's corners.</summary>
    private const double CaretHalf = 6;

    /// <summary>The space the arrow keeps from the form's rounded corners.</summary>
    private const double CaretInset = 14;

    /// <summary>How far a panel beside a node glides in from.</summary>
    private const double GlideFrom = 8;

    /// <summary>How long a panel beside a node takes to glide in.</summary>
    private const double GlideMilliseconds = 200;

    /// <summary>Creates the page.</summary>
    public GraphView()
    {
        InitializeComponent();
        Canvas.Drawn += (_, _) =>
        {
            PlaceOverNode();
            PlaceAreaForm();
        };
    }

    /// <summary>
    /// The inspector's width when it first opens, in device-independent pixels: wider than sketch IN2b's 340 px
    /// (the user's wish 2026-11-08), so that addresses and amounts fit without being cut.
    /// </summary>
    private const double InspectorWidth = 420;

    /// <summary>How long the inspector takes to glide in from the right edge, or out again.</summary>
    private const double InspectorGlideMilliseconds = 220;

    /// <summary>Whether the inspector is shown or on its way in, so that a glide out can tell it was overtaken.</summary>
    private bool _inspectorShown;

    /// <summary>The width of the checks' panel where the inspector stands (sketch KF2b).</summary>
    private const double ChecksPanelWidth = 400;

    /// <summary>The narrowest the inspector can be dragged to.</summary>
    private const double NarrowestInspector = 240;

    /// <summary>The widest the inspector can be dragged to, as a share of the page.</summary>
    private const double WidestInspectorShare = 0.5;

    /// <summary>The inspector's width as the user last dragged it, kept while Chainsaw runs.</summary>
    private double _inspectorWidth = InspectorWidth;

    /// <summary>
    /// Moves the graph's corner (the switch, the method and the amounts) out from under a panel that lies over the
    /// page's right side, such as the Guide's (sketch GD1b), so that it can still be seen and used.
    /// </summary>
    /// <param name="covered">How far from the page's right edge the panel reaches; 0 when none is open.</param>
    public void MakeRoom(double covered)
    {
        var inspector = InspectorColumn.Width.Value;
        var beyond = Math.Max(0, covered - inspector);
        Corner.Margin = CornerMargin with { Right = CornerMargin.Right + beyond };
    }

    /// <summary>
    /// Wires the trash (sketch SO1a): a node dragged on the surface grows the bin as it comes near, the bin's click opens
    /// the trash's view, and the warning's buttons throw or cancel. The keyboard goes back to the surface after.
    /// </summary>
    /// <param name="model">The view model.</param>
    private void AttachTrash(GraphViewModel model)
    {
        Canvas.NodeDragged += (_, at) => model.DragNear(Bin.DistanceFrom(at, Canvas));
        Bin.Opened += (_, _) => model.TrashView.Toggle();
        TrashWarning.Confirmed += (_, _) => model.ConfirmThrowCommand.Execute(null);
        TrashWarning.Cancelled += (_, _) => model.CancelThrow();
        model.TrashView.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TrashViewModel.IsOpen) && !model.TrashView.IsOpen)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(Canvas));
            }
        };
    }

    /// <summary>Attaches the graph's view model to the page and its surface.</summary>
    /// <param name="model">The view model.</param>
    public void Attach(GraphViewModel model)
    {
        DataContext = model;
        Canvas.Attach(model);
        TimeStrip.Attach(model.Timeline);
        AttachTrash(model);
        AttachWait(model);
        model.Checks.PropertyChanged += (_, e) =>
        {
            // The list's keys (J, K, Enter, X and ?) and Z work in the graph; the keyboard goes back to it.
            if (e.PropertyName is nameof(ChecksViewModel.IsOpen) or nameof(ChecksViewModel.IsFormOpen)
                && !model.Checks.IsFormOpen)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(Canvas));
            }

            if (e.PropertyName == nameof(ChecksViewModel.IsFormOpen))
            {
                PlaceAreaForm();
            }
        };
        model.PropertyChanged += (_, e) =>
        {
            // The node's menu opens where the right click was, inside the page.
            if (e.PropertyName is nameof(GraphViewModel.MenuNode) or nameof(GraphViewModel.SurfaceMenuAt)
                && model.IsMenuOpen)
            {
                var at = Canvas.MenuAt;
                NodeMenu.Margin = new Thickness(
                    Math.Max(0, Math.Min(at.X, Canvas.ActualWidth - NodeMenu.Width)),
                    Math.Max(0, Math.Min(at.Y, Canvas.ActualHeight - MenuHeight)), 0, 0);
            }

            if (e.PropertyName is nameof(GraphViewModel.SelectedNode) or nameof(GraphViewModel.HasNodeTools)
                or nameof(GraphViewModel.IsWatchFormOpen))
            {
                PlaceOverNode();
            }

            if (e.PropertyName == nameof(GraphViewModel.IsWatchFormOpen) && !model.IsWatchFormOpen
                && IsKeyboardFocusWithin)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(Canvas));
            }
        };
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GraphViewModel.IsInspectorShown))
            {
                ShowInspector(model.IsInspectorShown);
            }
        };
        model.Checks.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChecksViewModel.IsOpen))
            {
                ShowInspector(model.IsInspectorShown);
            }
        };
        ShowInspector(model.IsInspectorShown);
        PreviewKeyDown += (_, e) => e.Handled = e.Handled || PasteStartPoint(e, model.Start);
        model.CopyWanted += (_, text) => CopyText(text);
        model.Forms.PropertyChanged += (_, e) =>
        {
            // A closed form's field collapses; the keyboard goes back to the graph, so that E, F, L, U and K work.
            if (e.PropertyName == nameof(InspectorFormsViewModel.Form) && !model.Forms.IsOpen
                && IsKeyboardFocusWithin)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(Canvas));
            }
        };
    }

    /// <summary>Keeps what stands over the chosen node, and the own area's form, in place once sized.</summary>
    /// <param name="sender">The actions or the form.</param>
    /// <param name="e">The size.</param>
    private void OnOverNodeSized(object sender, SizeChangedEventArgs e)
    {
        PlaceOverNode();
        PlaceAreaForm();
    }

    /// <summary>
    /// Puts the own area's form beside the actors it is about (sketch KF1b): to the right of them, or else to their
    /// left, its top at theirs; under the graph's corner when none of them is on the graph.
    /// </summary>
    private void PlaceAreaForm()
    {
        if (DataContext is not GraphViewModel { Checks.IsFormOpen: true } model)
        {
            return;
        }

        var frames = model.Checks.FormActors
            .Select(Canvas.NodeOnScreen)
            .OfType<Rect>()
            .ToList();
        var width = OwnAreaForm.ActualWidth;
        var height = OwnAreaForm.ActualHeight;
        if (frames.Count == 0)
        {
            OwnAreaForm.Margin = new Thickness(Canvas.ActualWidth - width - CornerMargin.Right, AreaFormTop, 0, 0);
            return;
        }

        var actors = frames.Skip(1).Aggregate(frames[0], Rect.Union);
        var right = actors.Right + FormGap + width <= Canvas.ActualWidth - EdgeSpace;
        OwnAreaForm.Margin = new Thickness(
            Within(right ? actors.Right + FormGap : actors.Left - FormGap - width, EdgeSpace,
                Canvas.ActualWidth - width - EdgeSpace),
            Within(actors.Top, EdgeSpace, Canvas.ActualHeight - height - EdgeSpace),
            0,
            0);
    }

    /// <summary>The own area's form's top under the graph's corner, when none of its actors is on the graph.</summary>
    private const double AreaFormTop = 56;

    /// <summary>
    /// Puts the chosen node's actions over it (under it when there is no room above, sketch IN2b) and its open form
    /// beside it, to the right or else to the left, with the arrow at the node's middle (sketch IN1b).
    /// </summary>
    private void PlaceOverNode()
    {
        if (DataContext is not GraphViewModel model || Canvas.NodeOnScreen(model.SelectedNode) is not { } node)
        {
            return;
        }

        var width = Canvas.ActualWidth;
        var scale = ScaleTools(Canvas.Zoom);
        var toolsWidth = NodeTools.ActualWidth * scale;
        var toolsHeight = NodeTools.ActualHeight * scale;
        var above = node.Top - ToolGap - toolsHeight;
        NodeTools.Margin = new Thickness(
            Within(node.Left + (node.Width / 2) - (toolsWidth / 2), EdgeSpace, width - toolsWidth - EdgeSpace),
            above >= EdgeSpace ? above : node.Bottom + ToolGap,
            0,
            0);

        PlaceBeside(NodeForm, Caret, CaretTurn, node);
        PlaceBeside(WatchPanel, WatchCaret, WatchCaretTurn, node);
    }

    /// <summary>
    /// Sizes the node's actions after the zoom, so that they never dwarf a graph seen from afar: they shrink with it
    /// down to <see cref="SmallestTools"/>, and below <see cref="ToolsHiddenBelow"/> they fade away, while their keys
    /// still work.
    /// </summary>
    /// <param name="zoom">The graph's zoom.</param>
    /// <returns>The scale the actions are drawn at.</returns>
    private double ScaleTools(double zoom)
    {
        var scale = Math.Clamp(zoom, SmallestTools, 1);
        NodeTools.RenderTransform = new ScaleTransform(scale, scale);
        var shown = zoom >= ToolsHiddenBelow;
        NodeTools.Opacity = shown ? 1 : 0;
        NodeTools.IsHitTestVisible = shown;
        return scale;
    }

    /// <summary>
    /// Puts a panel beside a node, to its right or else to its left, its heading at the node's middle, with its arrow
    /// pointing at the node (sketch IN1b).
    /// </summary>
    /// <param name="panel">The panel.</param>
    /// <param name="caret">Its arrow.</param>
    /// <param name="turn">The arrow's turn, which points it left or right.</param>
    /// <param name="node">The node's frame on the screen.</param>
    private void PlaceBeside(FrameworkElement panel, FrameworkElement caret, RotateTransform turn, Rect node)
    {
        var width = panel.Width;
        var height = panel.ActualHeight;
        var right = node.Right + FormGap + width <= Canvas.ActualWidth - EdgeSpace;
        var middle = node.Top + (node.Height / 2);
        var top = Within(middle - FormLift, EdgeSpace, Canvas.ActualHeight - height - EdgeSpace);
        panel.Margin = new Thickness(right ? node.Right + FormGap : node.Left - FormGap - width, top, 0, 0);
        caret.Margin = new Thickness(
            right ? -CaretHalf - 1 : width - CaretHalf - 1,
            Within(middle - top - CaretHalf, CaretInset, height - CaretInset - (2 * CaretHalf)),
            0,
            0);
        turn.Angle = right ? 45 : 225;
    }

    /// <summary>
    /// Fades the selection view's chrome in as the dots settle (sketch EX2: from 40 % of their 760 ms), so that the
    /// panel and the bars come with the view and not before it; at once without animations.
    /// </summary>
    /// <param name="sender">The chrome.</param>
    /// <param name="e">Whether it was shown.</param>
    private void OnHeldBackShown(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement chrome || e.NewValue is not true || !SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var fade = new DoubleAnimationUsingKeyFrames
        {
            FillBehavior = FillBehavior.Stop,
        };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(
            0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ChromeWaitMilliseconds))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(
            1, KeyTime.FromTimeSpan(Graph.HeldBackMotion.OutTime + Graph.HeldBackMotion.ViewTime)));
        chrome.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// How long the selection view's chrome waits before it fades in: the blast and the dots' first part.
    /// </summary>
    private const double ChromeWaitMilliseconds = 1000;

    /// <summary>Centres the selection view's time window where the time track was dragged to (sketch EX2).</summary>
    /// <param name="sender">The track.</param>
    /// <param name="share">Where along it, from 0 to 1.</param>
    private void OnTimeWindowMoved(object? sender, double share) =>
        (DataContext as GraphViewModel)?.HeldBack?.MoveWindowTo(share);

    /// <summary>
    /// Lets a panel beside a node glide in from the node's side and fade in as it opens; with Windows' animations off
    /// it
    /// just appears.
    /// </summary>
    /// <param name="sender">The panel.</param>
    /// <param name="e">Whether it is shown.</param>
    private void OnBesideNodeShown(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement panel || e.NewValue is not true || !SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        PlaceOverNode();
        var time = new Duration(TimeSpan.FromMilliseconds(GlideMilliseconds));
        var glide = new TranslateTransform();
        panel.RenderTransform = glide;
        glide.BeginAnimation(
            TranslateTransform.XProperty,
            new System.Windows.Media.Animation.DoubleAnimation(-GlideFrom, 0, time)
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
                },
            });
        panel.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, time));
    }

    /// <summary>Keeps a value between two bounds; when they cross, the lower one wins.</summary>
    /// <param name="value">The value.</param>
    /// <param name="lowest">The lower bound.</param>
    /// <param name="highest">The upper bound.</param>
    /// <returns>The value within the bounds.</returns>
    private static double Within(double value, double lowest, double highest) =>
        Math.Max(lowest, Math.Min(value, highest));

    /// <summary>
    /// Shows the inspector, gliding in from the right edge, or glides it out and folds it away when there is nothing in
    /// it; with Windows' animations off it just appears and goes.
    /// </summary>
    /// <param name="shown">Whether the inspector is shown.</param>
    private void ShowInspector(bool shown)
    {
        var wasShown = _inspectorShown;
        _inspectorShown = shown;
        if (shown)
        {
            OpenInspectorColumn();
            if (!wasShown)
            {
                // From where a glide out was overtaken, or from beyond the right edge.
                var from = Inspector.RenderTransform is TranslateTransform { X: > 0 } halfway
                    ? halfway.X
                    : InspectorColumn.Width.Value;
                GlideInspector(from, to: 0, then: null);
            }

            return;
        }

        if (!wasShown)
        {
            FoldInspector();
            return;
        }

        GlideInspector(from: 0, to: InspectorColumn.Width.Value, then: () =>
        {
            if (!_inspectorShown)
            {
                FoldInspector();
            }
        });
    }

    /// <summary>Gives the inspector its column at the width last dragged to, and shows it with its shadow and edge.</summary>
    private void OpenInspectorColumn()
    {
        InspectorColumn.MinWidth = NarrowestInspector;
        InspectorColumn.MaxWidth = Math.Max(NarrowestInspector, ActualWidth * WidestInspectorShare);
        // The checks' panel (sketch KF2b) is never narrower than its 400 px, whatever the inspector was dragged to.
        var checksOpen = DataContext is GraphViewModel { Checks.IsOpen: true };
        var width = checksOpen ? Math.Max(_inspectorWidth, ChecksPanelWidth) : _inspectorWidth;
        InspectorColumn.Width = new GridLength(width);
        SetInspectorVisibility(Visibility.Visible);
    }

    /// <summary>Folds the inspector's column away and hides it with its shadow and edge.</summary>
    private void FoldInspector()
    {
        InspectorColumn.MinWidth = 0;
        InspectorColumn.MaxWidth = 0;
        InspectorColumn.Width = new GridLength(0);
        SetInspectorVisibility(Visibility.Collapsed);
    }

    /// <summary>
    /// Shows or hides the inspector and the edge that drags its width, and with them the sheet's rounded corner and its
    /// shadow over the panel (sketch IN5a); without the inspector the sheet fills the page square.
    /// </summary>
    /// <param name="visibility">The visibility.</param>
    private void SetInspectorVisibility(Visibility visibility)
    {
        var shown = visibility == Visibility.Visible;
        Inspector.Visibility = visibility;
        InspectorEdge.Visibility = visibility;
        Sheet.CornerRadius = shown ? new CornerRadius(0, 0, SheetCorner, 0) : default;
        SheetShadow.Opacity = shown ? SheetShadowStrength : 0;
        InspectorReachChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The sheet's corner towards the inspector, as the title bar's (sketch IN5a).</summary>
    public const double SheetCorner = 14;

    /// <summary>How dark the sheet's shadow over the inspector is.</summary>
    private const double SheetShadowStrength = 0.45;

    /// <summary>Occurs when the inspector opens, closes or changes width, so the title bar can stop where it starts.</summary>
    public event EventHandler? InspectorReachChanged;

    /// <summary>Gets how far in from the right edge the inspector reaches, or 0 when it is closed.</summary>
    public double InspectorReach => Inspector.Visibility == Visibility.Visible ? InspectorColumn.ActualWidth : 0;

    /// <summary>Tells the window the inspector's new width while its edge is dragged.</summary>
    /// <param name="sender">The inspector.</param>
    /// <param name="e">Not used.</param>
    private void OnInspectorSized(object sender, SizeChangedEventArgs e) => InspectorReachChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Slides the inspector sideways, easing out on the way in and in on the way out; without
    /// animations it jumps to the end at once.
    /// </summary>
    /// <param name="from">The offset to the right it starts at.</param>
    /// <param name="to">The offset it ends at; 0 is in place.</param>
    /// <param name="then">What is done when it has ended, or <see langword="null"/>.</param>
    private void GlideInspector(double from, double to, Action? then)
    {
        var slide = new TranslateTransform();
        Inspector.RenderTransform = slide;
        if (!SystemParameters.ClientAreaAnimation)
        {
            slide.X = to;
            then?.Invoke();
            return;
        }

        var glide = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(InspectorGlideMilliseconds)))
        {
            EasingFunction = new CubicEase { EasingMode = to == 0 ? EasingMode.EaseOut : EasingMode.EaseIn },
        };
        if (then is not null)
        {
            glide.Completed += (_, _) => then();
        }

        slide.BeginAnimation(TranslateTransform.XProperty, glide);
    }

    /// <summary>
    /// Tells the model that the menu of areas closed, after a click outside it or Esc, so that the next click on its
    /// button opens it again.
    /// </summary>
    /// <param name="sender">The menu.</param>
    /// <param name="e">Nothing.</param>
    private void OnAreasMenuClosed(object? sender, EventArgs e)
    {
        if (DataContext is GraphViewModel model)
        {
            model.Checks.CloseMenu();
        }
    }

    /// <summary>
    /// Keeps the width the inspector was dragged to, so that it comes back when the inspector is shown.
    /// </summary>
    /// <param name="sender">The inspector's edge.</param>
    /// <param name="e">The drag.</param>
    private void OnInspectorResized(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _inspectorWidth = InspectorColumn.ActualWidth;
        Canvas.InvalidateVisual();
    }

    /// <summary>
    /// Ctrl+V anywhere on the empty graph puts the text in the start point's field (sketch GR6c); a text field that has
    /// the keyboard pastes as usual.
    /// </summary>
    /// <param name="e">The key.</param>
    /// <param name="start">The empty graph.</param>
    /// <returns><see langword="true"/> when the paste was taken.</returns>
    private bool PasteStartPoint(KeyEventArgs e, GraphStartViewModel start)
    {
        if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control || !start.IsNoStartPoint
            || Keyboard.FocusedElement is TextBox or ICSharpCode.AvalonEdit.Editing.TextArea)
        {
            return false;
        }

        string text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another program holds the clipboard; nothing is pasted.
            return false;
        }

        if (!start.Paste(text))
        {
            return false;
        }

        StartView.FocusField();
        return true;
    }

    /// <summary>Copies a node's address to the clipboard; a clipboard held by another program is left alone.</summary>
    /// <param name="text">The address.</param>
    private static void CopyText(string text) => Interop.CaseClipboard.TrySetText(text);
}
