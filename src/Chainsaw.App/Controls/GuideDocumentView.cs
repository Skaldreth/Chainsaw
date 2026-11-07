using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Chainsaw.App.Guide;

namespace Chainsaw.App.Controls;

/// <summary>
/// Draws a section of the Guide (sketches GD1a and GD1b): the title, the blocks read by
/// <see cref="GuideMarkdown"/>, the marked heading with the accent at its left, and <em>Läs också</em> under the
/// text. Links run <see cref="LinkCommand"/> with their <see cref="GuideLink"/>. When the section or the mark changes,
/// the nearest scroll viewer is brought to the marked heading, or to the top.
/// </summary>
public sealed class GuideDocumentView : StackPanel
{
    /// <summary>Identifies <see cref="Section"/>.</summary>
    public static readonly DependencyProperty SectionProperty = DependencyProperty.Register(
        nameof(Section), typeof(GuideSection), typeof(GuideDocumentView), new PropertyMetadata(null, OnChanged));

    /// <summary>Identifies <see cref="Anchor"/>.</summary>
    public static readonly DependencyProperty AnchorProperty = DependencyProperty.Register(
        nameof(Anchor), typeof(string), typeof(GuideDocumentView), new PropertyMetadata(null, OnChanged));

    /// <summary>Identifies <see cref="Compact"/>.</summary>
    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact), typeof(bool), typeof(GuideDocumentView), new PropertyMetadata(false, OnChanged));

    /// <summary>Identifies <see cref="Library"/>.</summary>
    public static readonly DependencyProperty LibraryProperty = DependencyProperty.Register(
        nameof(Library), typeof(GuideLibrary), typeof(GuideDocumentView), new PropertyMetadata(null, OnChanged));

    /// <summary>Identifies <see cref="LinkCommand"/>.</summary>
    public static readonly DependencyProperty LinkCommandProperty = DependencyProperty.Register(
        nameof(LinkCommand), typeof(ICommand), typeof(GuideDocumentView), new PropertyMetadata(null, OnChanged));

    /// <summary>How far a nested list moves in per level.</summary>
    private const double ListIndent = 18;

    /// <summary>The space kept above the marked heading when it is scrolled to.</summary>
    private const double ScrollMargin = 12;

    /// <summary>The marked heading's element, once drawn.</summary>
    private FrameworkElement? _marked;

    /// <summary>Gets or sets the section to draw.</summary>
    public GuideSection? Section
    {
        get => (GuideSection?)GetValue(SectionProperty);
        set => SetValue(SectionProperty, value);
    }

    /// <summary>Gets or sets the marked heading's anchor, or <see langword="null"/>.</summary>
    public string? Anchor
    {
        get => (string?)GetValue(AnchorProperty);
        set => SetValue(AnchorProperty, value);
    }

    /// <summary>Gets or sets whether the text is drawn for the narrow panel (smaller) or for the view.</summary>
    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    /// <summary>Gets or sets the library, for the titles under <em>Läs också</em>.</summary>
    public GuideLibrary? Library
    {
        get => (GuideLibrary?)GetValue(LibraryProperty);
        set => SetValue(LibraryProperty, value);
    }

    /// <summary>Gets or sets the command a link runs, with its <see cref="GuideLink"/>.</summary>
    public ICommand? LinkCommand
    {
        get => (ICommand?)GetValue(LinkCommandProperty);
        set => SetValue(LinkCommandProperty, value);
    }

    /// <summary>Gets the body text's size.</summary>
    private double BodySize => Compact ? 13.5 : 14;

    /// <summary>Draws anew when anything changes.</summary>
    /// <param name="d">The view.</param>
    /// <param name="e">The change.</param>
    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((GuideDocumentView)d).Build();

    /// <summary>Draws the section and scrolls to its mark.</summary>
    private void Build()
    {
        Children.Clear();
        _marked = null;
        if (Section is not { } section)
        {
            return;
        }

        Children.Add(new TextBlock
        {
            Text = section.Title,
            FontSize = Compact ? 20 : 26,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("Brush.Text"),
            TextWrapping = TextWrapping.Wrap,
        });
        foreach (var block in section.Document.Blocks)
        {
            Children.Add(Draw(block));
        }

        if (Related(section) is { } related)
        {
            Children.Add(related);
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ScrollToMark);
    }

    /// <summary>Draws a block.</summary>
    /// <param name="block">The block.</param>
    /// <returns>Its element.</returns>
    private FrameworkElement Draw(GuideBlock block) => block switch
    {
        GuideHeading heading => Heading(heading),
        GuideParagraph paragraph => Text(paragraph.Runs, new Thickness(0, 10, 0, 0)),
        GuideQuote quote => Quote(quote),
        GuideCode code => Code(code),
        GuideList list => List(list),
        GuideTable table => Table(table),
        _ => new FrameworkElement(),
    };

    /// <summary>Draws a heading; the marked one gets the accent at its left (sketch GD1b).</summary>
    /// <param name="heading">The heading.</param>
    /// <returns>Its element.</returns>
    private Border Heading(GuideHeading heading)
    {
        var marked = heading.Anchor == Anchor;
        var text = Text(heading.Runs, default);
        text.FontSize = heading.Level == 2 ? BodySize + 1.5 : BodySize + 0.5;
        text.FontWeight = FontWeights.SemiBold;
        text.Foreground = Brush("Brush.Text");
        var border = new Border
        {
            Child = text,
            Margin = new Thickness(0, 26, 0, 0),
            Padding = new Thickness(marked ? 10 : 0, 0, 0, 0),
            BorderThickness = new Thickness(marked ? 2 : 0, 0, 0, 0),
            BorderBrush = Brush("Brush.Accent"),
        };
        if (marked)
        {
            _marked = border;
        }

        return border;
    }

    /// <summary>Draws a quote: the text with a line at its left.</summary>
    /// <param name="quote">The quote.</param>
    /// <returns>Its element.</returns>
    private Border Quote(GuideQuote quote) => new()
    {
        Child = Text(quote.Runs, default),
        Margin = new Thickness(0, 12, 0, 0),
        Padding = new Thickness(12, 2, 0, 2),
        BorderThickness = new Thickness(2, 0, 0, 0),
        BorderBrush = Brush("Brush.BorderStrong"),
    };

    /// <summary>
    /// Draws code as it stands, in Cascadia Mono; a drawing wider than the column is made smaller rather than broken
    /// or scrolled sideways.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <returns>Its element.</returns>
    private Border Code(GuideCode code) => new()
    {
        Margin = new Thickness(0, 12, 0, 0),
        Padding = new Thickness(14, 12, 14, 12),
        CornerRadius = new CornerRadius(8),
        Background = Brush("Brush.Surface"),
        BorderBrush = Brush("Brush.Border"),
        BorderThickness = new Thickness(1),
        Child = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = code.Text,
                FontFamily = (FontFamily)FindResource("Font.Mono"),
                FontSize = 12,
                LineHeight = 19,
                Foreground = Brush("Brush.TextMuted"),
            },
        },
    };

    /// <summary>Draws a list: a dot or a number per item, nested items moved in.</summary>
    /// <param name="list">The list.</param>
    /// <returns>Its element.</returns>
    private StackPanel List(GuideList list)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var item in list.Items)
        {
            var row = new DockPanel { Margin = new Thickness(item.Depth * ListIndent, 0, 0, 6) };
            FrameworkElement mark = item.Number is { } number
                ? new TextBlock
                {
                    Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".",
                    FontSize = BodySize,
                    Foreground = Brush("Brush.TextDim"),
                    Width = 18,
                }
                : new Ellipse
                {
                    Width = 4,
                    Height = 4,
                    Fill = Brush("Brush.TextDim"),
                    Margin = new Thickness(0, BodySize * 0.72, 10, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                };
            DockPanel.SetDock(mark, Dock.Left);
            row.Children.Add(mark);
            row.Children.Add(Text(item.Runs, default));
            panel.Children.Add(row);
        }

        return panel;
    }

    /// <summary>Draws a table: the headers dim, a thin line between the rows, the columns sharing the width.</summary>
    /// <param name="table">The table.</param>
    /// <returns>Its element.</returns>
    private Grid Table(GuideTable table)
    {
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        for (var column = 0; column < table.Header.Count; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        IEnumerable<IReadOnlyList<IReadOnlyList<GuideRun>>> rows = [table.Header, .. table.Rows];
        var at = 0;
        foreach (var row in rows)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var column = 0; column < row.Count; column++)
            {
                var cell = Text(row[column], default);
                cell.FontSize = BodySize - 1.5;
                if (at == 0)
                {
                    cell.Foreground = Brush("Brush.TextDim");
                }

                var frame = new Border
                {
                    Child = cell,
                    Padding = new Thickness(0, 6, 12, 6),
                    BorderBrush = Brush("Brush.Border"),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                };
                Grid.SetRow(frame, at);
                Grid.SetColumn(frame, column);
                grid.Children.Add(frame);
            }

            at++;
        }

        return grid;
    }

    /// <summary>Draws <em>Läs också</em>: the sections the text links to.</summary>
    /// <param name="section">The section.</param>
    /// <returns>Its element, or <see langword="null"/> when the text links nowhere.</returns>
    private Border? Related(GuideSection section)
    {
        if (section.Related.Count == 0)
        {
            return null;
        }

        var line = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, LineHeight = 22 };
        line.Inlines.Add(new Run("Läs också") { Foreground = Brush("Brush.TextDim") });
        foreach (var id in section.Related)
        {
            line.Inlines.Add(new Run("    "));
            line.Inlines.Add(Link([new Run(TitleOf(id))], new GuideLink(id, null)));
        }

        return new Border
        {
            Child = line,
            Margin = new Thickness(0, 28, 0, 0),
            Padding = new Thickness(0, 14, 0, 0),
            BorderBrush = Brush("Brush.Border"),
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
    }

    /// <summary>Gives a related section's title.</summary>
    /// <param name="id">The section's id.</param>
    /// <returns>The title, or the id when the library is not set.</returns>
    private string TitleOf(string id) => Library?.Section(id)?.Title ?? id;

    /// <summary>Draws text with its styles and links, wrapped.</summary>
    /// <param name="runs">The pieces.</param>
    /// <param name="margin">The margin.</param>
    /// <returns>The text.</returns>
    private TextBlock Text(IReadOnlyList<GuideRun> runs, Thickness margin)
    {
        var text = new TextBlock
        {
            Margin = margin,
            FontSize = BodySize,
            LineHeight = Math.Round(BodySize * 1.65),
            Foreground = Brush("Brush.TextMuted"),
            TextWrapping = TextWrapping.Wrap,
        };
        var i = 0;
        while (i < runs.Count)
        {
            var link = runs[i].Link;
            var inlines = new List<Inline>();
            while (i < runs.Count && runs[i].Link == link)
            {
                inlines.Add(Styled(runs[i]));
                i++;
            }

            if (link is null)
            {
                text.Inlines.AddRange(inlines);
            }
            else
            {
                text.Inlines.Add(Link(inlines, link));
            }
        }

        return text;
    }

    /// <summary>Draws a piece in its style: bold and bright, italic, or code in Cascadia Mono on a surface.</summary>
    /// <param name="run">The piece.</param>
    /// <returns>The inline.</returns>
    private Run Styled(GuideRun run)
    {
        var inline = new Run(run.Text);
        if (run.IsStrong)
        {
            inline.FontWeight = FontWeights.SemiBold;
            inline.Foreground = Brush("Brush.Text");
        }

        if (run.IsEmphasis)
        {
            inline.FontStyle = FontStyles.Italic;
        }

        if (run.IsCode)
        {
            inline.FontFamily = (FontFamily)FindResource("Font.Mono");
            inline.FontSize = BodySize - 1.5;
            inline.Background = Brush("Brush.SurfaceRaised");
            inline.Foreground = Brush("Brush.Text");
        }

        return inline;
    }

    /// <summary>Draws a link in the accent; a press runs <see cref="LinkCommand"/>.</summary>
    /// <param name="inlines">The link's text.</param>
    /// <param name="link">Where it leads.</param>
    /// <returns>The link.</returns>
    private Hyperlink Link(IEnumerable<Inline> inlines, GuideLink link)
    {
        var hyperlink = new Hyperlink
        {
            Command = LinkCommand,
            CommandParameter = link,
            Foreground = Brush("Brush.Accent"),
            TextDecorations = null,
            Cursor = Cursors.Hand,
            FocusVisualStyle = null,
        };
        hyperlink.Inlines.AddRange(inlines);
        foreach (var inline in hyperlink.Inlines.OfType<Run>())
        {
            inline.Foreground = Brush("Brush.Accent");
        }

        hyperlink.MouseEnter += (_, _) => hyperlink.TextDecorations = TextDecorations.Underline;
        hyperlink.MouseLeave += (_, _) => hyperlink.TextDecorations = null;
        return hyperlink;
    }

    /// <summary>Brings the nearest scroll viewer to the marked heading, or to the top without a mark.</summary>
    private void ScrollToMark()
    {
        var viewer = FindScrollViewer();
        if (viewer?.Content is not UIElement content)
        {
            return;
        }

        if (_marked is null || !_marked.IsVisible)
        {
            viewer.ScrollToVerticalOffset(0);
            return;
        }

        var top = _marked.TranslatePoint(default, content).Y;
        viewer.ScrollToVerticalOffset(Math.Max(0, top - ScrollMargin));
    }

    /// <summary>Finds the scroll viewer the view lies in.</summary>
    /// <returns>The scroll viewer, or <see langword="null"/>.</returns>
    private ScrollViewer? FindScrollViewer()
    {
        DependencyObject? parent = VisualTreeHelper.GetParent(this);
        while (parent is not null and not ScrollViewer)
        {
            parent = VisualTreeHelper.GetParent(parent);
        }

        return parent as ScrollViewer;
    }

    /// <summary>Finds a brush in the theme.</summary>
    /// <param name="key">The brush's key.</param>
    /// <returns>The brush.</returns>
    private Brush Brush(string key) => (Brush)FindResource(key);
}
