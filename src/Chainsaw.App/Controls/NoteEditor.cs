using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;

namespace Chainsaw.App.Controls;

/// <summary>
/// Where a comment is written, directly in its note (sketch KN1a): AvalonEdit with the note's face, wrapping and no
/// scroll bars, so that the note grows with its text, and the Markdown coloured as it is typed
/// (<see cref="NoteColorizer"/>).
/// Esc and Ctrl+Enter ask to leave the note. Links are never followed from here.
/// </summary>
internal sealed class NoteEditor : TextEditor
{
    /// <summary>The text's size (sketch KN1a: 13.5 px).</summary>
    private const double TextSize = 13.5;

    /// <summary>How opaque the selection's accent is.</summary>
    private const byte SelectionAlpha = 0x55;

    /// <summary>Colours the Markdown.</summary>
    private readonly NoteColorizer _colorizer;

    /// <summary>Creates the editor with the theme's faces and colours.</summary>
    public NoteEditor()
    {
        var text = (Brush)FindResource("Brush.Text");
        var accent = (Color)FindResource("Color.Accent");
        _colorizer = new NoteColorizer(new NoteInks(
            (Brush)FindResource("Brush.Note.Mark"),
            text,
            (Brush)FindResource("Brush.Note.Code"),
            (FontFamily)FindResource("Font.Mono")));

        FontFamily = (FontFamily)FindResource("Font.Ui");
        FontSize = TextSize;
        Foreground = text;
        Background = Brushes.Transparent;
        Padding = new Thickness(0);
        WordWrap = true;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        FocusVisualStyle = null;

        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Options.AllowScrollBelowDocument = false;
        Options.HighlightCurrentLine = false;
        Options.EnableRectangularSelection = false;
        Options.ConvertTabsToSpaces = true;

        TextArea.TextView.LineTransformers.Add(_colorizer);
        TextArea.Caret.CaretBrush = text;
        var selection = new SolidColorBrush(Color.FromArgb(SelectionAlpha, accent.R, accent.G, accent.B));
        selection.Freeze();
        TextArea.SelectionBrush = selection;
        TextArea.SelectionBorder = null;
        TextArea.SelectionForeground = null;
        TextArea.SelectionCornerRadius = 2;
        TextArea.FocusVisualStyle = null;
        TextArea.PreviewKeyDown += OnPreviewKey;
        Document.TextChanged += (_, _) =>
        {
            // A fence changes every line below it, which AvalonEdit would not draw again by itself.
            if (_colorizer.UpdateCodeBlocks(Document))
            {
                TextArea.TextView.Redraw();
            }
        };

        ContextMenu = new ContextMenu
        {
            Items =
            {
                new MenuItem { Header = "Klipp ut", InputGestureText = "Ctrl+X", Command = ApplicationCommands.Cut },
                new MenuItem { Header = "Kopiera", InputGestureText = "Ctrl+C", Command = ApplicationCommands.Copy },
                new MenuItem
                {
                    Header = "Klistra in", InputGestureText = "Ctrl+V", Command = ApplicationCommands.Paste,
                },
                new Separator(),
                new MenuItem
                {
                    Header = "Markera allt", InputGestureText = "Ctrl+A", Command = ApplicationCommands.SelectAll,
                },
            },
        };
    }

    /// <summary>Raised when Esc or Ctrl+Enter asks to leave the note.</summary>
    public event EventHandler? LeaveWanted;

    /// <summary>Puts the keyboard in the editor with the caret at the end of the text.</summary>
    public void FocusAtEnd()
    {
        Focus();
        Keyboard.Focus(TextArea);
        CaretOffset = Document.TextLength;
    }

    /// <summary>Esc and Ctrl+Enter leave the note; the rest is the editor's.</summary>
    /// <param name="sender">The text area.</param>
    /// <param name="e">The key.</param>
    private void OnPreviewKey(object sender, KeyEventArgs e)
    {
        var leaves = (e.Key, Keyboard.Modifiers) is (Key.Escape, ModifierKeys.None)
            or (Key.Enter, ModifierKeys.Control);
        if (!leaves)
        {
            return;
        }

        e.Handled = true;
        LeaveWanted?.Invoke(this, EventArgs.Empty);
    }
}
