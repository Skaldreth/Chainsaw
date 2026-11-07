namespace Chainsaw.Modules.Network.Contracts;

/// <summary>The HTTP methods a request through an exit may use.</summary>
public enum ExitMethod
{
    /// <summary>GET, without a body (for example an Esplora lookup).</summary>
    Get,

    /// <summary>POST, with a body (for example a JSON-RPC call).</summary>
    Post,
}
