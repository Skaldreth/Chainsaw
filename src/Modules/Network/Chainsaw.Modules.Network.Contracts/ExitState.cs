namespace Chainsaw.Modules.Network.Contracts;

/// <summary>Where the program's connection to its exit stands.</summary>
public enum ExitState
{
    /// <summary>No exit is connected; every request is refused.</summary>
    NotConnected,

    /// <summary>The selected exit is being connected; requests are refused until it is connected.</summary>
    Connecting,

    /// <summary>The selected exit is connected; requests leave through it.</summary>
    Connected,

    /// <summary>The exit could not be connected or the connection was lost; every request is refused.</summary>
    Failed,
}
