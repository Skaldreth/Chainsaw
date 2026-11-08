using System.Globalization;
using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Blockscout, the open-source explorer for Ethereum that can also be run in-house, as a sort of API for Ethereum: its
/// Etherscan-compatible API (<c>?module=…&amp;action=…</c>) for histories, transactions and tokens, and its block
/// endpoint (<c>v2/blocks/:height</c>) for a block's hash. The height is
/// <c>module=block&amp;action=eth_block_number</c>; a key goes as a bearer token (the open instance needs none).
/// </summary>
internal sealed class BlockscoutApi : ISourceApi
{
    /// <summary>The sort's code, as it is saved and sealed.</summary>
    public const string Code = "blockscout";

    /// <summary>The default endpoint's fixed id, so that the same default is the same endpoint in every file.</summary>
    private static readonly Guid DefaultId = Guid.Parse("6d0a2f0e-7e1b-4c55-9d0b-8b1f2c4a0101");

    /// <summary>Creates the sort with Ethereum's default endpoints from the options.</summary>
    /// <param name="options">The Ethereum source's settings.</param>
    /// <exception cref="ArgumentException">A default endpoint is not plain https.</exception>
    public BlockscoutApi(EthereumSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Defaults = [.. options.DefaultEndpoints.Select(Default)];
    }

    /// <inheritdoc/>
    public SourceApiInfo Info { get; } = new(Code, "Blockscout", Chain.Ethereum);

    /// <inheritdoc/>
    public IReadOnlyList<SourceEndpoint> Defaults { get; }

    /// <inheritdoc/>
    public string ChainName => BlockscoutEthereumSource.ChainName;

    /// <summary>The path of the chain's height.</summary>
    public static string HeightPath => "?module=block&action=eth_block_number";

    /// <summary>The path of the newest blocks the source has read, newest first, each with its height and hash.</summary>
    public static string NewestBlockPath => "v2/blocks?type=block";

    /// <inheritdoc/>
    public ExitRequest HeightRequest(Uri address) => Request(address, HeightPath);

    /// <inheritdoc/>
    public long ReadHeight(ReadOnlySpan<byte> body) => BlockscoutParser.ParseHeight(body.ToArray()).Height;

    /// <inheritdoc/>
    public ExitRequest HashRequest(Uri address, long height) => Request(address, BlockPath(height));

    /// <inheritdoc/>
    public string ReadHash(ReadOnlySpan<byte> body) => BlockscoutParser.ParseBlock(body.ToArray(), null).Hash!;

    /// <inheritdoc/>
    public ExitRequest Request(Uri address, string path) =>
        ExitRequest.Get(new Uri(address, path)) with { Accept = "application/json" };

    /// <inheritdoc/>
    public ExitCredential Credential(string secret) => new("Authorization", "Bearer " + secret);

    /// <summary>The path of the block at a height.</summary>
    /// <param name="height">The height.</param>
    /// <returns>The path.</returns>
    public static string BlockPath(long height) => "v2/blocks/" + height.ToString(CultureInfo.InvariantCulture);

    /// <summary>Makes a default endpoint of an address.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The endpoint, switched on.</returns>
    /// <exception cref="ArgumentException">The address is not plain https.</exception>
    private static SourceEndpoint Default(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!SourceAddress.TryRead(address.OriginalString, out var checkedAddress, out _))
        {
            throw new ArgumentException(
                "The Blockscout base address must be https without user, query or fragment.", nameof(address));
        }

        var known = checkedAddress!;
        var id = known == EthereumSourceOptions.Blockscout
            ? DefaultId
            : new Guid(System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(known.AbsoluteUri)).AsSpan(0, 16));
        var name = known == EthereumSourceOptions.Blockscout ? "Blockscout" : SourceAddress.NameOf(known);
        return new SourceEndpoint(id, Chain.Ethereum, name, known, Code, null, Enabled: true);
    }
}
