using System.Net;

namespace Chainsaw.Modules.Network;

/// <summary>What the pipeline checks about the tunnel's proxy when it opens a connection to it.</summary>
/// <param name="ProxyIsTunnel">Tells, once connected, whether the port still belongs to the tunnel.</param>
/// <param name="Credentials">The tunnel's credentials, whose password the helper proves it knows.</param>
internal sealed record TunnelCheck(Func<bool> ProxyIsTunnel, NetworkCredential Credentials);
