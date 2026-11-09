namespace Chainsaw.App.ViewModels;

/// <summary>
/// Carries a request to lock Chainsaw from where it is asked (<c>/lock</c>) to the gate, which does the locking, so
/// that the commands need not know the gate.
/// </summary>
public sealed class LockSignal
{
    /// <summary>Raised when Chainsaw is to be locked now.</summary>
    public event EventHandler? Requested;

    /// <summary>
    /// Raised when Chainsaw is to be locked now and the lock screen is to say why; the argument is the words.
    /// </summary>
    public event EventHandler<string>? RequestedWithNotice;

    /// <summary>Asks the gate to lock Chainsaw now.</summary>
    public void Request() => Requested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Asks the gate to lock Chainsaw now, also when it already is, and to show the words on the lock screen until the
    /// next unlock or until they are dismissed.
    /// </summary>
    /// <param name="notice">The words.</param>
    public void Request(string notice) => RequestedWithNotice?.Invoke(this, notice);
}
