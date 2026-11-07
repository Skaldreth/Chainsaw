namespace Chainsaw.Modules.Network;

/// <summary>Starts Chainsaw's WireGuard helper. The tests replace it with a process in memory.</summary>
internal interface ITunnelProcessStarter
{
    /// <summary>Starts a new copy of the helper, with nothing on its command line.</summary>
    /// <returns>The running helper.</returns>
    /// <exception cref="TunnelHelperUnavailableException">
    /// The helper is missing, is not the copy Chainsaw was built with, or cannot be started.
    /// </exception>
    ITunnelProcess Start();
}
