using System.Net;
using Chainsaw.Modules.Network.Contracts;

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
    /// <param name="pin">
    /// The fingerprint of the one certificate the node may show (<see cref="CertificatePin"/>, sketch KL3b), or
    /// <see langword="null"/> to check the certificate against the roots installed on the computer.
    /// </param>
    /// <returns>The pipeline's handler, owned by the caller.</returns>
    HttpMessageHandler Create(string? pin);

    /// <summary>
    /// Makes only the TLS handshake with the node, the same way the pipeline connects, and reads the certificate it
    /// shows without trusting it; nothing else is sent.
    /// </summary>
    /// <param name="endpoint">The node's host and port.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    /// <returns>The certificate.</returns>
    Task<NodeCertificate> ReadCertificateAsync(DnsEndPoint endpoint, CancellationToken cancellationToken);
}
