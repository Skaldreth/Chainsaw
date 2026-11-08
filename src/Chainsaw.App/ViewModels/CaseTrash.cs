using Chainsaw.App.Settings;
using Chainsaw.Modules.Cases.Contracts;
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

    /// <summary>The UI thread, where a failed save is told; none until the store is read.</summary>
    private IUiThread? _ui;

    /// <summary>Raised when a case is thrown or put back, or the saved cases are read.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised on the UI thread when the cases could not be saved on this computer.</summary>
    public event EventHandler? SaveFailed;

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
        _ui = ui;
        var saved = await Task.Run(store.Load);
        ui.Post(() =>
        {
            // A case thrown before the saved ones were read is kept, and written with them.
            var thrownMeanwhile = _cases.Any(each => !saved.Any(stored =>
                string.Equals(stored.CaseNumber, each.CaseNumber, StringComparison.OrdinalIgnoreCase)));
            _cases = [.. saved.Concat(_cases).DistinctBy(each => each.CaseNumber, StringComparer.OrdinalIgnoreCase)];
            _store = store;
            if (thrownMeanwhile)
            {
                Keep();
                return;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>Says whether a case lies in the trash.</summary>
    /// <param name="caseNumber">The case.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool Contains(string caseNumber) =>
        _cases.Any(each => string.Equals(each.CaseNumber, caseNumber, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Says whether a case file is hidden by the trash: its number was thrown, and the file has not been written since.
    /// A case written after it was thrown is another case with the same number, or the thrown one worked on again, and
    /// is never hidden.
    /// </summary>
    /// <param name="file">The case file as listed.</param>
    /// <returns><see langword="true"/> when it is hidden.</returns>
    public bool Hides(CaseFileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return ThrownOf(file.CaseNumber) is { } thrown && file.LastWrittenUtc <= thrown.ThrownUtc;
    }

    /// <summary>
    /// Takes the cases whose file was written after they were thrown out of the trash, so that a new case with the same
    /// number as a thrown one is neither hidden nor listed in the trash's view.
    /// </summary>
    /// <param name="files">The case files as last listed.</param>
    /// <returns><see langword="true"/> when any was taken out.</returns>
    public bool ForgetRewritten(IEnumerable<CaseFileInfo> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var rewritten = files
            .Where(file => ThrownOf(file.CaseNumber) is { } thrown && file.LastWrittenUtc > thrown.ThrownUtc)
            .Select(file => file.CaseNumber)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (rewritten.Count == 0 || _cases.RemoveAll(each => rewritten.Contains(each.CaseNumber)) == 0)
        {
            return false;
        }

        Keep();
        return true;
    }

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

    /// <summary>Finds a thrown case.</summary>
    /// <param name="caseNumber">The case.</param>
    /// <returns>The thrown case, or <see langword="null"/>.</returns>
    private TrashedCase? ThrownOf(string caseNumber) =>
        _cases.Find(each => string.Equals(each.CaseNumber, caseNumber, StringComparison.OrdinalIgnoreCase));

    /// <summary>Tells the change and writes the cases off the UI thread, when there is a store.</summary>
    private void Keep()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        if (_store is { } store)
        {
            var cases = _cases.ToList();
            var ui = _ui;
            Saved = Saved.ContinueWith(
                _ =>
                {
                    if (!store.Save(cases))
                    {
                        ui?.Post(() => SaveFailed?.Invoke(this, EventArgs.Empty));
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }
    }
}
