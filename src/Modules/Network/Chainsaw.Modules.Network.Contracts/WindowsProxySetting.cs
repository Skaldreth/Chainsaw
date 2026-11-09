namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// What Windows is set up with for the proxy, as Chainsaw reads it: Chainsaw never changes it (sketch PX1a).
/// </summary>
/// <param name="Kind">How the proxy is found.</param>
/// <param name="Address">
/// The proxy https lookups go through, as <c>host:port</c> in lower case, when it is set by hand; else
/// <see langword="null"/>, since a proxy found automatically is known only when a request asks for it.
/// </param>
public sealed record WindowsProxySetting(WindowsProxyKind Kind, string? Address)
{
    /// <summary>Gets the setting without a proxy.</summary>
    public static WindowsProxySetting None { get; } = new(WindowsProxyKind.None, null);
}
