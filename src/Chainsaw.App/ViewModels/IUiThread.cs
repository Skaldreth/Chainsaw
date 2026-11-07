namespace Chainsaw.App.ViewModels;

/// <summary>
/// The UI thread, as the view models see it: events from the modules can come on any thread, and what they change is
/// posted here. The window gives the dispatcher; the tests run it at once.
/// </summary>
public interface IUiThread
{
    /// <summary>Runs work on the UI thread, later, without waiting for it.</summary>
    /// <param name="work">The work.</param>
    void Post(Action work);
}
