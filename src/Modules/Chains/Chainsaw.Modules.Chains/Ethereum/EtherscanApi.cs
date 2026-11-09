using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Etherscan's API (version 2, one address for every chain it reads, told apart by <c>chainid</c>) as a sort of API for
/// Ethereum. Its account module answers alike Blockscout's Etherscan-compatible API, so the Ethereum source asks it
/// with the same paths and reads the answers with the same parser: an address's histories, its ether and a
/// transaction's internal transfers. The height and a block's hash come from its JSON-RPC proxy
/// (<c>eth_blockNumber</c>, <c>eth_getBlockByNumber</c>). What Etherscan gives only for a paid key (an address's tokens
/// and a token's details) or not at all (Blockscout's <c>gettxinfo</c>) is left to the chain's other endpoints, so an
/// endpoint here never claims to answer it.
/// </summary>
/// <remarks>
/// Etherscan takes its key only in the address, as <c>apikey</c>. The key goes as an
/// <see cref="ExitCredential.Query"/> credential: the exit adds it to the message it sends, and the response keeps
/// the address without it, so the key is never sealed in a case.
/// </remarks>
internal sealed class EtherscanApi : ISourceApi
{
    /// <summary>The sort's code, as it is saved and sealed.</summary>
    public const string Code = "etherscan";

    /// <summary>The name of the key in the vault that Etherscan's default endpoint uses.</summary>
    public const string KeyService = "Etherscan";

    /// <summary>
    /// The pace of Etherscan's default endpoint: at most three requests a second, the limit of a free key, so that
    /// Chainsaw never asks faster than the key allows.
    /// </summary>
    public static readonly TimeSpan FreeKeyPace = TimeSpan.FromMilliseconds(350);

    /// <summary>The default endpoint's fixed id, so that the same default is the same endpoint in every file.</summary>
    private static readonly Guid DefaultId = Guid.Parse("6d0a2f0e-7e1b-4c55-9d0b-8b1f2c4a0201");

    /// <summary>The paths the account module answers that Etherscan does not give for a free key, or at all.</summary>
    private static readonly string[] NotAnswered =
        ["action=gettxinfo", "action=tokenlist", "module=token"];

    /// <summary>Creates the sort for Ethereum, with Etherscan as an endpoint that is off until a key is added.</summary>
    /// <param name="options">The Ethereum source's settings.</param>
    public EtherscanApi(EthereumSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Info = new SourceApiInfo(Code, "Etherscan", Chain.Ethereum);
        Defaults =
        [
            new SourceEndpoint(DefaultId, Chain.Ethereum, "Etherscan", options.Etherscan, Code, KeyService,
                Enabled: false) { Interval = FreeKeyPace },
        ];
    }

    /// <inheritdoc/>
    public SourceApiInfo Info { get; }

    /// <inheritdoc/>
    public IReadOnlyList<SourceEndpoint> Defaults { get; }

    /// <inheritdoc/>
    public string ChainName => BlockscoutEthereumSource.ChainName;

    /// <summary>The chain id Etherscan knows Ethereum by.</summary>
    private static string ChainId => "1";

    /// <inheritdoc/>
    public ExitRequest HeightRequest(Uri address) => Request(address, BlockscoutApi.HeightPath);

    /// <inheritdoc/>
    public long ReadHeight(ReadOnlySpan<byte> body) => BlockscoutParser.ParseHeight(body.ToArray()).Height;

    /// <inheritdoc/>
    public ExitRequest HashRequest(Uri address, long height) => Request(address, BlockscoutApi.BlockPath(height));

    /// <inheritdoc/>
    public string ReadHash(ReadOnlySpan<byte> body) => BlockscoutParser.ParseBlock(body.ToArray(), null).Hash!;

    /// <inheritdoc/>
    public ExitRequest Request(Uri address, string path)
    {
        ArgumentNullException.ThrowIfNull(address);
        var query = "chainid=" + ChainId + "&" + Translate(path);
        var builder = new UriBuilder(address) { Query = query };
        return ExitRequest.Get(builder.Uri) with { Accept = "application/json" };
    }

    /// <inheritdoc/>
    public ExitCredential Credential(string secret) => ExitCredential.Query("apikey", secret);

    /// <inheritdoc/>
    public bool Answers(string path) =>
        !NotAnswered.Any(part => path.Contains(part, StringComparison.Ordinal));

    /// <summary>
    /// Turns one of the Ethereum source's paths, written for Blockscout, into the query Etherscan answers alike: the
    /// height and the blocks through the JSON-RPC proxy, an address's ether at the newest block (Etherscan wants the
    /// tag said), and the account module's other paths as they are.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The query, without <c>chainid</c>.</returns>
    /// <exception cref="ArgumentException">The path is not one Etherscan answers.</exception>
    internal static string Translate(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path == BlockscoutApi.HeightPath)
        {
            return "module=proxy&action=eth_blockNumber";
        }

        if (path == BlockscoutApi.NewestBlockPath)
        {
            return "module=proxy&action=eth_getBlockByNumber&tag=latest&boolean=false";
        }

        const string blocks = "v2/blocks/";
        if (path.StartsWith(blocks, StringComparison.Ordinal)
            && long.TryParse(path.AsSpan(blocks.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var height))
        {
            return "module=proxy&action=eth_getBlockByNumber&tag=0x"
                + height.ToString("x", CultureInfo.InvariantCulture) + "&boolean=false";
        }

        if (!path.StartsWith('?'))
        {
            throw new ArgumentException("Etherscan answers only the account module and blocks.", nameof(path));
        }

        var query = path[1..];
        return query.Contains("action=balance&", StringComparison.Ordinal) ? query + "&tag=latest" : query;
    }
}
