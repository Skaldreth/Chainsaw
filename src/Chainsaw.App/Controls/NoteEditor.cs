using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Chainsaw.SharedKernel.Text;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;

namespace Chainsaw.App.Controls;

/// <summary>
/// Where a comment is written, directly in its note (sketch KN1a): AvalonEdit with the note's face, wrapping and no
/// scroll bars, so that the note grows with its text, and the Markdown coloured as it is typed
/// (<see cref="NoteColorizer"/>).
/// Esc and Ctrl+Enter ask to leave the note; Enter in a list goes on with the list, and Tab and Shift+Tab move lines a
/// level in or out (<see cref="MarkdownEdits"/>). The lines stand as far apart as when the note is drawn, and the wheel
/// goes on to the graph, which zooms, since the note never scrolls. Links are never followed from here.
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
            Spaced((FontFamily)FindResource("Font.Mono"))));

        FontFamily = Spaced((FontFamily)FindResource("Font.Ui"));
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
        Options.IndentationSize = MarkdownEdits.IndentUnit.Length;

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

        // The editor's own scroller takes every turn of the wheel, also with nothing to scroll; the note grows with its
        // text instead, so the turn goes on to the graph, which zooms as it does over a drawn note.
        AddHandler(MouseWheelEvent, new MouseWheelEventHandler((_, e) => e.Handled = false), handledEventsToo: true);
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

    /// <summary>
    /// Gives a face whose lines stand as far apart as the drawn note's (sketch KN1a: 1.55), with the extra space shared
    /// above and below the text as a drawn line shares it, so that the text does not move when the note is left.
    /// </summary>
    /// <param name="face">The theme's face, which may name fallbacks.</param>
    /// <returns>A composite face over it; the face itself when its spacing cannot be read.</returns>
    private static FontFamily Spaced(FontFamily face)
    {
        if (face.LineSpacing <= 0)
        {
            return face;
        }

        var share = NoteMarkdownView.LineShare;
        var spaced = new FontFamily
        {
            LineSpacing = share,
            Baseline = face.Baseline * share / face.LineSpacing,
        };
        spaced.FamilyMaps.Add(new FontFamilyMap { Target = face.Source });
        return spaced;
    }

    /// <summary>
    /// Esc and Ctrl+Enter leave the note; Enter in a list, Tab and Shift+Tab are Markdown's; the rest is the editor's.
    /// </summary>
    /// <param name="sender">The text area.</param>
    /// <param name="e">The key.</param>
    private void OnPreviewKey(object sender, KeyEventArgs e)
    {
        switch (e.Key, Keyboard.Modifiers)
        {
            case (Key.Escape, ModifierKeys.None) or (Key.Enter, ModifierKeys.Control):
                e.Handled = true;
                LeaveWanted?.Invoke(this, EventArgs.Empty);
                break;
            case (Key.Enter, ModifierKeys.None) when SelectionLength == 0:
                var newLine = TextUtilities.GetNewLineFromDocument(Document, TextArea.Caret.Line);
                e.Handled = Apply(MarkdownEdits.ContinueList(Document.Text, CaretOffset, newLine));
                break;
            case (Key.Tab, ModifierKeys.None):
                e.Handled = Apply(MarkdownEdits.Indent(Document.Text, SelectionStart, SelectionLength, outdent: false));
                break;
            case (Key.Tab, ModifierKeys.Shift):
                // Shift+Tab never takes the keyboard out of the note, also when there is nothing to move.
                Apply(MarkdownEdits.Indent(Document.Text, SelectionStart, SelectionLength, outdent: true));
                e.Handled = true;
                break;
        }
    }

    /// <summary>Applies an edit as one step, so that Ctrl+Z undoes it at once, and selects what it says.</summary>
    /// <param name="edit">The edit, or <see langword="null"/> when the key does what it always does.</param>
    /// <returns><see langword="true"/> when the edit was applied.</returns>
    private bool Apply(TextEdit? edit)
    {
        if (edit is not { } change || IsReadOnly)
        {
            return false;
        }

        Document.Replace(change.Offset, change.Length, change.Text);
        Select(change.SelectionStart, change.SelectionLength);
        TextArea.Caret.BringCaretToView();
        return true;
    }
}
