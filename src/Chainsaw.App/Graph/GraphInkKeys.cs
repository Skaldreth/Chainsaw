namespace Chainsaw.App.Graph;

/// <summary>
/// Names the brush in <c>Themes/Colors.xaml</c> for each <see cref="GraphInk"/>, without WPF, so that a view model can
/// name a colour (the command bar's search rows, sketch KR1a) as the graph draws it.
/// </summary>
public static class GraphInkKeys
{
    /// <summary>Gives the resource key of an ink's brush.</summary>
    /// <param name="ink">The ink.</param>
    /// <returns>The key.</returns>
    public static string Of(GraphInk ink) => ink switch
    {
        GraphInk.Accent => "Brush.Accent",
        GraphInk.CashOut => "Brush.Kind.CashOut",
        GraphInk.Spent => "Brush.Kind.Spent",
        GraphInk.Risk => "Brush.Kind.Risk",
        GraphInk.Sanctioned => "Brush.Kind.Sanctioned",
        GraphInk.OtherChain => "Brush.Kind.OtherChain",
        GraphInk.Cluster => "Brush.Kind.Cluster",
        GraphInk.Unknown => "Brush.Kind.Unknown",
        GraphInk.StartMark => "Brush.Mark.Start",
        GraphInk.Freeze => "Brush.Mark.Freeze",
        _ => "Brush.Graph.Untraced",
    };
}
