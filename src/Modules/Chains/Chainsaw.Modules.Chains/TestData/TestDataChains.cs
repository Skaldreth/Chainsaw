#if DEBUG
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.TestData;

/// <summary>
/// Makes the made-up Litecoin, Zcash, BSC and Solana sources of the test case (<c>/testdata</c>), only in a Debug
/// build: each chain's real lookups and sealing, but asking a made-up chain on this computer instead of an endpoint
/// through the exit. Every sealed response names the source <see cref="TestDataBitcoin.SourceCode"/> and the endpoint
/// <see cref="TestDataBitcoin.EndpointName"/>, as the made-up Bitcoin, TRON and Ethereum sources' do, so that the case
/// never says that the data came from the chain.
/// </summary>
public static class TestDataChains
{
    /// <summary>
    /// The Litecoin endpoint's address, under the reserved top-level name <c>.invalid</c> that never resolves
    /// (RFC 2606); it is never sent, since the source asks the made-up chain it is given.
    /// </summary>
    public static readonly Uri LitecoinAddress = new("https://testdata.invalid/litecoin/");

    /// <summary>The Zcash endpoint's address (Blockbook's form), under <c>.invalid</c>.</summary>
    public static readonly Uri ZcashAddress = new("https://testdata.invalid/zcash/");

    /// <summary>The BSC endpoint's address (Blockscout's form), under <c>.invalid</c>.</summary>
    public static readonly Uri BscAddress = new("https://testdata.invalid/bsc/api/");

    /// <summary>The Solana endpoint's address (its JSON-RPC), under <c>.invalid</c>.</summary>
    public static readonly Uri SolanaAddress = new("https://testdata.invalid/solana/");

    /// <summary>Creates the Litecoin source over a made-up chain in Esplora's form.</summary>
    /// <param name="chain">Answers the requests from the made-up chain; it must never go out on the net.</param>
    /// <param name="operators">Who fetched, as in every sealed response.</param>
    /// <param name="clock">The clock.</param>
    /// <returns>The source.</returns>
    public static ILitecoinSource CreateLitecoin(IExitHttp chain, IOperatorProfileStore operators, IClock clock)
    {
        var options = BitcoinSourceOptions.ForLitecoin() with { DefaultEndpoints = [] };
        var sources = Sources(new EsploraApi(options), Chain.Litecoin, LitecoinAddress, 4, chain, clock);
        return new EsploraLitecoinSource(sources, Checked(operators), clock, options, TimeZoneInfo.Local);
    }

    /// <summary>Creates the Zcash source over a made-up chain in Blockbook's form.</summary>
    /// <param name="chain">Answers the requests from the made-up chain; it must never go out on the net.</param>
    /// <param name="operators">Who fetched, as in every sealed response.</param>
    /// <param name="clock">The clock.</param>
    /// <returns>The source.</returns>
    public static IZcashSource CreateZcash(IExitHttp chain, IOperatorProfileStore operators, IClock clock)
    {
        var options = new ZcashSourceOptions { DefaultEndpoints = [] };
        var sources = Sources(new BlockbookApi(options), Chain.Zcash, ZcashAddress, 5, chain, clock);
        return new BlockbookZcashSource(sources, Checked(operators), clock, options, TimeZoneInfo.Local);
    }

    /// <summary>Creates the BSC source over a made-up chain in Blockscout's form.</summary>
    /// <param name="chain">Answers the requests from the made-up chain; it must never go out on the net.</param>
    /// <param name="operators">Who fetched, as in every sealed response.</param>
    /// <param name="clock">The clock.</param>
    /// <returns>The source.</returns>
    public static IBscSource CreateBsc(IExitHttp chain, IOperatorProfileStore operators, IClock clock)
    {
        var options = new BscSourceOptions();
        var sources = Sources(new BlockscoutApi(options), Chain.Bsc, BscAddress, 6, chain, clock);
        return new BlockscoutBscSource(Checked(operators), clock, options, TimeZoneInfo.Local, sources);
    }

    /// <summary>Creates the Solana source over a made-up chain in its JSON-RPC's form.</summary>
    /// <param name="chain">Answers the requests from the made-up chain; it must never go out on the net.</param>
    /// <param name="operators">Who fetched, as in every sealed response.</param>
    /// <param name="clock">The clock.</param>
    /// <returns>The source.</returns>
    public static ISolanaSource CreateSolana(IExitHttp chain, IOperatorProfileStore operators, IClock clock)
    {
        var options = new SolanaSourceOptions { DefaultEndpoints = [] };
        var sources = Sources(new SolanaRpcApi(options), Chain.Solana, SolanaAddress, 7, chain, clock);
        return new SolanaRpcSource(Checked(operators), options, TimeZoneInfo.Local, sources);
    }

    /// <summary>
    /// Gives the endpoints of one made-up source: the real API's form under the made-up source's name, asked through
    /// the made-up chain only.
    /// </summary>
    /// <param name="form">The real API's form.</param>
    /// <param name="on">The chain.</param>
    /// <param name="address">The endpoint's address.</param>
    /// <param name="number">The last part of the endpoint's fixed id, one per made-up source.</param>
    /// <param name="chain">Answers the requests from the made-up chain.</param>
    /// <param name="clock">The clock.</param>
    /// <returns>The endpoints.</returns>
    private static ChainSources Sources(
        ISourceApi form, Chain on, Uri address, int number, IExitHttp chain, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(clock);
        var id = Guid.Parse($"7e57da7a-0000-4000-8000-{number:D12}");
        return new ChainSources(
            [new TestDataSourceApi(form, on, address, id)], chain, null, null, clock, new ChainSourcesOptions());
    }

    /// <summary>Checks that the operator profile is given.</summary>
    /// <param name="operators">The operator profile.</param>
    /// <returns>The same profile.</returns>
    private static IOperatorProfileStore Checked(IOperatorProfileStore operators) =>
        operators ?? throw new ArgumentNullException(nameof(operators));
}
#endif
