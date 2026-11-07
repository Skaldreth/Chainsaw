namespace Chainsaw.Modules.Network.Contracts;

/// <summary>What sort of tunnel an exit is.</summary>
public enum ExitKind
{
    /// <summary>A WireGuard tunnel, set up from a WireGuard configuration (a <c>.conf</c> file's text).</summary>
    WireGuard,
}
