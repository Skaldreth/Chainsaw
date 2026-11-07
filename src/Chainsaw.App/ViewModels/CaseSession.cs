using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The case open in the window. The first-run setup can create the first case; opening and closing cases from the
/// window comes in stage 11. When the vault is locked, Cases closes the case itself, and the window forgets it.
/// </summary>
public sealed class CaseSession
{
    /// <summary>Gets the open case, or <see langword="null"/> when none is open.</summary>
    public ICase? Current { get; private set; }

    /// <summary>Raised on the UI thread when a case is opened or forgotten.</summary>
    public event EventHandler? Changed;

    /// <summary>Makes a case the open one, forgetting any case open before.</summary>
    /// <param name="openCase">The open case; the session disposes it when it is forgotten.</param>
    public void Open(ICase openCase)
    {
        Forget();
        Current = openCase;
        Changed?.Invoke(this, EventArgs.Empty);
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

        Current = null;
        _ = Task.Run(() => DisposeQuietly(openCase));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Disposes a case; a failure to save keeps its working copy, which the next opening recovers.</summary>
    /// <param name="openCase">The case.</param>
    private static void DisposeQuietly(ICase openCase)
    {
        try
        {
            openCase.Dispose();
        }
        catch (Exception exception) when (exception is CaseException or IOException or UnauthorizedAccessException)
        {
            // The case keeps its working copy and is recovered, with a line in its journal, when it is opened next.
        }
    }
}
