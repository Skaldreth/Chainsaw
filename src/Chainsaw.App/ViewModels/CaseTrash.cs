using Chainsaw.App.Settings;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The cases thrown on the start page (sketch SO1a), shared by the start page, which hides them, and the trash's view,
/// which puts them back. Kept on this computer through an <see cref="ICaseTrashStore"/>; a thrown case is never taken
/// off the disk. Without a store it lives in memory only. No WPF.
/// </summary>
/// <param name="clock">The clock, which says when a case was thrown.</param>
public sealed class CaseTrash(IClock clock)
{
    /// <summary>The thrown cases, the latest last.</summary>
    private List<TrashedCase> _cases = [];

    /// <summary>Where they are kept, once read.</summary>
    private ICaseTrashStore? _store;

    /// <summary>Raised when a case is thrown or put back, or the saved cases are read.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the thrown cases, the latest last.</summary>
    public IReadOnlyList<TrashedCase> Cases => _cases;

    /// <summary>Gets the latest write, so that the tests can wait for it.</summary>
    public Task Saved { get; private set; } = Task.CompletedTask;

    /// <summary>Reads the saved cases off the UI thread and keeps them in the store from now on.</summary>
    /// <param name="store">Where they are kept.</param>
    /// <param name="ui">The UI thread, where they are shown.</param>
    /// <returns>A task that completes when they are read.</returns>
    public async Task UseAsync(ICaseTrashStore store, IUiThread ui)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ui);
        _store = store;
        var saved = await Task.Run(store.Load);
        ui.Post(() =>
        {
            _cases = [.. saved.Concat(_cases).DistinctBy(each => each.CaseNumber, StringComparer.OrdinalIgnoreCase)];
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>Says whether a case lies in the trash.</summary>
    /// <param name="caseNumber">The case.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool Contains(string caseNumber) =>
        _cases.Any(each => string.Equals(each.CaseNumber, caseNumber, StringComparison.OrdinalIgnoreCase));

    /// <summary>Throws a case in the trash; one already there stays as it was.</summary>
    /// <param name="caseNumber">The case.</param>
    /// <returns><see langword="true"/> when it was thrown now.</returns>
    public bool Throw(string caseNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseNumber);
        if (Contains(caseNumber))
        {
            return false;
        }

        _cases.Add(new TrashedCase(caseNumber, clock.UtcNow));
        Keep();
        return true;
    }

    /// <summary>Puts a case back on the start page.</summary>
    /// <param name="caseNumber">The case.</param>
    /// <returns><see langword="true"/> when it lay in the trash.</returns>
    public bool Restore(string caseNumber)
    {
        if (_cases.RemoveAll(each =>
                string.Equals(each.CaseNumber, caseNumber, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            return false;
        }

        Keep();
        return true;
    }

    /// <summary>Puts every case back on the start page.</summary>
    public void RestoreAll()
    {
        if (_cases.Count == 0)
        {
            return;
        }

        _cases = [];
        Keep();
    }

    /// <summary>Tells the change and writes the cases off the UI thread, when there is a store.</summary>
    private void Keep()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        if (_store is { } store)
        {
            var cases = _cases.ToList();
            Saved = Saved.ContinueWith(
                _ => store.Save(cases), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }
}
