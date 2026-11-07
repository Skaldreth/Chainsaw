namespace Chainsaw.Modules.Network;

/// <summary>The limits on requests through the exit.</summary>
public sealed class NetworkOptions
{
    /// <summary>
    /// The User-Agent sent with every request. Deliberately the most generic value there is: it says nothing about
    /// Chainsaw, the case, the user or the organisation, so a source cannot tell a lookup from any other client.
    /// </summary>
    public const string UserAgent = "Mozilla/5.0";

    /// <summary>
    /// Gets or sets how long a request may take in all, from sending to the last byte of the body. 30 seconds by
    /// default.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets how long connecting to the tunnel's proxy may take. 10 seconds by default.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the largest response body that is accepted, in bytes. 32 MiB by default, far more than an address
    /// or a transaction from Esplora.
    /// </summary>
    public int MaximumResponseBytes { get; set; } = 32 * 1024 * 1024;

    /// <summary>
    /// Gets or sets whether plain <c>http</c> is allowed. Only the tests turn it on, to talk to a proxy on the
    /// loopback address; the application only sends <c>https</c>, so the exit sees no content.
    /// </summary>
    internal bool AllowPlainHttp { get; set; }
}
