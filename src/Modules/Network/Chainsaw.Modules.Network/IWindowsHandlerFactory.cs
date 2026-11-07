namespace Chainsaw.Modules.Network;

/// <summary>
/// Builds the HTTP pipeline for the way out through Windows. The tests replace it to see what would be sent, and that
/// nothing reaches it while VPN is chosen.
/// </summary>
internal interface IWindowsHandlerFactory
{
    /// <summary>Builds a pipeline through the computer's own network and its proxy.</summary>
    /// <returns>The pipeline's handler, owned by the caller.</returns>
    HttpMessageHandler Create();
}
