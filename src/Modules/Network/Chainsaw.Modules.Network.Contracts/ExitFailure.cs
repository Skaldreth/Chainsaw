namespace Chainsaw.Modules.Network.Contracts;

/// <summary>Why an exit is in <see cref="ExitState.Failed"/>.</summary>
public enum ExitFailure
{
    /// <summary>The tunnel for this sort of exit is not available (not built or not installed).</summary>
    TunnelUnavailable,

    /// <summary>The tunnel could not be started, for example because the configuration was refused.</summary>
    ConnectFailed,

    /// <summary>The tunnel stopped after it was connected.</summary>
    ConnectionLost,
}
