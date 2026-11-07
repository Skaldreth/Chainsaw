namespace Chainsaw.Modules.Network;

/// <summary>
/// Builds the HTTP pipeline to the user's own node on the computer or a private network. The tests replace it to see
/// what would be sent.
/// </summary>
internal interface ILocalHandlerFactory
{
    /// <summary>
    /// Builds a pipeline that connects only to addresses in the user's own network, and never through a proxy.
    /// </summary>
    /// <returns>The pipeline's handler, owned by the caller.</returns>
    HttpMessageHandler Create();
}
