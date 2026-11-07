namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>The settings of the Bitcoin source.</summary>
public sealed record BitcoinSourceOptions
{
    /// <summary>The Esplora API at mempool.space, the default.</summary>
    public static readonly Uri MempoolSpace = new("https://mempool.space/api/");

    /// <summary>The Esplora API at Blockstream, an alternative.</summary>
    public static readonly Uri Blockstream = new("https://blockstream.info/api/");

    /// <summary>
    /// Gets the Esplora API's base address, <c>https</c> and ending in <c>/</c>. A self-hosted Esplora later is only
    /// another address here.
    /// </summary>
    public Uri EsploraBaseUri { get; init; } = MempoolSpace;

    /// <summary>Gets the network the source answers for; addresses from another network are refused.</summary>
    public BitcoinNetwork Network { get; init; } = BitcoinNetwork.Mainnet;

    /// <summary>
    /// Gets how many blocks deep a transaction must be (its confirmations) before it is never fetched again. Six is
    /// the custom; a reorganisation deeper than that has not happened in practice.
    /// </summary>
    public int SafeConfirmations { get; init; } = 6;
}
