using System.Windows.Threading;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Controls;

/// <summary>The UI thread through the application's dispatcher; work is posted, never waited for.</summary>
/// <param name="dispatcher">The application's dispatcher.</param>
public sealed class WpfUiThread(Dispatcher dispatcher) : IUiThread
{
    /// <inheritdoc />
    public void Post(Action work) => dispatcher.BeginInvoke(work);
}
