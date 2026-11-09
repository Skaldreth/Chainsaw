using System.Globalization;
using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Esplora, the API of mempool.space and Blockstream that can also be run in-house, as a sort of API for Bitcoin, and
/// for Litecoin (litecoinspace.org runs the same software). The height is <c>blocks/tip/height</c> and a block's hash
/// <c>block-height/:height</c>; a key goes as a bearer token. One sort per network, each with its own defaults.
/// </summary>
internal sealed class EsploraApi : ISourceApi
{
    /// <summary>The sort's code, as it is saved and sealed (the column <c>source</c> has always said it).</summary>
    public const string Code = "esplora";

    /// <summary>The default endpoints' fixed ids, so that the same default is the same endpoint in every
    /// file.</summary>
    private static readonly IReadOnlyDictionary<Uri, (Guid Id, string Name)> Known = new Dictionary<Uri, (Guid, string)>
    {
        [BitcoinSourceOptions.MempoolSpace] = (Guid.Parse("6d0a2f0e-7e1b-4c55-9d0b-8b1f2c4a0001"), "mempool.space"),
        [BitcoinSourceOptions.Blockstream] = (Guid.Parse("6d0a2f0e-7e1b-4c55-9d0b-8b1f2c4a0002"), "Blockstream"),
        [BitcoinSourceOptions.LitecoinSpace] = (Guid.Parse("6d0a2f0e-7e1b-4c55-9d0b-8b1f2c4a0201"), "litecoinspace.org"),
    };

    /// <summary>The chain the endpoints answer for.</summary>
    private readonly Chain _chain;

    /// <summary>Creates the sort with the network's default endpoints from the options.</summary>
    /// <param name="options">The source's settings: Bitcoin's, or Litecoin's.</param>
    /// <exception cref="ArgumentException">A default endpoint is not plain https.</exception>
    public EsploraApi(BitcoinSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _chain = options.Network.ChainOf();
        Info = new SourceApiInfo(Code, "Esplora", _chain);
        Defaults = [.. options.DefaultEndpoints.Select(Default)];
        ChainName = RawResponseStore.NameOf(options.Network);
    }

    /// <inheritdoc/>
    public SourceApiInfo Info { get; }

    /// <inheritdoc/>
    public IReadOnlyList<SourceEndpoint> Defaults { get; }

    /// <inheritdoc/>
    public string ChainName { get; }

    /// <inheritdoc/>
    public ExitRequest HeightRequest(Uri address) => Get(address, "blocks/tip/height");

    /// <inheritdoc/>
    public long ReadHeight(ReadOnlySpan<byte> body)
    {
        var text = Encoding.ASCII.GetString(body).Trim();
        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var height) && height > 0
            ? height
            : throw new FormatException("The height is not a whole number above zero.");
    }

    /// <inheritdoc/>
    public ExitRequest HashRequest(Uri address, long height) =>
        Get(address, "block-height/" + height.ToString(CultureInfo.InvariantCulture));

    /// <inheritdoc/>
    public string ReadHash(ReadOnlySpan<byte> body) =>
        BlockHash.TryParse(Encoding.ASCII.GetString(body).Trim(), out var hash)
            ? hash.Value
            : throw new FormatException("The hash is not 64 hex characters.");

    /// <inheritdoc/>
    public ExitRequest Request(Uri address, string path) => Get(address, path);

    /// <inheritdoc/>
    public ExitCredential Credential(string secret) => new("Authorization", "Bearer " + secret);

    /// <summary>Builds a GET under an endpoint's address.</summary>
    /// <param name="address">The base address, ending in <c>/</c>.</param>
    /// <param name="path">The path, which never names a case.</param>
    /// <returns>The request.</returns>
    public static ExitRequest Get(Uri address, string path) =>
        ExitRequest.Get(new Uri(address, path)) with { Accept = "application/json, text/plain" };

    /// <summary>Makes a default endpoint of an address.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The endpoint, switched on.</returns>
    /// <exception cref="ArgumentException">The address is not plain https.</exception>
    private SourceEndpoint Default(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!SourceAddress.TryRead(address.OriginalString, out var checkedAddress, out _))
        {
            throw new ArgumentException(
                "The Esplora base address must be https without user, query or fragment.", nameof(address));
        }

        var (id, name) = Known.TryGetValue(checkedAddress!, out var known)
            ? known
            : (StableId(checkedAddress!), SourceAddress.NameOf(checkedAddress!));
        return new SourceEndpoint(id, _chain, name, checkedAddress!, Code, null, Enabled: true);
    }

    /// <summary>Gives an address that is not one of the known defaults an id that is the same every start.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The id.</returns>
    private static Guid StableId(Uri address)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(address.AbsoluteUri));
        return new Guid(hash.AsSpan(0, 16));
    }
}
