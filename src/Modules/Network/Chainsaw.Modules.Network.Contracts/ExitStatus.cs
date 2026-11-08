namespace Chainsaw.Modules.Network.Contracts;

/// <summary>The state of the program's exit, as shown in the title bar.</summary>
/// <param name="State">Where the connection stands.</param>
/// <param name="ExitId">
/// The exit the state is about, or <see langword="null"/> when <see cref="ExitState.NotConnected"/> after a disconnect
/// or before any connection.
/// </param>
/// <param name="Failure">Why the state is <see cref="ExitState.Failed"/>; <see langword="null"/> otherwise.</param>
public sealed record ExitStatus(ExitState State, Guid? ExitId, ExitFailure? Failure)
{
    /// <summary>
    /// Gets the line of the exit's file that was refused, counted from 1, when <see cref="Failure"/> is
    /// <see cref="ExitFailure.ConfigurationRefused"/> and the helper named one; never the line's content.
    /// </summary>
    public int? RefusedLine { get; init; }

    /// <summary>Gets the status before any exit has been connected.</summary>
    public static ExitStatus NotConnected { get; } = new(ExitState.NotConnected, null, null);
}
