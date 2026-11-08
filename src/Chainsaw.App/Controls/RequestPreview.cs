using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Chainsaw.Modules.Requests.Contracts;

namespace Chainsaw.App.Controls;

/// <summary>
/// Draws a request as the large preview of the view Begäran (sketch BG1b): the title, headings, paragraphs, quiet
/// notes and tables, from the very blocks the file is written from, so that what is seen is what is exported. A
/// transaction, an address or a hash is written in mono and shortened only when it does not fit; its tooltip holds it
/// whole.
/// </summary>
public sealed class RequestPreview : StackPanel
{
    /// <summary>Identifies <see cref="Document"/>.</summary>
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document), typeof(RequestDocument), typeof(RequestPreview),
        new PropertyMetadata(null, (d, _) => ((RequestPreview)d).Rebuild()));

    /// <summary>The title's size (sketch BG1b: 20 px).</summary>
    private const double TitleSize = 20;

    /// <summary>A heading's size (sketch BG1b: 14 px).</summary>
    private const double HeadingSize = 14;

    /// <summary>A paragraph's size (sketch BG1b: 13 px).</summary>
    private const double BodySize = 13;

    /// <summary>A table's size (sketch BG1b: 12 px).</summary>
    private const double TableSize = 12;

    /// <summary>The line height as a share of the size (sketch BG1b: 1.55).</summary>
    private const double LineShare = 1.55;

    /// <summary>The space between blocks (sketch BG1b: 10 px).</summary>
    private const double Gap = 10;

    /// <summary>A cell's padding (sketch BG1b: 6 px above and below, 8 px to the right).</summary>
    private static readonly Thickness CellPadding = new(0, 6, 8, 6);

    /// <summary>Gets or sets the request to draw.</summary>
    public RequestDocument? Document
    {
        get => (RequestDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>Draws the blocks again from the request.</summary>
    private void Rebuild()
    {
        Children.Clear();
        if (Document is null)
        {
            return;
        }

        foreach (var block in Document.Blocks)
        {
            FrameworkElement element = block.Kind == RequestBlockKind.Table ? CreateTable(block) : CreateText(block);
            element.Margin = new Thickness(0, Children.Count == 0 ? 0 : Gap + TopExtra(block), 0, 0);
            Children.Add(element);
        }
    }

    /// <summary>Gives the extra space over a heading, which opens a section (sketch BG1b: 6 px).</summary>
    /// <param name="block">The block.</param>
    /// <returns>The space.</returns>
    private static double TopExtra(RequestBlock block) => block.Kind == RequestBlockKind.Heading ? 6 : 0;

    /// <summary>Draws a title, a heading, a paragraph or a note.</summary>
    /// <param name="block">The block.</param>
    /// <returns>Its element.</returns>
    private TextBlock CreateText(RequestBlock block)
    {
        var (size, weight, brush) = block.Kind switch
        {
            RequestBlockKind.Title => (TitleSize, FontWeights.SemiBold, "Brush.Text"),
            RequestBlockKind.Heading => (HeadingSize, FontWeights.SemiBold, "Brush.Text"),
            RequestBlockKind.Note => (BodySize, FontWeights.Normal, "Brush.TextFaint"),
            _ => (BodySize, FontWeights.Normal, "Brush.TextMuted"),
        };
        return new TextBlock
        {
            Text = block.Text,
            FontSize = size,
            FontWeight = weight,
            LineHeight = Math.Round(size * LineShare),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(brush),
        };
    }

    /// <summary>
    /// Draws a table: the headings dimmed over a line, then a row per row with a line under each; the mono columns
    /// share the room left by the others.
    /// </summary>
    /// <param name="block">The table.</param>
    /// <returns>Its element.</returns>
    private Grid CreateTable(RequestBlock block)
    {
        var grid = new Grid();
        for (var column = 0; column < block.Columns.Count; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = block.MonoColumns.Contains(column)
                    ? new GridLength(1, GridUnitType.Star)
                    : GridLength.Auto,
                MinWidth = block.MonoColumns.Contains(column) ? 60 : 0,
            });
        }

        AddRow(grid, block, block.Columns, isHead: true);
        foreach (var row in block.Rows)
        {
            AddRow(grid, block, row, isHead: false);
        }

        return grid;
    }

    /// <summary>Adds one row of a table, each cell with the line under it.</summary>
    /// <param name="grid">The table.</param>
    /// <param name="block">The table's block, for which columns are mono.</param>
    /// <param name="cells">The cells.</param>
    /// <param name="isHead">Whether it is the row of headings.</param>
    private void AddRow(Grid grid, RequestBlock block, IReadOnlyList<string> cells, bool isHead)
    {
        var index = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var column = 0; column < cells.Count; column++)
        {
            var isMono = !isHead && block.MonoColumns.Contains(column);
            var text = new TextBlock
            {
                Text = cells[column],
                FontSize = TableSize,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Brush(isHead ? "Brush.TextDim" : "Brush.Text"),
                ToolTip = isMono ? cells[column] : null,
            };
            if (isMono)
            {
                text.FontFamily = (FontFamily)FindResource("Font.Mono");
            }

            var cell = new Border
            {
                Padding = CellPadding,
                BorderBrush = Brush(isHead ? "Brush.BorderStrong" : "Brush.Border"),
                BorderThickness = new Thickness(0, isHead ? 1 : 0, 0, 1),
                Child = text,
            };
            Grid.SetRow(cell, index);
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }
    }

    /// <summary>Finds a brush of the theme.</summary>
    /// <param name="key">Its key.</param>
    /// <returns>The brush.</returns>
    private Brush Brush(string key) => (Brush)FindResource(key);
}
