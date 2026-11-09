using System.Net;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Network;

/// <summary>
/// The user's choice to log in to Windows' proxy with the Windows account (sketch PX1a), and the gate that holds it to
/// a proxy set by hand: the credentials it gives the pipeline through Windows are asked for by the proxy a request is
/// sent to, and they answer only for that very proxy, only while Windows' setting is still that proxy set by hand
/// (read anew at every question, so that a change to WPAD, a script or another address in Windows takes effect at
/// once), and only for Kerberos or NTLM, where Windows proves the account without sending the password. A source is
/// never asked: the pipeline has no credentials for a server.
/// </summary>
/// <param name="reader">Reads Windows' setting.</param>
/// <param name="options">The settings, with the proxy the login was turned on for when Chainsaw started.</param>
/// <param name="clock">The clock for when the proxy asked for a login.</param>
internal sealed class WindowsProxyLogin(IWindowsProxyReader reader, NetworkOptions options, IClock clock)
    : IWindowsProxy
{
    /// <summary>The schemes the Windows account is given for: those that never send the password.</summary>
    private static readonly string[] AllowedSchemes = ["Negotiate", "NTLM", "Kerberos"];

    /// <summary>Guards the choice and the last request for a login.</summary>
    private readonly Lock _gate = new();

    /// <summary>The proxy the login is on for, normalised, or <see langword="null"/> for off.</summary>
    private string? _loginFor = Normalised(options.ProxyLoginFor);

    /// <summary>When the proxy last refused with <c>407</c>, or <see langword="null"/>.</summary>
    private DateTimeOffset? _askedAt;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>
    /// Raised after the login has been turned on, off or moved, before <see cref="Changed"/>, so that the pipeline
    /// through Windows is built anew; never for a refusal noted.
    /// </summary>
    public event EventHandler? ChoiceChanged;

    /// <inheritdoc />
    public string? LoginFor
    {
        get
        {
            lock (_gate)
            {
                return _loginFor;
            }
        }
    }

    /// <inheritdoc />
    public DateTimeOffset? LoginAskedAt
    {
        get
        {
            lock (_gate)
            {
                return _askedAt;
            }
        }
    }

    /// <inheritdoc />
    public WindowsProxySetting Read()
    {
        try
        {
            return reader.Read();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A setting that cannot be read is no proxy set by hand: nothing is ever sent then.
            return WindowsProxySetting.None;
        }
    }

    /// <inheritdoc />
    public bool IsLoginOn()
    {
        var loginFor = LoginFor;
        return loginFor is not null && IsManual(Read(), loginFor);
    }

    /// <inheritdoc />
    public void AllowLogin(string? proxy)
    {
        var normalised = Normalised(proxy);
        if (proxy is not null && normalised is null)
        {
            throw new ArgumentException("The proxy's address cannot be read.", nameof(proxy));
        }

        lock (_gate)
        {
            if (_loginFor == normalised)
            {
                return;
            }

            _loginFor = normalised;
            _askedAt = null;
        }

        Raise(ChoiceChanged);
        Raise(Changed);
    }

    /// <summary>
    /// Gives the credentials for the pipeline through Windows: the gate itself, which answers only the proxy the login
    /// is on for, or <see langword="null"/> when the login is off, so that nothing can be given at all.
    /// </summary>
    /// <returns>The credentials for the system's proxy, or <see langword="null"/>.</returns>
    public ICredentials? ProxyCredentials() => LoginFor is null ? null : new GatedCredentials(this);

    /// <summary>
    /// Tells whether the Windows account may be given to a proxy that asks: the login is on for exactly that proxy,
    /// Windows' setting is still that proxy set by hand, and the scheme proves the account without the password.
    /// </summary>
    /// <param name="proxy">The proxy that asks, as the pipeline sends to it.</param>
    /// <param name="scheme">The scheme it asks with, such as <c>Negotiate</c>.</param>
    /// <returns><see langword="true"/> when the account may be given.</returns>
    public bool Allows(Uri? proxy, string? scheme)
    {
        var loginFor = LoginFor;
        return loginFor is not null
            && proxy is not null
            && scheme is not null
            && AllowedSchemes.Contains(scheme, StringComparer.OrdinalIgnoreCase)
            && string.Equals(WindowsProxyText.AddressOf(proxy), loginFor, StringComparison.Ordinal)
            && IsManual(Read(), loginFor);
    }

    /// <summary>Notes that the proxy refused a request with <c>407</c>, so that the settings can say so.</summary>
    public void NoteLoginAsked()
    {
        lock (_gate)
        {
            _askedAt = clock.UtcNow;
        }

        Raise(Changed);
    }

    /// <summary>Notes that a request went through Windows, so that an earlier refusal is no longer said.</summary>
    public void NoteThrough()
    {
        lock (_gate)
        {
            if (_askedAt is null)
            {
                return;
            }

            _askedAt = null;
        }

        Raise(Changed);
    }

    /// <summary>Tells whether a failure is the proxy's refusal with <c>407</c>.</summary>
    /// <param name="exception">The failure.</param>
    /// <returns><see langword="true"/> when the proxy asked for a login.</returns>
    public static bool AsksForLogin(Exception exception)
    {
        for (var inner = exception; inner is not null; inner = inner.InnerException)
        {
            if (inner is HttpRequestException { StatusCode: HttpStatusCode.ProxyAuthenticationRequired })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Tells whether a setting is a proxy set by hand at an address.</summary>
    /// <param name="setting">Windows' setting.</param>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool IsManual(WindowsProxySetting setting, string address) =>
        setting.Kind == WindowsProxyKind.Manual && string.Equals(setting.Address, address, StringComparison.Ordinal);

    /// <summary>Gives an address in the one form it is compared in.</summary>
    /// <param name="proxy">The address, or <see langword="null"/>.</param>
    /// <returns>The address as <c>host:port</c>, or <see langword="null"/>.</returns>
    private static string? Normalised(string? proxy) =>
        string.IsNullOrWhiteSpace(proxy) ? null : WindowsProxyText.AddressOf(proxy);

    /// <summary>Tells the listeners, one at a time; a listener that throws is passed over.</summary>
    /// <param name="listeners">The event's listeners.</param>
    private void Raise(EventHandler? listeners)
    {
        foreach (var handler in listeners?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler)handler)(this, EventArgs.Empty);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A listener's failure never changes the choice.
            }
        }
    }

    /// <summary>
    /// The credentials the pipeline asks when a proxy answers <c>407</c>: the Windows account for the proxy the gate
    /// allows, and nothing for anything else.
    /// </summary>
    /// <param name="login">The gate.</param>
    private sealed class GatedCredentials(WindowsProxyLogin login) : ICredentials
    {
        /// <inheritdoc />
        public NetworkCredential? GetCredential(Uri uri, string authType) =>
            login.Allows(uri, authType) ? CredentialCache.DefaultNetworkCredentials : null;
    }
}
