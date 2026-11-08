using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Chainsaw.App.Links;
using Chainsaw.SharedKernel.Text;

namespace Chainsaw.App.Controls;

/// <summary>
/// Draws a comment's Markdown on its note (sketch KN1a), after Gawk's view of a note: headings, bullets and numbered
/// items, quotes, code, bold, italic and links. A <c>chainsaw:</c> link is drawn with a dotted underline and runs
/// <see cref="LinkCommand"/> with its address when clicked; any other address is drawn as text and never followed,
/// since reaching it from here would go past the exit (principle 4).
/// </summary>
public sealed class NoteMarkdownView : StackPanel
{
    /// <summary>Identifies <see cref="Markdown"/>.</summary>
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown), typeof(string), typeof(NoteMarkdownView),
        new PropertyMetadata(null, (d, _) => ((NoteMarkdownView)d).Rebuild()));

    /// <summary>Identifies <see cref="LinkCommand"/>.</summary>
    public static readonly DependencyProperty LinkCommandProperty = DependencyProperty.Register(
        nameof(LinkCommand), typeof(ICommand), typeof(NoteMarkdownView),
        new PropertyMetadata(null, (d, _) => ((NoteMarkdownView)d).Rebuild()));

    /// <summary>The body's size (sketch KN1a: 13.5 px).</summary>
    private const double BodySize = 13.5;

    /// <summary>A heading's size (sketch KN1a: 15 px).</summary>
    private const double HeadingSize = 15;

    /// <summary>The code's size (sketch KN1a: 12 px).</summary>
    private const double CodeSize = 12;

    /// <summary>The line height as a share of the size (sketch KN1a: 1.55).</summary>
    private const double LineShare = 1.55;

    /// <summary>How far a bullet or numbered item steps in (sketch KN1a: 14 px), and each level more.</summary>
    private const double ItemIndent = 14;

    /// <summary>The space under a heading (sketch KN1a: 4 px).</summary>
    private const double HeadingGap = 4;

    /// <summary>The theme's bright text.</summary>
    private const string TextBrush = "Brush.Text";

    /// <summary>The theme's muted text, for quotes and addresses that are not followed.</summary>
    private const string MutedBrush = "Brush.TextMuted";

    /// <summary>The note's dim marks, for a quote's bar and a bullet.</summary>
    private const string MarkBrush = "Brush.Note.Mark";

    /// <summary>The note's code ground.</summary>
    private const string CodeBrush = "Brush.Note.Code";

    /// <summary>The dotted underline of a link that is followed (sketch KN1a), made once it is needed.</summary>
    private TextDecorationCollection? _dotted;

    /// <summary>Gets or sets the Markdown to draw.</summary>
    public string? Markdown
    {
        get => (string?)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>Gets or sets what a click on a <c>chainsaw:</c> link runs, with the link's address.</summary>
    public ICommand? LinkCommand
    {
        get => (ICommand?)GetValue(LinkCommandProperty);
        set => SetValue(LinkCommandProperty, value);
    }

    /// <summary>Draws the blocks again from the text.</summary>
    private void Rebuild()
    {
        Children.Clear();
        foreach (var block in SharedKernel.Text.Markdown.Parse(Markdown))
        {
            Children.Add(CreateBlock(block));
        }
    }

    /// <summary>Draws one block: a heading, an item, a quote, code or a paragraph.</summary>
    /// <param name="block">The block.</param>
    /// <returns>Its element.</returns>
    private FrameworkElement CreateBlock(MarkdownBlock block)
    {
        if (block.Kind == MarkdownBlockKind.Code)
        {
            return CreateCodeBlock(block);
        }

        var isHeading = block.Kind is MarkdownBlockKind.Heading or MarkdownBlockKind.Subheading;
        var size = isHeading ? HeadingSize : BodySize;
        var text = new TextBlock
        {
            FontSize = size,
            LineHeight = Math.Round(size * LineShare),
            TextWrapping = TextWrapping.Wrap,
            FontWeight = isHeading ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = Brush(block.Kind == MarkdownBlockKind.Quote ? MutedBrush : TextBrush),
            Margin = new Thickness(0, 0, 0, isHeading ? HeadingGap : 0),
        };
        foreach (var span in block.Spans)
        {
            text.Inlines.Add(CreateInline(span));
        }

        return block.Kind switch
        {
            MarkdownBlockKind.Bullet or MarkdownBlockKind.Numbered => CreateItem(block, text),
            MarkdownBlockKind.Quote => new Border
            {
                Padding = new Thickness(10, 0, 0, 0),
                BorderThickness = new Thickness(2, 0, 0, 0),
                BorderBrush = Brush(MarkBrush),
                Child = text,
            },
            _ => text,
        };
    }

    /// <summary>Draws a bullet or a numbered item: its mark, then its text, stepped in by its level.</summary>
    /// <param name="block">The item.</param>
    /// <param name="text">Its text.</param>
    /// <returns>The item.</returns>
    private DockPanel CreateItem(MarkdownBlock block, TextBlock text)
    {
        var marker = new TextBlock
        {
            Text = block.Marker ?? "•",
            FontSize = BodySize,
            LineHeight = text.LineHeight,
            Foreground = Brush(block.Marker is null ? TextBrush : MutedBrush),
            Margin = new Thickness(0, 0, 6, 0),
        };
        DockPanel.SetDock(marker, Dock.Left);
        return new DockPanel
        {
            Margin = new Thickness(ItemIndent * (block.Level + 1), 0, 0, 0),
            Children = { marker, text },
        };
    }

    /// <summary>Draws fenced lines as typed, in mono on the note's code ground; they wrap rather than scroll.</summary>
    /// <param name="block">The block.</param>
    /// <returns>Its element.</returns>
    private Border CreateCodeBlock(MarkdownBlock block) => new()
    {
        Margin = new Thickness(0, 2, 0, 4),
        Padding = new Thickness(8, 5, 8, 5),
        CornerRadius = new CornerRadius(4),
        Background = Brush(CodeBrush),
        Child = new TextBlock
        {
            Text = block.PlainText,
            FontFamily = (FontFamily)FindResource("Font.Mono"),
            FontSize = CodeSize,
            LineHeight = Math.Round(CodeSize * LineShare),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(TextBrush),
        },
    };

    /// <summary>Draws a piece of a line: bold, italic, code, and a link that is followed or only shown.</summary>
    /// <param name="span">The piece.</param>
    /// <returns>Its inline.</returns>
    private Inline CreateInline(MarkdownSpan span)
    {
        // A run has no padding, so thin spaces give the code's ground a little room on either side.
        var isCode = span.Style.HasFlag(MarkdownStyle.Code);
        var run = new Run(isCode ? $" {span.Text} " : span.Text);
        if (span.Style.HasFlag(MarkdownStyle.Bold))
        {
            run.FontWeight = FontWeights.SemiBold;
        }

        if (span.Style.HasFlag(MarkdownStyle.Italic))
        {
            run.FontStyle = FontStyles.Italic;
        }

        if (isCode)
        {
            run.FontFamily = (FontFamily)FindResource("Font.Mono");
            run.FontSize = CodeSize;
            run.Background = Brush(CodeBrush);
        }

        if (span.Target is not { } target)
        {
            return run;
        }

        if (!IsFollowed(target))
        {
            run.Foreground = Brush(MutedBrush);
            return new Span(run) { ToolTip = target + " · följs inte från Chainsaw" };
        }

        run.TextDecorations = _dotted ??= DottedLine(Brush(TextBrush));
        var link = new Span(run) { Cursor = Cursors.Hand, ToolTip = target };
        link.MouseLeftButtonDown += (_, e) =>
        {
            if (LinkCommand is { } command && command.CanExecute(target))
            {
                command.Execute(target);
            }

            e.Handled = true;
        };
        return link;
    }

    /// <summary>Reads whether an address is followed: only a <c>chainsaw:</c> link to a start point.</summary>
    /// <param name="target">The address.</param>
    /// <returns><see langword="true"/> when it is followed.</returns>
    public static bool IsFollowed(string target) => ChainsawLink.TryParse(target, out _);

    /// <summary>Finds a brush of the theme.</summary>
    /// <param name="key">Its key.</param>
    /// <returns>The brush.</returns>
    private Brush Brush(string key) => (Brush)FindResource(key);

    /// <summary>Makes the dotted underline of a followed link, frozen.</summary>
    /// <param name="brush">The text's colour.</param>
    /// <returns>The decorations.</returns>
    private static TextDecorationCollection DottedLine(Brush brush)
    {
        var pen = new Pen(brush, 1) { DashStyle = new DashStyle([1, 2], 0) };
        pen.Freeze();
        var underline = new TextDecoration(
            TextDecorationLocation.Underline, pen, 3, TextDecorationUnit.Pixel, TextDecorationUnit.Pixel);
        var decorations = new TextDecorationCollection { underline };
        decorations.Freeze();
        return decorations;
    }
}
