namespace Chainsaw.Modules.Network.Contracts;

/// <summary>How Windows finds the proxy that the way out through Windows uses (sketch PX1a).</summary>
public enum WindowsProxyKind
{
    /// <summary>Windows has no proxy for https: lookups go straight from the computer's own network.</summary>
    None,

    /// <summary>
    /// The proxy is set by hand in Windows, and Windows looks for no other: the only kind Chainsaw may log in to.
    /// </summary>
    Manual,

    /// <summary>
    /// Windows finds the proxy by itself, with automatic detection (WPAD) or a script (PAC). Anyone on the same network
    /// can answer that question, so Chainsaw never sends the Windows account to such a proxy.
    /// </summary>
    Automatic,
}
