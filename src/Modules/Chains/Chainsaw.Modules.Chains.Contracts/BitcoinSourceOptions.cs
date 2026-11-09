namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>The settings of an Esplora source: Bitcoin's, or Litecoin's (<see cref="ForLitecoin"/>).</summary>
public sealed record BitcoinSourceOptions
{
    /// <summary>
    /// The Esplora API at litecoinspace.org, Litecoin's default: mempool.space's software on an Esplora backend for
    /// Litecoin, open without a key (tried 2026-11-09: <c>blocks/tip/height</c>, <c>block-height/0</c> gives Litecoin's
    /// first block, and the addresses are written <c>L…</c>, <c>M…</c> and <c>ltc1…</c>).
    /// </summary>
    public static readonly Uri LitecoinSpace = new("https://litecoinspace.org/api/");

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

    /// <summary>
    /// Gives Litecoin's settings: litecoinspace.org as the endpoint until the user changes it, Litecoin's network, and
    /// twelve blocks (half an hour, as Litecoin's blocks come every two and a half minutes) before what rests on a
    /// block is never fetched again: Litecoin has less work behind each block than Bitcoin, so a little more depth.
    /// </summary>
    /// <returns>The settings.</returns>
    public static BitcoinSourceOptions ForLitecoin() => new()
    {
        DefaultEndpoints = [LitecoinSpace],
        Network = BitcoinNetwork.Litecoin,
        SafeConfirmations = 12,
    };

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
