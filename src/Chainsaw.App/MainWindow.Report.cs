using System.ComponentModel;
using System.Windows.Threading;
using Chainsaw.App.Navigation;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App;

/// <summary>The window's wiring of the view Rapport (sketch RP1b).</summary>
public partial class MainWindow
{
    /// <summary>
    /// Wires the view Rapport (sketch RP1b): the report follows the graph shown and what is marked in it while it is
    /// shown, checks the hash chains again each time it is shown, takes the keyboard then, so that Ctrl+S reaches it,
    /// and <em>Ändra i grafen</em> shows the graph with a way back, <em>‹ Rapport Esc</em>.
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
        graph.Checks.Changed += (_, _) => report.UpdateApproved(graph.Checks.ApprovedPayouts);
        report.GraphWanted += (_, _) =>
        {
            shell.Show(AppView.Graph);
            graph.OfferReturn(AppView.Report);
        };
        shell.PropertyChanged += (_, e) => OnReportShown(shell, graph, report, e);
    }

    /// <summary>
    /// When the view Rapport is shown or hidden: shown, the marking and the hash chains are read anew and it takes the
    /// keys; hidden, it stops building.
    /// </summary>
    /// <param name="shell">The shell.</param>
    /// <param name="graph">The graph.</param>
    /// <param name="report">The view Rapport.</param>
    /// <param name="e">What changed in the shell.</param>
    private void OnReportShown(
        ShellViewModel shell, GraphViewModel graph, ReportViewModel report, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellViewModel.IsReportShown))
        {
            return;
        }

        _ = report.SetShownAsync(shell.IsReportShown, graph.Scene, graph.Checks.Picked, graph.Places);
        if (shell.IsReportShown)
        {
            Dispatcher.BeginInvoke(() => ReportPage.Focus(), DispatcherPriority.Input);
        }
    }
}
