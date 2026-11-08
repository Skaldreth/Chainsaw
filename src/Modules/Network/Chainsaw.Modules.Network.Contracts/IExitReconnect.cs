namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// Connects the exit again by itself when its tunnel falls after it was connected, for example after the computer has
/// slept, with a wait that grows between the attempts and an attempt at once when the network comes back. The watches
/// need it most, since they would otherwise wait for the user (CLAUDE.md, <em>Bevaka</em>). It says when the next
/// attempt is made, so that the waiting is shown honestly.
/// </summary>
/// <remarks>
/// Only a connection that was lost is taken up again; a connection the user disconnected, or one whose tunnel refused
/// the exit's configuration or is not available, is left alone. Any connection or disconnection by the user ends the
/// reconnecting.
/// </remarks>
public interface IExitReconnect
{
    /// <summary>
    /// Gets when the next attempt is made, or <see langword="null"/> while none waits (connected, never lost, given
    /// up, or an attempt runs now).
    /// </summary>
    DateTimeOffset? NextAttempt { get; }

    /// <summary>Gets how many attempts have been made since the connection was lost; 0 when none.</summary>
    int Attempts { get; }

    /// <summary>
    /// Raised after <see cref="NextAttempt"/> or <see cref="Attempts"/> has changed, on any thread. A handler that
    /// throws does not affect the reconnecting.
    /// </summary>
    event EventHandler? Changed;
}
