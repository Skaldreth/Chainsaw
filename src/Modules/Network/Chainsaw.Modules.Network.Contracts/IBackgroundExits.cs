namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// The exits the user lets the background watching go out through when Chainsaw is closed (decided 2026-11-08). An
/// exit's WireGuard file is otherwise only in the vault, which the background never opens; for an exit the user
/// allows, a copy is kept protected for the Windows user (DPAPI, scope CurrentUser) outside the vault, so that the
/// background can start the same helper (checked by its SHA-256) and watch through VPN. The copy is removed when the
/// user takes the permission back, when the exit is removed and when the background watching is turned off.
/// </summary>
/// <remarks>
/// The background goes out only through the program's selected exit, and only when that exit is allowed: an exit that
/// is allowed but not selected is never used, and with no allowed selected exit the background looks nothing up
/// (principle 4). The protection binds the copy to the Windows account on this computer; it is no protection against
/// programs that run as the same user, which is why it is the user's choice per exit.
/// </remarks>
public interface IBackgroundExits
{
    /// <summary>
    /// Gets whether copies can be kept at all: the file's place is set and there is a protection for the user (DPAPI on
    /// Windows; elsewhere protecting fails and nothing is kept).
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Tells whether an exit is allowed in the background.</summary>
    /// <param name="exitId">The exit.</param>
    /// <returns><see langword="true"/> when a copy of its configuration is kept for the background.</returns>
    bool IsAllowed(Guid exitId);

    /// <summary>
    /// Gives the exit the background would connect: the program's selected exit, when it is allowed.
    /// </summary>
    /// <returns>Its id, or <see langword="null"/> when the selected exit is not allowed or none is selected.</returns>
    Guid? Usable();

    /// <summary>
    /// Allows an exit in the background: its configuration is copied from the vault, which must be unlocked, and kept
    /// protected for the Windows user, with the program's selected exit.
    /// </summary>
    /// <param name="exitId">The exit.</param>
    /// <exception cref="ArgumentException">No exit has the id.</exception>
    /// <exception cref="InvalidOperationException">
    /// Copies cannot be kept (<see cref="IsAvailable"/>), or the computer cannot protect them.
    /// </exception>
    /// <exception cref="IOException">The copy could not be written; nothing was changed.</exception>
    void Allow(Guid exitId);

    /// <summary>Takes the permission back: the exit's copy is removed. An exit not allowed is ignored.</summary>
    /// <param name="exitId">The exit.</param>
    /// <exception cref="IOException">The copy could not be removed just now.</exception>
    void Forbid(Guid exitId);

    /// <summary>Removes every copy, as when the background watching is turned off.</summary>
    /// <exception cref="IOException">The copies could not be removed just now.</exception>
    void Clear();
}
