using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Rectangle = System.Windows.Shapes.Rectangle;
using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Controls;

/// <summary>
/// One comment on the surface (sketch KN1a): a sheet in a faint warm tone with a folded corner, its Markdown drawn, or
/// written directly in it with the marks dim, its foot (who and when, or how to leave it) and the grip in its lower
/// right corner on hovering or when chosen. A click writes in it, a drag moves it, the grip sizes it; an empty new one
/// is a dashed outline with a hint.
/// </summary>
internal sealed class CommentNoteView : Grid
{
    /// <summary>The sheet's corners (sketch KN1a: 3, 14, 6 and 6 px).</summary>
    private static readonly CornerRadius Corners = new(3, 14, 6, 6);

    /// <summary>The space inside the sheet (sketch KN1a: 14 px over, 16 at the sides, 22 under for the foot).</summary>
    private static readonly Thickness Inside = new(16, 14, 16, 22);

    /// <summary>The folded corner's side (sketch KN1a: 16 px).</summary>
    private const double FoldSide = 16;

    /// <summary>The frame's width (sketch KN1a: 1.5 px).</summary>
    private const double FrameWidth = 1.5;

    /// <summary>How far the pointer moves before a press on the sheet becomes a drag, in the surface's units.</summary>
    private const double DragThreshold = 3;

    /// <summary>What an empty new note says (sketch KN1a).</summary>
    private const string Hint = "Skriv …   # **fet** - lista";

    /// <summary>The note.</summary>
    private readonly CommentNote _note;

    /// <summary>The comments, which store what is done to the note.</summary>
    private readonly CommentsViewModel _comments;

    /// <summary>The layer the note stands in, whose units (the surface's) the drags are measured in.</summary>
    private readonly UIElement _layer;

    /// <summary>The graph's surface, which takes the keyboard back when the note is left with Esc.</summary>
    private readonly UIElement _surface;

    /// <summary>The sheet: its ground, frame and content.</summary>
    private readonly Border _sheet;

    /// <summary>The soft shadow under the sheet, apart so that the text stays sharp.</summary>
    private readonly Border _shadow;

    /// <summary>The dashed outline of an empty new note.</summary>
    private readonly Rectangle _outline;

    /// <summary>The folded corner.</summary>
    private readonly Canvas _fold;

    /// <summary>The drawn Markdown.</summary>
    private readonly NoteMarkdownView _view;

    /// <summary>The hint in an empty new note.</summary>
    private readonly TextBlock _hint;

    /// <summary>Who and when, or how to leave the note.</summary>
    private readonly TextBlock _foot;

    /// <summary>The grip that sizes the note.</summary>
    private readonly Border _grip;

    /// <summary>The editor while the note is written in, or <see langword="null"/>.</summary>
    private NoteEditor? _editor;

    /// <summary>Where a press on the sheet or the grip began, on the surface, or <see langword="null"/>.</summary>
    private Point? _pressedAt;

    /// <summary>The note's place or size when the press began.</summary>
    private Rect _pressedFrame;

    /// <summary>Whether the press holds the grip, and not the sheet.</summary>
    private bool _sizing;

    /// <summary>Whether the press has moved far enough to be a drag.</summary>
    private bool _dragged;

    /// <summary>The paper's shadow, made and frozen once and shared by every note.</summary>
    private static readonly DropShadowEffect Shadow = Freezing.Frozen(new DropShadowEffect
    {
        BlurRadius = 26, ShadowDepth = 10, Direction = 270, Opacity = 0.4, Color = Colors.Black,
    });

    /// <summary>Creates the view of a note.</summary>
    /// <param name="note">The note.</param>
    /// <param name="comments">The comments.</param>
    /// <param name="layer">The layer the note stands in, in the surface's units.</param>
    /// <param name="surface">The graph's surface.</param>
    /// <param name="linkCommand">What a click on a <c>chainsaw:</c> link runs.</param>
    public CommentNoteView(
        CommentNote note, CommentsViewModel comments, UIElement layer, UIElement surface, ICommand linkCommand)
    {
        _note = note;
        _comments = comments;
        _layer = layer;
        _surface = surface;
        Cursor = Cursors.Hand;
        _shadow = new Border
        {
            CornerRadius = Corners,
            Background = Brush("Brush.Note.Paper"),
            IsHitTestVisible = false,
            Effect = Shadow,

            // The blurred shadow is drawn once into a bitmap and moved with the pan, instead of blurred again on every
            // frame of a pan with many notes (BACKLOG, Kommentarsnoden).
            CacheMode = new BitmapCache(),
        };
        _view = new NoteMarkdownView { LinkCommand = linkCommand };
        _hint = new TextBlock
        {
            Text = Hint, FontSize = 13, Foreground = Brush("Brush.TextDim"), IsHitTestVisible = false,
        };
        _sheet = new Border
        {
            CornerRadius = Corners,
            BorderThickness = new Thickness(FrameWidth),
            Padding = Inside,
            Background = Brush("Brush.Note.Paper"),
            Child = new Grid { Children = { _view, _hint } },
        };
        _outline = new Rectangle
        {
            Stroke = Brush("Brush.Note.Draft"),
            StrokeThickness = FrameWidth,
            StrokeDashArray = [4, 3],
            RadiusX = 6,
            RadiusY = 6,
            IsHitTestVisible = false,
        };
        _fold = Fold();
        _foot = new TextBlock
        {
            FontSize = 10.5,
            Foreground = Brush("Brush.TextDim"),
            Margin = new Thickness(12, 0, 24, 5),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false,
        };
        _grip = Grip();
        Children.Add(_shadow);
        Children.Add(_sheet);
        Children.Add(_outline);
        Children.Add(_fold);
        Children.Add(_foot);
        Children.Add(_grip);
        note.PropertyChanged += OnNoteChanged;
        MouseEnter += (_, _) => ShowChrome();
        MouseLeave += (_, _) => ShowChrome();
        Show();
    }

