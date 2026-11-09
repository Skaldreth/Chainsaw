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

    /// <summary>
    /// Gets the chain and network its endpoints answer for, as the sealed responses name it (for example
    /// <c>bitcoin-mainnet</c>), against which a probe checks an endpoint (<see cref="ChainLimits"/>).
    /// </summary>
    string ChainName { get; }

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

    /// <summary>Builds a lookup's request under an endpoint's address.</summary>
    /// <param name="address">The endpoint's base address, ending in <c>/</c>.</param>
    /// <param name="path">The path and query under it, which never name a case.</param>
    /// <returns>The request, without a key.</returns>
    ExitRequest Request(Uri address, string path);

    /// <summary>
    /// Builds a lookup's POST under an endpoint's address, for a sort of API that is asked with a body (a JSON-RPC
    /// call). A sort that is only asked with GET refuses it.
    /// </summary>
    /// <param name="address">The endpoint's base address, ending in <c>/</c>.</param>
    /// <param name="path">The path under it, which never names a case.</param>
    /// <param name="body">The body, which never names a case either.</param>
    /// <returns>The request, without a key.</returns>
    /// <exception cref="NotSupportedException">The sort is not asked with a body.</exception>
    ExitRequest Post(Uri address, string path, string body) =>
        throw new NotSupportedException("The sort of API is only asked with GET.");

    /// <summary>
    /// Gets the pace an endpoint of the sort on the internet gets when the user adds it: Chainsaw's careful default,
    /// or the source's own limit when it has one (a free Etherscan key's three requests a second).
    /// </summary>
    TimeSpan Pace => SourcePace.Public;

    /// <summary>Gives the header a key from the vault is sent in.</summary>
    /// <param name="secret">The key.</param>
    /// <returns>The header.</returns>
    ExitCredential Credential(string secret);

    /// <summary>
    /// Gets whether the sort answers a lookup's path (in the Esplora form the chain's source asks in). A sort that
    /// answers only some, as the user's own Bitcoin Core does, is passed over for the others, which are asked of the
    /// chain's other endpoints; every other sort answers all.
    /// </summary>
    /// <param name="path">The path under the endpoint's address.</param>
    /// <returns><see langword="true"/> when it can be asked.</returns>
    bool Answers(string path) => true;

    /// <summary>
    /// Reads what a response says beyond its HTTP status, before the lookup reads it as an answer: a JSON-RPC error
    /// that means the source does not know what was asked, or why it cannot be used, in Swedish.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <returns>The verdict, or <see langword="null"/> to read the response by its status as every source is.</returns>
    SourceVerdict? Judge(ExitResponse response) => null;
}
