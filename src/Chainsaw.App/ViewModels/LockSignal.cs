namespace Chainsaw.App.ViewModels;

/// <summary>
/// Carries a request to lock Chainsaw from where it is asked (<c>/lock</c>) to the gate, which does the locking, so
/// that the commands need not know the gate.
/// </summary>
public sealed class LockSignal
{
    /// <summary>Raised when Chainsaw is to be locked now.</summary>
    public event EventHandler? Requested;

    /// <summary>Asks the gate to lock Chainsaw now.</summary>
    public void Request() => Requested?.Invoke(this, EventArgs.Empty);
}