    /// <summary>Gets the note shown.</summary>
    public CommentNote Note => _note;

    /// <summary>Stops following the note, when it leaves the surface.</summary>
    public void Detach()
    {
        _note.PropertyChanged -= OnNoteChanged;
        CloseEditor();
    }

    /// <summary>Takes hold of the sheet, to move the note or, on a click, to write in it.</summary>
    /// <param name="e">The press.</param>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);
        if (e.Handled)
        {
            return;
        }

        Press(e, sizing: false);
    }

    /// <summary>
    /// Keeps a right press in the editor for its own menu, so that the graph does not take it to pan; elsewhere on the
    /// note it goes on to the graph, which pans over notes as over nodes.
    /// </summary>
    /// <param name="e">The press.</param>
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseRightButtonDown(e);
        if (_editor is { IsMouseOver: true })
        {
            e.Handled = true;
        }
    }

    /// <summary>Moves or sizes the note while a press holds it.</summary>
    /// <param name="e">The move.</param>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        if (_pressedAt is not { } from || !IsMouseCaptured)
        {
            return;
        }

        var at = e.GetPosition(_layer);
        var delta = at - from;
        if (!_dragged && delta.Length < DragThreshold)
        {
            return;
        }

        _dragged = true;
        if (_sizing)
        {
            _comments.Resize(_note, _pressedFrame.Width + delta.X, _pressedFrame.Height + delta.Y);
            return;
        }

        _comments.Move(_note, new GraphPoint(_pressedFrame.X + delta.X, _pressedFrame.Y + delta.Y));
        if (_surface is GraphCanvas canvas)
        {
            canvas.NoteDraggedAt(e.GetPosition(canvas));
        }
    }

    /// <summary>Lets go: a drag is stored, and a click on the sheet writes in the note.</summary>
    /// <param name="e">The release.</param>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonUp(e);
        if (_pressedAt is null)
        {
            return;
        }

        var dragged = _dragged;
        var sizing = _sizing;
        _pressedAt = null;
        _dragged = false;
        ReleaseMouseCapture();
        e.Handled = true;
        if (dragged && !sizing && _surface is GraphCanvas canvas && canvas.DropNoteOnBin(_note))
        {
            // Thrown in the trash: it stays where it stood, hidden, and comes back there when put back.
            _comments.Move(_note, new GraphPoint(_pressedFrame.X, _pressedFrame.Y));
        }
        else if (dragged)
        {
            _ = _comments.DropAsync(_note);
        }
        else if (!sizing)
        {
            _comments.Edit(_note);
        }
    }

    /// <summary>Starts a press on the sheet or on the grip.</summary>
    /// <param name="e">The press.</param>
    /// <param name="sizing">Whether the grip was pressed.</param>
    private void Press(MouseButtonEventArgs e, bool sizing)
    {
        _comments.Select(_note);
        _pressedAt = e.GetPosition(_layer);
        _pressedFrame = new Rect(_note.Place.X, _note.Place.Y, ActualWidth, ActualHeight);
        _sizing = sizing;
        _dragged = false;
        CaptureMouse();
        e.Handled = true;
    }

    /// <summary>Follows the note: its text, its size, and whether it is chosen or written in.</summary>
    /// <param name="sender">The note.</param>
    /// <param name="e">What changed.</param>
    private void OnNoteChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CommentNote.IsEditing) when _note.IsEditing:
                OpenEditor();
                Show();
                break;
            case nameof(CommentNote.IsEditing):
                CloseEditor();
                Show();
                break;
            case nameof(CommentNote.Writing):
                // A text given back after a failed store is put in the editor; what the editor wrote is there already.
                if (_editor is not null && _editor.Text != _note.Writing)
                {
                    _editor.Text = _note.Writing;
                }

                ShowEmpty();
                break;
            default:
                Show();
                break;
        }
    }

    /// <summary>Shows the note as it is now.</summary>
    private void Show()
    {
        // A note whose node lies in the trash is hidden with it, and comes back when the node is put back.
        Visibility = _note.IsInTrash ? Visibility.Collapsed : Visibility.Visible;
        Width = _note.Width;
        MinHeight = _note.MinimumHeight;
        _view.Markdown = _note.Text;
        _view.Visibility = _editor is null ? Visibility.Visible : Visibility.Collapsed;
        _foot.Text = _note.Foot;
        ShowEmpty();
        ShowChrome();
    }

    /// <summary>Shows an empty new note as a dashed outline with a hint, and a written one as a sheet.</summary>
    private void ShowEmpty()
    {
        var empty = _note.IsDraft && _note.Writing.Length == 0;
        _hint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        _outline.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        _sheet.Background = empty ? Brushes.Transparent : Brush("Brush.Note.Paper");
        _shadow.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        _fold.Visibility = _shadow.Visibility;
        ShowChrome();
    }

    /// <summary>
    /// Shows the frame, the foot and the grip: lighter while the note is chosen or written in, and the foot and the
    /// grip only on hovering or then (principle 7).
    /// </summary>
    private void ShowChrome()
    {
        var lit = _note.IsSelected || _note.IsEditing;
        var empty = _note.IsDraft && _note.Writing.Length == 0;
        _sheet.BorderBrush = empty ? Brushes.Transparent : Brush(lit ? "Brush.TextMuted" : "Brush.Note.Edge");
        var chrome = (lit || IsMouseOver) && !empty ? Visibility.Visible : Visibility.Collapsed;
        _foot.Visibility = chrome;
        _grip.Visibility = chrome;
        Cursor = _note.IsEditing ? Cursors.IBeam : Cursors.Hand;
    }

    /// <summary>Puts the editor in the sheet, with the note's text, and the keyboard in it.</summary>
    private void OpenEditor()
    {
        if (_editor is not null)
        {
            return;
        }

        _editor = new NoteEditor { Text = _note.Writing };
        _editor.TextChanged += (_, _) => _note.Writing = _editor?.Text ?? string.Empty;
        _editor.LeaveWanted += (_, _) => Leave(focusSurface: true);
        _editor.IsKeyboardFocusWithinChanged += (_, e) =>
        {
            // The keyboard that goes to the editor's own menu has not left the note.
            if (e.NewValue is false && _editor?.ContextMenu?.IsOpen != true)
            {
                Leave(focusSurface: false);
            }
        };
        ((Grid)_sheet.Child).Children.Add(_editor);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () => _editor?.FocusAtEnd());
    }

    /// <summary>Leaves the note: what it says is stored, and the keyboard goes back to the graph when asked.</summary>
    /// <param name="focusSurface">Whether the graph takes the keyboard.</param>
    private void Leave(bool focusSurface)
    {
        if (!_note.IsEditing)
        {
            return;
        }

        _ = _comments.LeaveAsync(_note);
        if (focusSurface)
        {
            _surface.Focus();
        }
    }

    /// <summary>Takes the editor out of the sheet.</summary>
    private void CloseEditor()
    {
        if (_editor is null)
        {
            return;
        }

        ((Grid)_sheet.Child).Children.Remove(_editor);
        _editor = null;
    }

    /// <summary>Makes the folded corner: the surface's ground over the corner and the fold under it.</summary>
    /// <returns>The corner.</returns>
    private Canvas Fold()
    {
        var cut = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M0,0 L16,0 L16,16 Z"),
            Fill = Brush("Brush.Background"),
        };
        var fold = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M0,0 L16,16 L0,16 Z"),
            Fill = Brush("Brush.Note.Edge"),
        };
        return new Canvas
        {
            Width = FoldSide,
            Height = FoldSide,
            Margin = new Thickness(0, -FrameWidth, -FrameWidth, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Children = { cut, fold },
        };
    }

    /// <summary>Makes the grip in the lower right corner that drags the note to its own size.</summary>
    /// <returns>The grip.</returns>
    private Border Grip()
    {
        var grip = new Border
        {
            Width = 18,
            Height = 18,
            Margin = new Thickness(0, 0, 2, 2),
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE,
            ToolTip = "Dra till egen storlek",
            Child = new GridIcon
            {
                Size = 12,
                Data = (Geometry)FindResource("Icon.Resize"),
                Foreground = Brush("Brush.TextMuted"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
            },
        };
        grip.MouseLeftButtonDown += (_, e) => Press(e, sizing: true);
        return grip;
    }

    /// <summary>Finds a brush of the theme.</summary>
    /// <param name="key">Its key.</param>
    /// <returns>The brush.</returns>
    private Brush Brush(string key) => (Brush)FindResource(key);
}
