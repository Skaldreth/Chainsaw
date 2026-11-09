using System.Globalization;
using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// TronGrid, TRON's public API, as a sort of API for TRON: the wallet part every TRON node answers (a transaction, what
/// it did in its block, a block) and TronGrid's own index of an address's history (<c>v1/accounts/…</c>). The height is
/// the newest block, <c>wallet/getblock?detail=false</c> (its header only); a key goes in the header
/// <c>TRON-PRO-API-KEY</c> (TronGrid answers without one, but limits it hard).
/// </summary>
internal sealed class TronGridApi : ISourceApi
{
    /// <summary>The sort's code, as it is saved and sealed.</summary>
    public const string Code = "trongrid";

    /// <summary>The header TronGrid reads its key from.</summary>
    public const string KeyHeader = "TRON-PRO-API-KEY";

    /// <summary>The path of the newest block's header.</summary>
    public const string NewestBlockPath = "wallet/getblock?detail=false";

    /// <summary>The most entries TronGrid gives on a page of an address's history (<c>limit</c>).</summary>
    public const int MostPerPage = 200;

    /// <summary>The default endpoint's fixed id, so that the same default is the same endpoint in every file.</summary>
    private static readonly Guid DefaultId = Guid.Parse("6d0a2f0e-7e1b-4c55-9d0b-8b1f2c4a0201");

    /// <summary>Creates the sort with TRON's default endpoints from the options.</summary>
    /// <param name="options">The TRON source's settings.</param>
    /// <exception cref="ArgumentException">A default endpoint is not plain https.</exception>
    public TronGridApi(TronSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Defaults = [.. options.DefaultEndpoints.Select(address => Default(address, options.DefaultPace))];
    }

    /// <inheritdoc/>
    public SourceApiInfo Info { get; } = new(Code, "TronGrid", Chain.Tron);

    /// <inheritdoc/>
    public IReadOnlyList<SourceEndpoint> Defaults { get; }

    /// <inheritdoc/>
    public string ChainName => TronGridTronSource.ChainName;

    /// <inheritdoc/>
    public ExitRequest HeightRequest(Uri address) => Request(address, NewestBlockPath);

    /// <inheritdoc/>
    public long ReadHeight(ReadOnlySpan<byte> body) => TronGridParser.ParseHeight(body.ToArray()).Height;

    /// <inheritdoc/>
    public ExitRequest HashRequest(Uri address, long height) => Request(address, BlockPath(height));

    /// <inheritdoc/>
    public string ReadHash(ReadOnlySpan<byte> body) => TronGridParser.ParseBlock(body.ToArray(), null).Hash!;

    /// <inheritdoc/>
    public ExitRequest Request(Uri address, string path) =>
        ExitRequest.Get(new Uri(address, path)) with { Accept = "application/json" };

    /// <inheritdoc/>
    public ExitCredential Credential(string secret) => new(KeyHeader, secret);

    /// <summary>The path of the block at a height, its header only.</summary>
    /// <param name="height">The height.</param>
    /// <returns>The path.</returns>
    public static string BlockPath(long height) =>
        "wallet/getblock?id_or_num=" + height.ToString(CultureInfo.InvariantCulture) + "&detail=false";

    /// <summary>Makes a default endpoint of an address.</summary>
    /// <param name="address">The address.</param>
    /// <param name="pace">The shortest time between two requests to it.</param>
    /// <returns>The endpoint, switched on.</returns>
    /// <exception cref="ArgumentException">The address is not plain https.</exception>
    private static SourceEndpoint Default(Uri address, TimeSpan pace)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!SourceAddress.TryRead(address.OriginalString, out var checkedAddress, out _))
        {
            throw new ArgumentException(
                "The TronGrid base address must be https without user, query or fragment.", nameof(address));
        }

        var known = checkedAddress!;
        var id = known == TronSourceOptions.TronGrid
            ? DefaultId
            : new Guid(System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(known.AbsoluteUri)).AsSpan(0, 16));
        var name = known == TronSourceOptions.TronGrid ? "TronGrid" : SourceAddress.NameOf(known);
        return new SourceEndpoint(id, Chain.Tron, name, known, Code, null, Enabled: true) { Interval = pace };
    }
}
