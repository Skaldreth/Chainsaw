using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// A sort of API for a chain: how its height and a block's hash are asked for when an endpoint is tried, how a key is
/// sent, and the endpoints a chain starts with. Another chain (Ethereum's JSON-RPC, TRON's API) only adds its own.
/// </summary>
internal interface ISourceApi
{
    /// <summary>Gets the sort as the settings and the sealed responses name it.</summary>
    SourceApiInfo Info { get; }

    /// <summary>Gets the endpoints the chain starts with until the user changes them.</summary>
    IReadOnlyList<SourceEndpoint> Defaults { get; }

    /// <summary>Builds the request that asks only for the chain's height.</summary>
    /// <param name="address">The endpoint's base address.</param>
    /// <returns>The request, without a key.</returns>
    ExitRequest HeightRequest(Uri address);

    /// <summary>Reads the height from a successful response.</summary>
    /// <param name="body">The body.</param>
    /// <returns>The height.</returns>
    /// <exception cref="FormatException">The body is not a height.</exception>
    long ReadHeight(ReadOnlySpan<byte> body);

    /// <summary>Builds the request for the hash of the block at a height.</summary>
    /// <param name="address">The endpoint's base address.</param>
    /// <param name="height">The height.</param>
    /// <returns>The request, without a key.</returns>
    ExitRequest HashRequest(Uri address, long height);

    /// <summary>Reads a block's hash from a successful response.</summary>
    /// <param name="body">The body.</param>
    /// <returns>The hash as the chain writes it.</returns>
    /// <exception cref="FormatException">The body is not a hash.</exception>
    string ReadHash(ReadOnlySpan<byte> body);

    /// <summary>Gives the header a key from the vault is sent in.</summary>
    /// <param name="secret">The key.</param>
    /// <returns>The header.</returns>
    ExitCredential Credential(string secret);
}
