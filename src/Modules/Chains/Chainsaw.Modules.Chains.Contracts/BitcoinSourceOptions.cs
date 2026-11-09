namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>The settings of the Bitcoin source.</summary>
public sealed record BitcoinSourceOptions
{
    /// <summary>The Esplora API at mempool.space, the default.</summary>
    public static readonly Uri MempoolSpace = new("https://mempool.space/api/");

    /// <summary>The Esplora API at Blockstream, an alternative.</summary>
    public static readonly Uri Blockstream = new("https://blockstream.info/api/");

    /// <summary>
    /// Gets Bitcoin's endpoints until the user changes them in the settings (sketch KL1a): Esplora at mempool.space and
    /// at Blockstream, in that order. Each is <c>https</c>; a self-hosted Esplora is only another endpoint.
    /// </summary>
    public IReadOnlyList<Uri> DefaultEndpoints { get; init; } = [MempoolSpace, Blockstream];

    /// <summary>
    /// Gets the first default endpoint, or sets it as the only one (as a test or a single in-house Esplora does).
    /// </summary>
    public Uri EsploraBaseUri
    {
        get => DefaultEndpoints[0];
        init => DefaultEndpoints = [value];
    }

    /// <summary>Gets the network the source answers for; addresses from another network are refused.</summary>
    public BitcoinNetwork Network { get; init; } = BitcoinNetwork.Mainnet;

    /// <summary>
    /// Gets how many blocks deep a transaction must be (its confirmations) before it is never fetched again. Six is
    /// the custom; a reorganisation deeper than that has not happened in practice.
    /// </summary>
    public int SafeConfirmations { get; init; } = 6;

    /// <summary>
    /// Gets whether a block header's target must be within the network's limit (powLimit). On, always, in Chainsaw;
    /// only the tests whose recorded blocks were mined at a test network's ease turn it off.
    /// </summary>
    internal bool CheckWorkLimit { get; init; } = true;
}
