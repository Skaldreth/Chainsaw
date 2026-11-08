using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Chainsaw.App.Controls;
using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The start page's piles (sketch GR8a): the drags on the ring, the piles drawn as the graph's pile node, and the pile
/// fanned out over the hub. Only drawing and the pointer live here; what a drop or a click means lives in
/// <see cref="GraphStartViewModel"/>.
/// </summary>
public partial class GraphStartView
{
    /// <summary>A pile node's size, the same as the other nodes on the ring.</summary>
    private static readonly Size PileSize = new(200, 54);

    /// <summary>How far each card behind a pile's front lies, so that it reads as a deck (sketch EX2b).</summary>
    private const double DeckOffset = 3;

    /// <summary>How many cards are drawn behind a pile's front.</summary>
    private const int DeckDepth = 2;

    /// <summary>The corner radius of a pile's frames, square as the graph's pile node.</summary>
    private const double PileRadius = 10;

    /// <summary>The corner radius of a card in the fan, as the graph's cards.</summary>
    private const double CardRadius = 7;

    /// <summary>How far the cards dragged together lie below each other, as in the graph.</summary>
    private const double DraggedStep = 6;

    /// <summary>How much a case or pile under a lifted case grows, to say that it takes the drop.</summary>
    private const double TargetScale = 1.06;

    /// <summary>How much a lifted case is turned, as a card picked up (sketch GR8a).</summary>
    private const double LiftAngle = -3;

    /// <summary>How dark the hub is under a fanned pile, as the graph's dimmed ground.</summary>
    private const double FanDimOpacity = 0.78;

    /// <summary>The nodes on the ring by their item's key, for the drags and the fan's origin.</summary>
    private readonly Dictionary<string, FrameworkElement> _ringNodes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The cards in the fan by their case number.</summary>
    private readonly Dictionary<string, Border> _cards = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A press on a loose case on the ring, which becomes a lift once the pointer moves far enough.</summary>
    private (FrameworkElement Node, string CaseNumber, Point At)? _ringPress;

    /// <summary>The lifted case: its node, the picture that follows the pointer and where it was gripped.</summary>
    private (FrameworkElement Node, FrameworkElement Ghost, string CaseNumber, Vector Grip)? _lift;

    /// <summary>The node under the lifted case that takes the drop, or <see langword="null"/>.</summary>
    private FrameworkElement? _dropTarget;

    /// <summary>A press on a card in the fan, which becomes a drag once the pointer moves far enough.</summary>
    private (string CaseNumber, Point At)? _cardPress;

    /// <summary>The cards being dragged out of the fan, the grabbed one first.</summary>
    private IReadOnlyList<string> _dragged = [];

    /// <summary>The pile fanned out, so that a new name is shown in it and the right ring node is hidden.</summary>
    private string? _fannedPile;

    /// <summary>Wires the pointer and the keys for the piles, once.</summary>
    private void WirePiles()
    {
        var dim = new SolidColorBrush(((SolidColorBrush)Brush("Brush.Background")).Color) { Opacity = FanDimOpacity };
        dim.Freeze();
        Fan.BackdropBrush = dim;
        Fan.BackdropClicked += (_, _) => _model?.ClosePile();
        Ring.PreviewMouseLeftButtonDown += OnRingPressed;
        PreviewMouseMove += OnPointerMoved;
        PreviewMouseLeftButtonUp += OnPointerReleased;
        LostMouseCapture += (_, e) =>
        {
            // Only the view's own capture: a button or a card losing theirs bubbles here too.
            if (ReferenceEquals(e.OriginalSource, this))
            {
                CancelLift();
            }
        };
        PreviewKeyDown += OnPileKey;
        Bin.Opened += (_, _) => _model?.OpenTrash();
    }

