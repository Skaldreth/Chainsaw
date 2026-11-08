using System.ComponentModel;
using System.Windows.Threading;
using Chainsaw.App.Navigation;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App;

/// <summary>The window's wiring of the view Rapport (sketch RP1b).</summary>
public partial class MainWindow
{
    /// <summary>
    /// Wires the view Rapport (sketch RP1b): the report follows the graph shown and what is marked in it, checks the
    /// hash chain again each time it is shown, takes the keyboard then, so that Ctrl+S reaches it, and
    /// <em>Ändra i grafen</em> shows the graph.
    /// </summary>
    /// <param name="shell">The shell.</param>
    /// <param name="graph">The graph.</param>
    /// <param name="report">The view Rapport.</param>
    private void WireReport(ShellViewModel shell, GraphViewModel graph, ReportViewModel report)
    {
        ReportPage.Attach(report);
        void Follow() => report.Update(graph.Scene, graph.Checks.Picked, graph.Places);
        Follow();
        graph.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GraphViewModel.Scene))
            {
                Follow();
            }
        };
        graph.Checks.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChecksViewModel.Picked))
            {
                Follow();
            }
        };
        report.GraphWanted += (_, _) => shell.Show(AppView.Graph);
        shell.PropertyChanged += (_, e) => OnReportShown(shell, graph, report, e);
    }

    /// <summary>When the view Rapport is shown: the marking and the hash chain are read anew, and it takes the keys.
    /// </summary>
    /// <param name="shell">The shell.</param>
    /// <param name="graph">The graph.</param>
    /// <param name="report">The view Rapport.</param>
    /// <param name="e">What changed in the shell.</param>
    private void OnReportShown(
        ShellViewModel shell, GraphViewModel graph, ReportViewModel report, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellViewModel.IsReportShown) || !shell.IsReportShown)
        {
            return;
        }

        // The places may have moved since the marking last changed, so the small picture is drawn anew.
        report.Update(graph.Scene, graph.Checks.Picked, graph.Places);
        _ = report.RefreshAsync();
        Dispatcher.BeginInvoke(() => ReportPage.Focus(), DispatcherPriority.Input);
    }
}
