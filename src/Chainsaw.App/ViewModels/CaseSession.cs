using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The case open in the window. <see cref="CaseWorkflow"/> opens, creates and closes cases through it; when the vault
/// is locked, Cases closes the case itself, and the window forgets it.
/// </summary>
/// <remarks>
/// The session is used on the UI thread. Closing a case saves it, which can take a noticeable time, so it is done on
/// the thread pool; only <see cref="CloseNow"/>, when Chainsaw exits, waits for it.
/// </remarks>
public sealed class CaseSession
{
    /// <summary>The closing that <see cref="Forget"/> started in the background, or a completed task.</summary>
    private Task _closing = Task.CompletedTask;

    /// <summary>Gets the open case, or <see langword="null"/> when none is open.</summary>
    public ICase? Current { get; private set; }

    /// <summary>
    /// Gets a task that completes when a case forgotten in the background (at a lock) has been saved and closed, so
    /// that opening it again does not meet its own lock file and say that another Chainsaw uses it.
    /// </summary>
    public Task Settled => _closing;

    /// <summary>Gets the open case's number and title for the title bar, or an empty text when none is open.</summary>
    public string DisplayName => Current is { } open ? NameOf(open) : string.Empty;

    /// <summary>
    /// Raised on the UI thread when a case is opened or forgotten, so that what was shown for the case before is let
    /// go; a new title is <see cref="Renamed"/>, since it is the same case.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised on the UI thread when the open case's title has changed, so that what shows its name follows without
    /// starting over for a case that is still the same.
    /// </summary>
    public event EventHandler? Renamed;

    /// <summary>
    /// Raised on the UI thread just before the open case is let go (closed, forgotten at a lock, or replaced), while it
    /// is still open, so that what is being written in it (a comment) can be sealed first.
    /// </summary>
    public event EventHandler<ICase>? Closing;

    /// <summary>
    /// Gives a case's name as the title bar shows it: <c>2026-0117 · Investeringsbedrägeri via chatt</c>.
    /// </summary>
    /// <param name="openCase">The case.</param>
    /// <returns>Its number, and its title when it has one.</returns>
    public static string NameOf(ICase openCase) =>
        openCase.Title.Length == 0 ? NumberOf(openCase) : $"{NumberOf(openCase)} · {openCase.Title}";

    /// <summary>
    /// Gives a case's number as its file is named. It is the number the case was created with, except for a working
    /// copy saved as a case of its own after a crash (sketch ÄR1b), which keeps the case inside and gets a file name
    /// of its own, for example <c>2026-0117-B</c>.
    /// </summary>
    /// <param name="openCase">The case.</param>
    /// <returns>The number.</returns>
    public static string NumberOf(ICase openCase)
    {
        var fileNumber = Path.GetFileNameWithoutExtension(openCase.Path);
        return CaseNumberRules.IsValid(fileNumber) ? fileNumber : openCase.Info.CaseNumber;
    }

    /// <summary>
    /// Makes a case the open one. A case open before is forgotten quietly; <see cref="CaseWorkflow"/> closes it
    /// properly first, so this only happens when two openings race.
    /// </summary>
    /// <param name="openCase">The open case; the session closes it when it is closed or forgotten.</param>
    public void Open(ICase openCase)
    {
        Forget();
        Current = openCase;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Tells the window that the open case's title has changed.</summary>
    public void Refresh() => Renamed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Closes the open case properly: the window forgets it at once, and it is saved and closed off the UI thread.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the case was saved and closed, or none was open; <see langword="false"/> when the
    /// save failed and the case's working copy is kept for the next opening to recover.
    /// </returns>
    public async Task<bool> CloseAsync()
    {
        if (Current is not { } openCase)
        {
            return true;
        }

        Closing?.Invoke(this, openCase);
        Current = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return await Task.Run(() => TryClose(openCase));
    }

    /// <summary>
    /// Closes the open case and waits for it, when Chainsaw exits, so that the process never ends before the case is
    /// saved.
    /// </summary>
    /// <returns><see langword="true"/> when the case was saved, or none was open.</returns>
    public bool CloseNow()
    {
        if (Current is not { } openCase)
        {
            return true;
        }

        Closing?.Invoke(this, openCase);
        Current = null;
        return TryClose(openCase);
    }

    /// <summary>
    /// Forgets the open case and disposes it off the UI thread, since closing saves the case. When the vault has been
    /// locked, Cases has already closed it and the dispose does nothing.
    /// </summary>
    public void Forget()
    {
        if (Current is not { } openCase)
        {
            return;
        }

        Closing?.Invoke(this, openCase);
        Current = null;
        Release(openCase);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Saves and closes, in the background, a case that was never made the open one (it was opened or created while
    /// Chainsaw locked); <see cref="Settled"/> waits for it too, so that opening it again does not meet its lock file.
    /// </summary>
    /// <param name="openCase">The case.</param>
    public void Release(ICase openCase)
    {
        ArgumentNullException.ThrowIfNull(openCase);
        var before = _closing;
        _closing = Task.Run(async () =>
        {
            await before;
            DisposeQuietly(openCase);
        });
    }

    /// <summary>Disposes a case; a failure to save keeps its working copy, which the next opening recovers.</summary>
    /// <param name="openCase">The case.</param>
    public static void DisposeQuietly(ICase openCase) => TryClose(openCase);

    /// <summary>Saves and closes a case.</summary>
    /// <param name="openCase">The case.</param>
    /// <returns><see langword="true"/> when it was saved (or had already been closed).</returns>
    private static bool TryClose(ICase openCase)
    {
        try
        {
            openCase.Close();
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Close releases the case's keys and lock whatever fails, and keeps the working copy, which is recovered
            // with a line in the journal when the case is opened next. Nothing more can be done here, and a failed
            // save must never take the window down, so every failure is answered with false.
            return false;
        }
    }
}