    /// <summary>Makes a node on the ring: a loose case, or a pile drawn as the graph's pile node.</summary>
    /// <param name="item">What stands there.</param>
    /// <returns>The node.</returns>
    private FrameworkElement RingNode(IStartRingItem item)
    {
        if (item is CasePileItem pile)
        {
            var node = PileNode(pile, inFan: false);
            node.MouseLeftButtonUp += (_, e) =>
            {
                if (_lift is null && !pile.IsNaming)
                {
                    e.Handled = true;
                    _model?.OpenPileOf(pile.Id);
                }
            };
            return node;
        }

        return new ContentPresenter
        {
            Content = item, ContentTemplate = (DataTemplate)FindResource("RecentCase"), Tag = item,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
    }

    /// <summary>Hides the fanned pile's own node on the ring, and puts the keyboard in a new pile's name.</summary>
    /// <param name="model">The model.</param>
    private void ShowRingPiles(GraphStartViewModel model)
    {
        if (_fannedPile is { } open && _ringNodes.TryGetValue(open, out var hidden))
        {
            hidden.Opacity = 0;
        }

        if (model.NamingPileId is { } naming && _ringNodes.TryGetValue(naming, out var node))
        {
            FocusName(node);
        }
    }

    /// <summary>
    /// Makes a pile as the graph draws its pile node (sketch EX2b): a square frame with the deck's cards behind it, the
    /// layers in a square icon frame, the name with when its latest case was saved under it, and how many lie in it.
    /// The name is written in the pile itself while it is being named.
    /// </summary>
    /// <param name="pile">The pile.</param>
    /// <param name="inFan">Whether it stands in its fan, where a pen renames it and a click folds it.</param>
    /// <returns>The node.</returns>
    private Grid PileNode(CasePileItem pile, bool inFan)
    {
        var deck = DeckOffset * DeckDepth;
        var node = new Grid
        {
            Width = PileSize.Width + deck, Height = PileSize.Height + deck, Tag = pile, Cursor = Cursors.Hand,
            Background = Brushes.Transparent, RenderTransformOrigin = new Point(0.5, 0.5),
            ToolTip = inFan ? "Fäll ihop · Esc" : "Fäll ut högen",
        };
        AutomationProperties.SetName(node, pile.ShownName);
        for (var depth = DeckDepth; depth > 0; depth--)
        {
            node.Children.Add(DeckCard(depth * DeckOffset));
        }

        var front = new Border
        {
            Width = PileSize.Width, Height = PileSize.Height, CornerRadius = new CornerRadius(PileRadius),
            BorderThickness = new Thickness(1.5), BorderBrush = Brush("Brush.BorderHover"),
            Background = Brush("Brush.Surface"), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(10, 0, 12, 0),
        };
        node.MouseEnter += (_, _) => front.BorderBrush = Brush("Brush.TextDim");
        node.MouseLeave += (_, _) => front.BorderBrush = Brush("Brush.BorderHover");
        front.Child = PileInside(pile, node, inFan);
        node.Children.Add(front);
        return node;
    }

    /// <summary>Makes a card behind a pile's front.</summary>
    /// <param name="offset">How far behind it lies.</param>
    /// <returns>The card.</returns>
    private Border DeckCard(double offset) => new()
    {
        Width = PileSize.Width, Height = PileSize.Height, CornerRadius = new CornerRadius(PileRadius),
        BorderThickness = new Thickness(1), BorderBrush = Brush("Brush.SurfaceSelected"),
        Background = Brush("Brush.Background"), Margin = new Thickness(offset, offset, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
    };

    /// <summary>Makes a pile's inside: the icon, the name or its field, the line under it, and the count.</summary>
    /// <param name="pile">The pile.</param>
    /// <param name="node">The pile's node, which the name's field is found in.</param>
    /// <param name="inFan">Whether a pen renames it.</param>
    /// <returns>The inside.</returns>
    private DockPanel PileInside(CasePileItem pile, Grid node, bool inFan)
    {
        var inside = new DockPanel { VerticalAlignment = VerticalAlignment.Center, LastChildFill = true };
        var icon = new Grid { Style = (Style)FindResource("Grid.NodeIcon") };
        icon.Children.Add(new System.Windows.Shapes.Rectangle
        {
            RadiusX = 7, RadiusY = 7, Stroke = Brush("Brush.TextDim"), StrokeThickness = 1.5,
        });
        icon.Children.Add(new GridIcon
        {
            Size = 15, Data = (Geometry)FindResource("Icon.Layers"), Foreground = Brush("Brush.TextMuted"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        DockPanel.SetDock(icon, Dock.Left);
        inside.Children.Add(icon);
        var count = new TextBlock
        {
            Text = pile.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 12,
            Foreground = Brush("Brush.TextDim"), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        DockPanel.SetDock(count, Dock.Right);
        inside.Children.Add(count);
        if (inFan)
        {
            var pen = PenButton(pile);
            DockPanel.SetDock(pen, Dock.Right);
            inside.Children.Add(pen);
        }

        inside.Children.Add(PileWords(pile, node));
        return inside;
    }

    /// <summary>Makes the small pen in the fanned pile that renames it.</summary>
    /// <param name="pile">The pile.</param>
    /// <returns>The pen.</returns>
    private Button PenButton(CasePileItem pile)
    {
        var pen = new Button
        {
            Style = (Style)FindResource("Button.Graph"), Width = 22, Height = 22, Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "Byt namn", VerticalAlignment = VerticalAlignment.Center,
            Content = new GridIcon { Size = 12, Data = (Geometry)FindResource("Icon.Pen") },
        };
        AutomationProperties.SetName(pen, "Byt namn på högen");
        pen.Click += (_, e) =>
        {
            e.Handled = true;
            if (Fan.Children.OfType<Grid>().FirstOrDefault(each => each.Tag is CasePileItem) is { } centre)
            {
                StartNaming(centre, pile);
            }
        };
        return pen;
    }

    /// <summary>Makes a pile's name with the line under it, and the field the name is written in.</summary>
    /// <param name="pile">The pile.</param>
    /// <param name="node">The pile's node.</param>
    /// <returns>The words.</returns>
    private Grid PileWords(CasePileItem pile, Grid node)
    {
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock
        {
            Text = pile.ShownName, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Brush("Brush.Text"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        words.Children.Add(new TextBlock
        {
            Text = pile.ChangedText, FontSize = 12, Foreground = Brush("Brush.TextDim"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var field = new TextBox
        {
            Style = (Style)FindResource("TextBox.Field"), Tag = GraphStartViewModel.PileNamePlaceholder, Height = 30,
            FontSize = 13, MaxLength = GraphStartViewModel.MaximumPileName, Text = pile.Name,
            Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(field, "Högens namn");
        var holder = new Grid { Tag = node };
        holder.Children.Add(words);
        holder.Children.Add(field);
        if (pile.IsNaming)
        {
            words.Visibility = Visibility.Collapsed;
            field.Visibility = Visibility.Visible;
        }

        WireName(field, words, pile);
        return holder;
    }

    /// <summary>
    /// Wires a pile's name field: Enter or leaving it keeps what is written, Esc keeps the name it had. It is kept
    /// once, so that the field leaving the screen after Esc does not keep what was written after all.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="words">The name and the line, shown again after.</param>
    /// <param name="pile">The pile.</param>
    private void WireName(TextBox field, FrameworkElement words, CasePileItem pile)
    {
        var kept = false;
        void Keep(string name)
        {
            if (kept || field.Visibility != Visibility.Visible)
            {
                return;
            }

            kept = true;
            field.Visibility = Visibility.Collapsed;
            words.Visibility = Visibility.Visible;
            _model?.NamePile(pile.Id, name);
        }

        field.IsVisibleChanged += (_, _) =>
        {
            if (field.IsVisible)
            {
                kept = false;
            }
        };
        field.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                e.Handled = true;
                Keep(e.Key == Key.Enter ? field.Text : pile.Name);
            }
        };
        field.LostKeyboardFocus += (_, _) => Keep(field.Text);
        field.MouseLeftButtonUp += (_, e) => e.Handled = true;
    }

    /// <summary>Shows a pile's name field and puts the keyboard in it.</summary>
    /// <param name="node">The pile's node.</param>
    /// <param name="pile">The pile.</param>
    private void StartNaming(FrameworkElement node, CasePileItem pile)
    {
        if (NameField(node) is not { } field)
        {
            return;
        }

        field.Text = pile.Name;
        ((FrameworkElement)((Grid)field.Parent).Children[0]).Visibility = Visibility.Collapsed;
        field.Visibility = Visibility.Visible;
        FocusName(node);
    }

    /// <summary>Puts the keyboard in a pile's name field once it is laid out, with what is written chosen.</summary>
    /// <param name="node">The pile's node.</param>
    private void FocusName(FrameworkElement node) => Dispatcher.InvokeAsync(
        () =>
        {
            if (NameField(node) is { IsVisible: true } field)
            {
                field.Focus();
                field.SelectAll();
            }
        },
        System.Windows.Threading.DispatcherPriority.Input);

    /// <summary>Finds a pile node's name field among its children, also before it is laid out.</summary>
    /// <param name="node">Where to look.</param>
    /// <returns>The field, or <see langword="null"/>.</returns>
    private static TextBox? NameField(DependencyObject node) => node is TextBox field
        ? field
        : LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>().Select(NameField)
            .FirstOrDefault(found => found is not null);

    // ----- Lifting a case on the ring -------------------------------------------------------------------------------

    /// <summary>Notes a press on a loose case on the ring, which may become a lift.</summary>
    /// <param name="sender">The ring.</param>
    /// <param name="e">The press.</param>
    private void OnRingPressed(object sender, MouseButtonEventArgs e)
    {
        _ringPress = RingNodeAt(e.OriginalSource as DependencyObject) is { Tag: RecentCaseItem item } node
            ? (node, item.CaseNumber, e.GetPosition(this))
            : null;
    }

    /// <summary>Lifts a pressed case once the pointer has moved far enough, and moves a lifted one.</summary>
    /// <param name="sender">The view.</param>
    /// <param name="e">The move.</param>
    private void OnPointerMoved(object sender, MouseEventArgs e)
    {
        var at = e.GetPosition(this);
        if (_lift is { } lift)
        {
            Canvas.SetLeft(lift.Ghost, at.X - lift.Grip.X);
            Canvas.SetTop(lift.Ghost, at.Y - lift.Grip.Y);
            ShowDropTarget(at);
            _model?.LiftNear(Bin.DistanceFrom(at, this));
            return;
        }

        if (_ringPress is { } press && e.LeftButton == MouseButtonState.Pressed && Moved(press.At, at))
        {
            BeginLift(press.Node, press.CaseNumber, at);
        }
    }

    /// <summary>Drops a lifted case: on a case it makes a pile, on a pile it goes in it, else nothing.</summary>
    /// <param name="sender">The view.</param>
    /// <param name="e">The release.</param>
    private void OnPointerReleased(object sender, MouseButtonEventArgs e)
    {
        _ringPress = null;
        if (_lift is not { } lift)
        {
            return;
        }

        e.Handled = true;
        var target = _dropTarget?.Tag;
        if (_model?.DropOnBin(lift.CaseNumber) == true)
        {
            // Thrown in the trash (sketch SO1a): nothing else takes the drop.
            EndLift();
            return;
        }

        EndLift();
        switch (target)
        {
            case RecentCaseItem other:
                _model?.Stack(lift.CaseNumber, other.CaseNumber);
                break;
            case CasePileItem pile:
                _model?.AddToPile(lift.CaseNumber, pile.Id);
                break;
        }
    }

    /// <summary>Lifts a case: a turned picture of it follows the pointer, and its node goes faint.</summary>
    /// <param name="node">The case's node on the ring.</param>
    /// <param name="caseNumber">The case.</param>
    /// <param name="at">The pointer.</param>
    private void BeginLift(FrameworkElement node, string caseNumber, Point at)
    {
        _ringPress = null;
        var corner = node.TranslatePoint(default, this);
        var ghost = new ContentPresenter
        {
            Content = node.Tag, ContentTemplate = (DataTemplate)FindResource("RecentCase"), Opacity = 0.95,
            RenderTransform = new RotateTransform(LiftAngle), RenderTransformOrigin = new Point(0.5, 0.5),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 12, Direction = 270, Opacity = 0.55 },
        };
        LiftLayer.Children.Add(ghost);
        Canvas.SetLeft(ghost, corner.X);
        Canvas.SetTop(ghost, corner.Y);
        node.Opacity = 0.3;
        _lift = (node, ghost, caseNumber, at - corner);
        CaptureMouse();
        _model?.Lift(caseNumber);
    }

    /// <summary>Puts the lifted case down: the picture goes, its node comes back, and no target is marked.</summary>
    private void EndLift()
    {
        if (_lift is not { } lift)
        {
            return;
        }

        _lift = null;
        LiftLayer.Children.Remove(lift.Ghost);
        lift.Node.Opacity = 1;
        MarkDropTarget(null);
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        _model?.Drop();
        _model?.Bin.EndDrag();
    }

    /// <summary>Puts a lifted case back where it was, when the pointer is lost or Esc is pressed.</summary>
    private void CancelLift() => EndLift();

    /// <summary>Marks the case or pile under the pointer when the lifted case can be dropped on it.</summary>
    /// <param name="at">The pointer, in the view.</param>
    private void ShowDropTarget(Point at)
    {
        var under = RingNodeAt(Ring.InputHitTest(TranslatePoint(at, Ring)) as DependencyObject);
        var lifted = _lift!.Value.CaseNumber;
        var takes = under?.Tag switch
        {
            RecentCaseItem other => _model?.CanStack(lifted, other.CaseNumber) == true,
            CasePileItem pile => _model?.CanAddToPile(lifted, pile.Id) == true,
            _ => false,
        };
        MarkDropTarget(takes ? under : null);
    }

    /// <summary>Lets the node that takes the drop grow a little, and the one before go back.</summary>
    /// <param name="target">The node, or <see langword="null"/>.</param>
    private void MarkDropTarget(FrameworkElement? target)
    {
        if (ReferenceEquals(target, _dropTarget))
        {
            return;
        }

        if (_dropTarget is not null)
        {
            _dropTarget.RenderTransform = Transform.Identity;
        }

        _dropTarget = target;
        if (target is not null)
        {
            target.RenderTransform = new ScaleTransform(TargetScale, TargetScale);
        }
    }

    /// <summary>Finds the node on the ring an element lies in.</summary>
    /// <param name="element">The element, for example what the pointer is over.</param>
    /// <returns>The node, or <see langword="null"/> off the ring and for Nytt ärende and Alla ärenden.</returns>
    private FrameworkElement? RingNodeAt(DependencyObject? element)
    {
        for (var current = element; current is not null; current = ParentOf(current))
        {
            if (current is FrameworkElement { Tag: IStartRingItem } node && ReferenceEquals(ParentOf(node), Ring))
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>Gives an element's parent, in the visual tree or, for text, the logical one.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The parent, or <see langword="null"/>.</returns>
    private static DependencyObject? ParentOf(DependencyObject element) => element is Visual
        ? VisualTreeHelper.GetParent(element)
        : LogicalTreeHelper.GetParent(element);

    /// <summary>Says whether the pointer has moved far enough from a press to make it a drag.</summary>
    /// <param name="from">The press.</param>
    /// <param name="to">The pointer.</param>
    /// <returns><see langword="true"/> when it is a drag.</returns>
    private static bool Moved(Point from, Point to) =>
        Math.Abs(to.X - from.X) >= SystemParameters.MinimumHorizontalDragDistance
        || Math.Abs(to.Y - from.Y) >= SystemParameters.MinimumVerticalDragDistance;

    // ----- The fanned pile ------------------------------------------------------------------------------------------

    /// <summary>Fans the open pile out, folds it, or shows its new name, as the model says.</summary>
    /// <param name="model">The model.</param>
    private void ShowFan(GraphStartViewModel model)
    {
        if (model.OpenPile is not { } pile)
        {
            FoldFan();
            return;
        }

        if (_fannedPile == pile.Id && Fan.IsOpen)
        {
            RenameCentre(pile);
            return;
        }

        if (!_ringNodes.TryGetValue(pile.Id, out var node))
        {
            model.ClosePile();
            return;
        }

        _fannedPile = pile.Id;
        var origin = node.TranslatePoint(new Point(node.ActualWidth / 2, node.ActualHeight / 2), Fan);
        node.Opacity = 0;
        var centre = PileNode(pile, inFan: true);
        centre.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _model?.ClosePile();
        };
        _cards.Clear();
        var cards = model.OpenCards.Select(Card).ToList();
        Fan.Open(Hub, origin, centre, cards);
    }

    /// <summary>Folds the fan, and shows the pile's node on the ring again once it is folded.</summary>
    private void FoldFan()
    {
        var folded = _fannedPile;
        _fannedPile = null;
        _cardPress = null;
        _dragged = [];
        Fan.Close(() =>
        {
            if (folded is not null && _ringNodes.TryGetValue(folded, out var node))
            {
                node.Opacity = 1;
            }
        });
    }

    /// <summary>Shows the fanned pile's new name in its node in the middle.</summary>
    /// <param name="pile">The pile.</param>
    private void RenameCentre(CasePileItem pile)
    {
        if (Fan.Children.OfType<Grid>().FirstOrDefault(each => each.Tag is CasePileItem) is not { } centre
            || NameField(centre) is not { Parent: Grid holder })
        {
            return;
        }

        centre.Tag = pile;
        if (holder.Children[0] is StackPanel { Children: [TextBlock name, ..] })
        {
            name.Text = pile.ShownName;
        }
    }

    /// <summary>
    /// Makes a card in the fan as the graph draws its cards: a small frame with the case number and when it was saved;
    /// a chosen card has the accent's frame.
    /// </summary>
    /// <param name="card">The card.</param>
    /// <returns>The card's element.</returns>
    private Border Card(PileCardViewModel card)
    {
        var words = new StackPanel { Margin = new Thickness(6, 1, 6, 0) };
        words.Children.Add(new TextBlock
        {
            Text = card.CaseNumber, FontSize = 10.5, FontWeight = FontWeights.Medium, Foreground = Brush("Brush.Text"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        words.Children.Add(new TextBlock
        {
            Text = card.ChangedText, FontSize = 9.5, Foreground = Brush("Brush.TextDim"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var element = new Border
        {
            Width = PileFanLayout.CardWidth, Height = PileFanLayout.CardHeight,
            CornerRadius = new CornerRadius(CardRadius),
            Child = words, Cursor = Cursors.Hand, Tag = card,
            ToolTip = "Öppna ärendet · Shift-klick väljer · dra ut på ytan",
        };
        AutomationProperties.SetName(element, card.CaseNumber);
        ShowChosen(element, card);
        card.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PileCardViewModel.IsSelected))
            {
                ShowChosen(element, card);
            }
        };
        element.MouseLeftButtonDown += (_, e) => PressCard(element, card, e);
        element.MouseMove += (_, e) => DragCards(element, e);
        element.MouseLeftButtonUp += (_, e) => ReleaseCard(element, card, e);
        element.LostMouseCapture += (_, _) => ForgetCardPress();
        _cards[card.CaseNumber] = element;
        return element;
    }

    /// <summary>Draws a card chosen or not: the accent's frame on a raised ground, or a thin dim frame.</summary>
    /// <param name="element">The card's element.</param>
    /// <param name="card">The card.</param>
    private void ShowChosen(Border element, PileCardViewModel card)
    {
        element.BorderBrush = card.IsSelected ? Brush("Brush.Accent") : Brush("Brush.TextDim");
        element.BorderThickness = new Thickness(card.IsSelected ? 1.5 : 1);
        element.Background = card.IsSelected ? Brush("Brush.SurfaceRaised") : Brush("Brush.Surface");
    }

    /// <summary>Notes a press on a card, which is a click or the start of a drag.</summary>
    /// <param name="element">The card's element.</param>
    /// <param name="card">The card.</param>
    /// <param name="e">The press.</param>
    private void PressCard(Border element, PileCardViewModel card, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _cardPress = (card.CaseNumber, e.GetPosition(Fan));
        _dragged = [];
        element.CaptureMouse();
    }

    /// <summary>Drags the pressed card, with the chosen ones under it, once the pointer has moved far enough.</summary>
    /// <param name="element">The card's element.</param>
    /// <param name="e">The move.</param>
    private void DragCards(Border element, MouseEventArgs e)
    {
        if (_cardPress is not { } press || !element.IsMouseCaptured)
        {
            return;
        }

        var at = e.GetPosition(Fan);
        if (_dragged.Count == 0 && !Moved(press.At, at))
        {
            return;
        }

        if (_dragged.Count == 0 && _model is not null)
        {
            _dragged = _model.CardsToDrag(press.CaseNumber);
        }

        for (var index = 0; index < _dragged.Count; index++)
        {
            if (_cards.TryGetValue(_dragged[index], out var card))
            {
                Fan.Move(card, new Point(at.X, at.Y + (index * DraggedStep)));
            }
        }
    }

    /// <summary>
    /// Lets go of a card: dragged out onto the dimmed hub it and the cards with it lie loose on the start page; dropped
    /// on the pile they go back; a click opens the case, and Shift-click chooses it.
    /// </summary>
    /// <param name="element">The card's element.</param>
    /// <param name="card">The card.</param>
    /// <param name="e">The release.</param>
    private void ReleaseCard(Border element, PileCardViewModel card, MouseButtonEventArgs e)
    {
        if (_cardPress is null)
        {
            return;
        }

        e.Handled = true;
        var dragged = _dragged;
        var at = e.GetPosition(Fan);
        ForgetCardPress();
        element.ReleaseMouseCapture();
        if (dragged.Count > 0)
        {
            DropCards(dragged, at);
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            _model?.ToggleCard(card.CaseNumber);
        }
        else if (_model is { } model)
        {
            model.ClosePile();
            model.OpenCaseCommand.Execute(card.Case);
        }
    }

    /// <summary>Drops dragged cards: back into the pile when let go on it, else out onto the start page.</summary>
    /// <param name="dragged">The cards' cases.</param>
    /// <param name="at">Where they were let go, in the fan.</param>
    private void DropCards(IReadOnlyList<string> dragged, Point at)
    {
        if (Fan.CentreBounds.Contains(at))
        {
            foreach (var number in dragged)
            {
                if (_cards.TryGetValue(number, out var card))
                {
                    Fan.Return(card);
                }
            }

            return;
        }

        _model?.TakeOut([.. dragged]);
    }

    /// <summary>Forgets a press on a card.</summary>
    private void ForgetCardPress()
    {
        _cardPress = null;
        _dragged = [];
    }

    /// <summary>
    /// The piles' keys: Esc puts a lifted case back or folds the fan, and Enter takes the chosen cards out, as in the
    /// graph's fan.
    /// </summary>
    /// <param name="sender">The view.</param>
    /// <param name="e">The key.</param>
    private void OnPileKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox || _model is not { } model)
        {
            return;
        }

        if (e.Key == Key.Escape && _lift is not null)
        {
            e.Handled = true;
            CancelLift();
        }
        else if (e.Key == Key.Escape && model.IsPileOpen)
        {
            e.Handled = true;
            model.ClosePile();
        }
        else if (e.Key == Key.Enter && model.IsPileOpen)
        {
            e.Handled = true;
            model.TakeOut([.. model.OpenCards.Where(card => card.IsSelected).Select(card => card.CaseNumber)]);
        }
    }
}
