using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.Modules.Network;

/// <summary>The limits on requests through the exit.</summary>
public sealed class NetworkOptions
{
    /// <summary>
    /// The User-Agent sent with every request. Deliberately the most generic value there is: it says nothing about
    /// Chainsaw, the case, the user or the organisation, so a source cannot tell a lookup from any other client.
    /// </summary>
    public const string UserAgent = "Mozilla/5.0";

    /// <summary>The longest time limit that is accepted; a lookup that needs longer is broken.</summary>
    private static readonly TimeSpan LongestTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets or sets how long a request may take in all, from sending to the last byte of the body. 30 seconds by
    /// default.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets how long connecting to the tunnel's proxy may take. 10 seconds by default.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets how long the WireGuard helper may take to start the tunnel, from its start until the exit's server
    /// has answered a handshake. 30 seconds by default: the helper itself waits at most 15 seconds for the handshake
    /// and 10 for the server's name.
    /// </summary>
    public TimeSpan TunnelStartTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the largest response body that is accepted, in bytes. 32 MiB by default, far more than an address
    /// or a transaction from Esplora.
    /// </summary>
    public int MaximumResponseBytes { get; set; } = 32 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the way out when Chainsaw starts, read from the application's settings; VPN by default, the safest.
    /// It is changed afterwards with <see cref="IExitRoute.Choose"/>.
    /// </summary>
    public ExitRoute Route { get; set; } = ExitRoute.Vpn;

    /// <summary>
    /// Gets or sets whether plain <c>http</c> is allowed. Only the tests turn it on, to talk to a proxy on the
    /// loopback address; the application only sends <c>https</c>, so the exit sees no content.
    /// </summary>
    internal bool AllowPlainHttp { get; set; }

    /// <summary>
    /// Gets or sets whether addresses on the computer or a private network may be asked for. Only the tests turn it on;
    /// a lookup never needs them, and a tunnel could reach them on its own side.
    /// </summary>
    internal bool AllowLocalTargets { get; set; }

    /// <summary>
    /// Gets or sets where the WireGuard helper is. Only the tests set it; Chainsaw uses the helper next to the program.
    /// </summary>
    internal string? TunnelHelperPath { get; set; }

    /// <summary>
    /// Gets or sets the SHA-256 the helper must have. Only the tests set it; Chainsaw uses the hash it was built with.
    /// </summary>
    internal byte[]? TunnelHelperSha256 { get; set; }

    /// <summary>Refuses limits that would make every request fail or never end.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A limit is zero, negative or too long, or the way is unknown.
    /// </exception>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(RequestTimeout, TimeSpan.Zero, nameof(RequestTimeout));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(RequestTimeout, LongestTimeout, nameof(RequestTimeout));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ConnectTimeout, TimeSpan.Zero, nameof(ConnectTimeout));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ConnectTimeout, LongestTimeout, nameof(ConnectTimeout));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            TunnelStartTimeout, TimeSpan.Zero, nameof(TunnelStartTimeout));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(TunnelStartTimeout, LongestTimeout, nameof(TunnelStartTimeout));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(MaximumResponseBytes, 0, nameof(MaximumResponseBytes));
        if (!Enum.IsDefined(Route))
        {
            throw new ArgumentOutOfRangeException(nameof(Route), Route, "Unknown way out.");
        }
    }
}
