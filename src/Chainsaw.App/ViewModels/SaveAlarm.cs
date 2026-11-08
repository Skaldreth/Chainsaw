using System.Collections.Specialized;
using System.Diagnostics;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// Says when the open case cannot be saved (sketch TO1a): the case saves itself after writes and tries again after a
/// save that fails, silently at first, since a file that is briefly locked is common. Once
/// <see cref="FailuresBeforeWarning"/> saves in a row have failed, a warning stands in the toaster with
/// <em>Försök igen</em>, and it goes when a save succeeds. Nothing is lost meanwhile: the working copy holds every
/// write and is taken back after a crash; only the <c>.cut</c> file is not fresh.
/// </summary>
public sealed class SaveAlarm
{
    /// <summary>How many saves in a row must fail before the warning is shown.</summary>
    public const int FailuresBeforeWarning = 3;

    /// <summary>The warning's title.</summary>
    public const string WarningTitle = "Ärendet kunde inte sparas";

    /// <summary>The warning's line: that nothing is lost, and what is not fresh.</summary>
    public const string WarningText =
        "Arbetskopian har allt och tas tillbaka efter en krasch, men ärendefilen är inte uppdaterad.";

    /// <summary>The warning's action.</summary>
    public const string RetryText = "Försök igen";

    /// <summary>The toast once a save succeeds after the warning.</summary>
    public const string SavedTitle = "Ärendet är sparat igen";

    /// <summary>The open case.</summary>
    private readonly CaseSession _session;

    /// <summary>The toaster.</summary>
    private readonly Toaster _toaster;

    /// <summary>The UI thread, where the case's word from its saving thread is handled.</summary>
    private readonly IUiThread _ui;

    /// <summary>The case whose saves are followed, or <see langword="null"/>.</summary>
    private ICase? _followed;

    /// <summary>The warning standing in the toaster, or <see langword="null"/>.</summary>
    private ToastRequest? _warning;

    /// <summary>Creates the alarm and follows the open case.</summary>
    /// <param name="session">The open case.</param>
    /// <param name="toaster">The toaster.</param>
    /// <param name="ui">The UI thread.</param>
    public SaveAlarm(CaseSession session, Toaster toaster, IUiThread ui)
    {
        _session = session;
        _toaster = toaster;
        _ui = ui;
        session.Changed += (_, _) => Follow(session.Current);
        toaster.Toasts.CollectionChanged += OnToastsChanged;
        Follow(session.Current);
    }

    /// <summary>Gets whether the warning stands.</summary>
    public bool IsWarning => _warning is not null;

    /// <summary>Gets the last <em>Försök igen</em>, which completes once the toaster is told how it went.</summary>
    public Task Retrying { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Forgets the warning when its toast is gone (closed by the user, or emptied at a lock), so that the next failure
    /// warns again.
    /// </summary>
    /// <param name="sender">The toasts.</param>
    /// <param name="e">What changed.</param>
    private void OnToastsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_warning is not { } warning)
        {
            return;
        }

        var gone = e.Action == NotifyCollectionChangedAction.Reset
            || (e.OldItems?.OfType<Toast>().Any(toast => ReferenceEquals(toast.Request, warning)) ?? false);
        if (gone)
        {
            _warning = null;
        }
    }

    /// <summary>Follows another case's saves; a warning about the one before is taken away.</summary>
    /// <param name="open">The case open now, or <see langword="null"/>.</param>
    private void Follow(ICase? open)
    {
        if (ReferenceEquals(open, _followed))
        {
            return;
        }

        if (_followed is { } before)
        {
            before.SaveStateChanged -= OnSaveStateChanged;
        }

        _followed = open;
        TakeWarningAway();
        if (open is not null)
        {
            open.SaveStateChanged += OnSaveStateChanged;
        }
    }

    /// <summary>
    /// Takes the case's word from the thread that saved, which holds the case's gate, over to the UI thread.
    /// </summary>
    /// <param name="sender">The case.</param>
    /// <param name="state">How its saves go.</param>
    private void OnSaveStateChanged(object? sender, CaseSaveState state) =>
        _ui.Post(() => Show(sender as ICase, state));

    /// <summary>Shows the warning after enough failures, and says the case is saved once one succeeds.</summary>
    /// <param name="saved">The case that said it.</param>
    /// <param name="state">How its saves go.</param>
    private void Show(ICase? saved, CaseSaveState state)
    {
        if (saved is null || !ReferenceEquals(saved, _followed))
        {
            return;
        }

        if (state.IsSaved)
        {
            if (TakeWarningAway())
            {
                _toaster.Show(ToastKind.Success, SavedTitle);
            }

            return;
        }

        if (state.FailuresInARow >= FailuresBeforeWarning && _warning is null)
        {
            Warn(saved);
        }
    }

    /// <summary>Puts the warning with <em>Försök igen</em> in the toaster.</summary>
    /// <param name="saved">The case that cannot be saved.</param>
    private void Warn(ICase saved)
    {
        var warning = new ToastRequest(ToastKind.Warning, WarningTitle, WarningText, RetryText, () => Retry(saved));
        _warning = warning;
        _toaster.Show(warning);
    }

    /// <summary>
    /// <em>Försök igen</em>: the warning closes and the case is saved off the UI thread; the toaster says how it went.
    /// </summary>
    /// <param name="saved">The case.</param>
    private void Retry(ICase saved)
    {
        _warning = null;
        Retrying = RetryAsync(saved);
    }

    /// <summary>
    /// Saves the case on the thread pool: says so when it succeeds, and shows the warning again when it fails.
    /// </summary>
    /// <param name="saved">The case.</param>
    /// <returns>A task that completes when the save has been tried and the toaster told.</returns>
    private async Task RetryAsync(ICase saved)
    {
        bool succeeded;
        try
        {
            await Task.Run(saved.Save).ConfigureAwait(false);
            succeeded = true;
        }
        catch (CaseException exception) when (exception.Error == CaseError.Closed)
        {
            // The case closed meanwhile, and the closing saved it or said why not.
            return;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Whatever stopped the save, the warning comes back; only the kind is noted, never a path or the case.
            Trace.TraceWarning("Saving the case by hand failed: {0}", exception.GetType().Name);
            succeeded = false;
        }

        _ui.Post(() =>
        {
            if (!ReferenceEquals(saved, _followed))
            {
                return;
            }

            if (succeeded)
            {
                TakeWarningAway();
                _toaster.Show(ToastKind.Success, SavedTitle);
            }
            else if (_warning is null)
            {
                Warn(saved);
            }
        });
    }

    /// <summary>Takes the warning out of the toaster, if it stands.</summary>
    /// <returns><see langword="true"/> when it stood.</returns>
    private bool TakeWarningAway()
    {
        if (_warning is not { } warning)
        {
            return false;
        }

        _warning = null;
        foreach (var toast in _toaster.Toasts.Where(toast => ReferenceEquals(toast.Request, warning)).ToList())
        {
            _toaster.Dismiss(toast);
        }

        return true;
    }
}
