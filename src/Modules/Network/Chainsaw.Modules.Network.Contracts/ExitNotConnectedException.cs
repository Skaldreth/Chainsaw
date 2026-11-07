namespace Chainsaw.Modules.Network.Contracts;

/// <summary>
/// A request was refused because no exit is connected (principle 4). Nothing was sent: Chainsaw never falls back to
/// the computer's own network.
/// </summary>
public sealed class ExitNotConnectedException : ExitException
{
    /// <summary>Creates the exception with its standard Swedish message.</summary>
    public ExitNotConnectedException()
        : base(ExitError.NotConnected)
    {
    }
}
