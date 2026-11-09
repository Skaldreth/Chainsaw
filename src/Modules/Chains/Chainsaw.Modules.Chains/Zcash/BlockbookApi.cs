using System.Globalization;
using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Blockbook's API (Trezor's open indexer, API v2) as spoken for Zcash: the height from <c>api/</c> and a block's
/// hash from <c>api/v2/block-index/:height</c>, the probes that say nothing about a case. The default endpoint is the
/// open Blockbook for Zcash that Zelcore runs (<see cref="ZcashSourceOptions.ZelcoreBlockbook"/>), which answers
/// without a key; the user can add their own under <em>Källor</em>.
/// </summary>
internal sealed class BlockbookApi : ISourceApi
{
    /// <summary>The API's code, as an endpoint names it.</summary>
    public const string Code = "blockbook";

    /// <summary>The default endpoints' stable ids, so that a sealed answer names the same endpoint across runs.</summary>
    private static readonly IReadOnlyDictionary<Uri, (Guid Id, string Name)> Known = new Dictionary<Uri, (Guid, string)>
    {
        [ZcashSourceOptions.ZelcoreBlockbook] =
            (Guid.Parse("6d0a2f0e-7e1b-4c55-9d0b-8b1f2c4a0301"), "Zelcore Blockbook"),
    };

    /// <summary>Creates the API for Zcash.</summary>
    /// <param name="options">The settings, whose default endpoints come first.</param>
    public BlockbookApi(ZcashSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Defaults = [.. options.DefaultEndpoints.Select(Default)];
    }

    /// <inheritdoc/>
    public SourceApiInfo Info { get; } = new(Code, "Blockbook", Chain.Zcash);

    /// <inheritdoc/>
    public IReadOnlyList<SourceEndpoint> Defaults { get; }

    /// <inheritdoc/>
    public string ChainName => RawResponseStore.ZcashChainName;

    /// <inheritdoc/>
    public ExitRequest HeightRequest(Uri address) => Get(address, "api/");

    /// <inheritdoc/>
    public long ReadHeight(ReadOnlySpan<byte> body)
    {
        try
        {
            return BlockbookParser.ParseTip(body).Height;
        }
        catch (EsploraFormatException exception)
        {
            throw new FormatException("The height cannot be read.", exception);
        }
    }

    /// <inheritdoc/>
    public ExitRequest HashRequest(Uri address, long height) =>
        Get(address, "api/v2/block-index/" + height.ToString(CultureInfo.InvariantCulture));

    /// <inheritdoc/>
    public string ReadHash(ReadOnlySpan<byte> body)
    {
        try
        {
            return BlockbookParser.ParseBlockIndex(body).Value;
        }
        catch (EsploraFormatException exception)
        {
            throw new FormatException("The hash cannot be read.", exception);
        }
    }

    /// <inheritdoc/>
    public ExitRequest Request(Uri address, string path) => Get(address, path);

    /// <inheritdoc/>
    public ExitCredential Credential(string secret) => new("Authorization", "Bearer " + secret);

    /// <summary>Makes a GET request for a path below an endpoint.</summary>
    /// <param name="address">The endpoint's base address.</param>
    /// <param name="path">The path, relative.</param>
    /// <returns>The request.</returns>
    private static ExitRequest Get(Uri address, string path) =>
        ExitRequest.Get(new Uri(address, path)) with { Accept = "application/json" };

    /// <summary>Makes a default endpoint of an address.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The endpoint.</returns>
    /// <exception cref="ArgumentException">The address is not https of its own.</exception>
    private static SourceEndpoint Default(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!SourceAddress.TryRead(address.OriginalString, out var checkedAddress, out _))
        {
            throw new ArgumentException(
                "The Blockbook base address must be https without user, query or fragment.", nameof(address));
        }

        var (id, name) = Known.TryGetValue(checkedAddress!, out var known)
            ? known
            : (StableId(checkedAddress!), SourceAddress.NameOf(checkedAddress!));
        return new SourceEndpoint(id, Chain.Zcash, name, checkedAddress!, Code, null, Enabled: true);
    }

    /// <summary>Gives an address a stable id, so that the same default is the same endpoint across runs.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The id.</returns>
    private static Guid StableId(Uri address)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(address.AbsoluteUri));
        return new Guid(hash.AsSpan(0, 16));
    }
}
