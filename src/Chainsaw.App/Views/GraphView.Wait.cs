using System.Windows.Threading;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// <em>Väntar på källan</em> in the graph's head (sketch GR9a): while a wait is shown, the view moves its countdown on
/// once a second; the view model says how long is left, so that the time is never the view's.
/// </summary>
public partial class GraphView
{
    /// <summary>How often the countdown moves on.</summary>
    private static readonly TimeSpan WaitTick = TimeSpan.FromSeconds(1);

    /// <summary>Moves the countdown on while a wait is shown.</summary>
    private DispatcherTimer? _waitTimer;

    /// <summary>Starts and stops the countdown with the wait.</summary>
    /// <param name="model">The view model.</param>
    private void AttachWait(GraphViewModel model)
    {
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GraphViewModel.HasWait))
            {
                KeepWaitTicking(model);
            }
        };
        IsVisibleChanged += (_, _) => KeepWaitTicking(model);
        KeepWaitTicking(model);
    }

    /// <summary>Runs the countdown while a wait is shown and the graph is on screen.</summary>
    /// <param name="model">The view model.</param>
    private void KeepWaitTicking(GraphViewModel model)
    {
        if (model.HasWait && IsVisible)
        {
            _waitTimer ??= new DispatcherTimer(WaitTick, DispatcherPriority.Background, (_, _) => model.TickWait(), Dispatcher);
            return;
        }

        _waitTimer?.Stop();
        _waitTimer = null;
    }
}
