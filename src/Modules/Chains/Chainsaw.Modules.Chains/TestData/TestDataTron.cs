#if DEBUG
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.TestData;

/// <summary>
/// Makes the made-up TRON source of the test case (<c>/testdata</c>), only in a Debug build: the real TronGrid lookups
/// and sealing, but asking a made-up chain on this computer instead of an endpoint through the exit. Every sealed
/// response names the source <see cref="TestDataBitcoin.SourceCode"/> and the endpoint
/// <see cref="TestDataBitcoin.EndpointName"/>, as the made-up Bitcoin source's do, so that the case never says that the
/// data came from the chain.
/// </summary>
public static class TestDataTron
{
    /// <summary>
    /// The endpoint's address, under the reserved top-level name <c>.invalid</c> that never resolves (RFC 2606); it is
    /// never sent, since <see cref="Create"/> asks the made-up chain it is given.
    /// </summary>
    public static readonly Uri Address = new("https://testdata.invalid/trongrid/");

    /// <summary>Creates the source over a made-up chain.</summary>
    /// <param name="chain">Answers TronGrid's requests from the made-up chain; it must never go out on the net.</param>
    /// <param name="operators">Who fetched, as in every sealed response.</param>
    /// <param name="clock">The clock.</param>
    /// <returns>The source.</returns>
    public static ITronSource Create(IExitHttp chain, IOperatorProfileStore operators, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(operators);
        ArgumentNullException.ThrowIfNull(clock);
        var sources = new ChainSources([new TestDataTronApi()], chain, null, null, clock, new ChainSourcesOptions());
        return new TronGridTronSource(operators, clock, new TronSourceOptions(), TimeZoneInfo.Local, sources);
    }

    /// <summary>
    /// TronGrid's API in form, under the made-up source's code and name, with one endpoint of its own that is never
    /// one of the user's and is never asked through the exit.
    /// </summary>
    private sealed class TestDataTronApi : ISourceApi
    {
        /// <summary>The endpoint's fixed id, the same in every run so that the sealed responses can be compared.</summary>
        private static readonly Guid EndpointId = Guid.Parse("7e57da7a-0000-4000-8000-000000000002");

        /// <summary>The TronGrid form that the requests and the probe's answers follow.</summary>
        private readonly TronGridApi _tronGrid = new(new TronSourceOptions { DefaultEndpoints = [] });

        /// <inheritdoc/>
        public SourceApiInfo Info { get; } = new(TestDataBitcoin.SourceCode, TestDataBitcoin.SourceName, Chain.Tron);

        /// <inheritdoc/>
        public IReadOnlyList<SourceEndpoint> Defaults { get; } =
        [
            new SourceEndpoint(
                EndpointId, Chain.Tron, TestDataBitcoin.EndpointName, Address, TestDataBitcoin.SourceCode, null,
                Enabled: true)
            {
                Interval = SourcePace.Local,
            },
        ];

        /// <summary>Gets its own name, so that its made-up chain is never checked against the real chain's.</summary>
        public string ChainName => TestDataBitcoin.SourceCode;

        /// <inheritdoc/>
        public ExitRequest HeightRequest(Uri address) => _tronGrid.HeightRequest(address);

        /// <inheritdoc/>
        public long ReadHeight(ReadOnlySpan<byte> body) => _tronGrid.ReadHeight(body);

        /// <inheritdoc/>
        public ExitRequest HashRequest(Uri address, long height) => _tronGrid.HashRequest(address, height);

        /// <inheritdoc/>
        public string ReadHash(ReadOnlySpan<byte> body) => _tronGrid.ReadHash(body);

        /// <inheritdoc/>
        public ExitRequest Request(Uri address, string path) => _tronGrid.Request(address, path);

        /// <inheritdoc/>
        public ExitCredential Credential(string secret) => _tronGrid.Credential(secret);
    }
}
#endif
