namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// The proxy of the way out through Windows and the user's choice to log in to it with the Windows account (sketch
/// PX1a, decided 2026-11-08). Chainsaw reads Windows' setting and changes nothing in it. The Windows account (Kerberos
/// or NTLM, worked out by Windows; the password never leaves the computer) is sent only to a proxy that is set by hand,
/// only to the very address the user turned the login on for, only when the proxy asks for it (<c>407</c>), and never
/// to a source, to a proxy found with WPAD or a PAC script, or to another address after the proxy has been changed in
/// Windows. Everything else is as before: only https, no cookies, no referer. The members are thread-safe.
/// </summary>
public interface IWindowsProxy
{
    /// <summary>
    /// Gets the proxy the user turned the login on for, as <c>host:port</c>, or <see langword="null"/> when it is off.
    /// </summary>
    string? LoginFor { get; }

    /// <summary>
    /// Gets when the proxy last asked for a login (<c>407</c>) and did not let a lookup or the probe through, in UTC;
    /// or <see langword="null"/> when nothing has been refused that way since something last went through.
    /// </summary>
    DateTimeOffset? LoginAskedAt { get; }

    /// <summary>
    /// Raised after <see cref="LoginFor"/> or <see cref="LoginAskedAt"/> has changed, on the thread that changed it. A
    /// handler that throws does not affect the change.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>Reads what Windows is set up with now.</summary>
    /// <returns>The setting; <see cref="WindowsProxySetting.None"/> where it cannot be read.</returns>
    WindowsProxySetting Read();

    /// <summary>
    /// Tells whether the login applies now: it is on, and Windows' proxy is still set by hand, to the same address.
    /// </summary>
    /// <returns><see langword="true"/> when the Windows account may be sent to the proxy when it asks.</returns>
    bool IsLoginOn();

    /// <summary>
    /// Turns the login on for a proxy set by hand, or off. The way out through Windows is built anew, so that no
    /// connection that logged in stays open after the login is turned off or moved.
    /// </summary>
    /// <param name="proxy">
    /// The proxy's address as <see cref="WindowsProxySetting.Address"/> gives it, or <see langword="null"/> for off.
    /// </param>
    void AllowLogin(string? proxy);
}
