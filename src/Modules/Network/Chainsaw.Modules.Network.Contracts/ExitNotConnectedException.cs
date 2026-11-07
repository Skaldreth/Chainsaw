namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// A request was refused because the way out cannot carry it now (principle 4): no exit is connected, or Windows is the
/// way out and the computer has no network. Nothing was sent: Chainsaw never falls back to another way.
/// </summary>
public sealed class ExitNotConnectedException : ExitException
{
    /// <summary>Creates the exception for no exit connected, with its standard Swedish message.</summary>
    public ExitNotConnectedException()
        : base(ExitError.NotConnected)
    {
    }

    /// <summary>Creates the exception for a reason that means nothing was sent.</summary>
    /// <param name="error">Why nothing was sent.</param>
    private ExitNotConnectedException(ExitError error)
        : base(error)
    {
    }

    /// <summary>
    /// Creates the exception for Windows as the way out while the computer has no network, with its own message, so
    /// that offline is said honestly and every caller that already waits for an exit waits for the network too.
    /// </summary>
    /// <returns>The exception, with <see cref="ExitException.Error"/> <see cref="ExitError.Offline"/>.</returns>
    public static ExitNotConnectedException Offline() => new(ExitError.Offline);
}
