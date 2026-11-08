#if DEBUG
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.TestData;

/// <summary>
/// Makes the made-up Bitcoin source of the test case (<c>/testdata</c>), only in a Debug build: the real Esplora
/// lookups, sealing and checks against the transactions' own bytes, but asking a made-up chain on this computer instead
/// of an endpoint through the exit. Every sealed response names the source <see cref="SourceCode"/> and the endpoint
/// <see cref="EndpointName"/>, so that the case never says that the data came from the chain.
/// </summary>
public static class TestDataBitcoin
{
    /// <summary>The source's code in every sealed response.</summary>
    public const string SourceCode = "testdata";

    /// <summary>The source's name as the settings would show it.</summary>
    public const string SourceName = "Påhittade data";

    /// <summary>The endpoint's name in every sealed response.</summary>
    public const string EndpointName = "testdata (påhittade data, bara Debug)";

    /// <summary>
    /// The endpoint's address. The top-level name <c>.invalid</c> is reserved and never resolves (RFC 2606), so the
    /// address could never reach anything even if it were sent; it is not, since <see cref="Create"/> asks the made-up
    /// chain it is given.
    /// </summary>
    public static readonly Uri Address = new("https://testdata.invalid/esplora/");

    /// <summary>Creates the source over a made-up chain.</summary>
    /// <param name="chain">Answers the Esplora requests from the made-up chain; it must never go out on the net.</param>
    /// <param name="operators">Who fetched, as in every sealed response.</param>
    /// <param name="clock">The clock.</param>
    /// <returns>The source.</returns>
    public static IBitcoinSource Create(IExitHttp chain, IOperatorProfileStore operators, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(operators);
        ArgumentNullException.ThrowIfNull(clock);
        var sources = new ChainSources([new TestDataApi()], chain, null, null, clock, new ChainSourcesOptions());
        var options = new BitcoinSourceOptions { DefaultEndpoints = [] };
        var source = new EsploraBitcoinSource(sources, operators, clock, options, TimeZoneInfo.Local);
        return new ProbedSource(source, sources);
    }

    /// <summary>
    /// The Esplora source with its one endpoint tried once before the first lookup that needs an endpoint in step
    /// with the chain (the monitoring's), as the application tries its endpoints at start.
    /// </summary>
    /// <param name="inner">The Esplora source.</param>
    /// <param name="sources">The made-up endpoint.</param>
    private sealed class ProbedSource(IBitcoinSource inner, ChainSources sources) : IBitcoinSource
    {
        /// <summary>Guards the one probe.</summary>
        private readonly SemaphoreSlim _probing = new(1, 1);

        /// <summary>Whether the endpoint has been tried.</summary>
        private bool _probed;

        /// <inheritdoc/>
        public BitcoinNetwork Network => inner.Network;

        /// <inheritdoc/>
        public Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
            ICase @case, BitcoinAddress address, Txid? after = null, CancellationToken cancellationToken = default) =>
            inner.GetAddressTransactionsAsync(@case, address, after, cancellationToken);

        /// <inheritdoc/>
        public async Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
            ICase @case, BitcoinAddress address, Txid? after, LookupMode mode,
            CancellationToken cancellationToken = default)
        {
            if (mode == LookupMode.InStep)
            {
                await ProbeOnceAsync(cancellationToken).ConfigureAwait(false);
            }

            return await inner.GetAddressTransactionsAsync(@case, address, after, mode, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public Task<BitcoinLookup<BitcoinTransaction>> GetTransactionAsync(
            ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default) =>
            inner.GetTransactionAsync(@case, txid, mode, cancellationToken);

        /// <inheritdoc/>
        public Task<BitcoinLookup<BitcoinTransaction>> VerifyTransactionAsync(
            ICase @case, Txid txid, CancellationToken cancellationToken = default) =>
            inner.VerifyTransactionAsync(@case, txid, cancellationToken);

        /// <inheritdoc/>
        public Task<BitcoinLookup<BitcoinAddressSummary>> GetAddressSummaryAsync(
            ICase @case, BitcoinAddress address, CancellationToken cancellationToken = default) =>
            inner.GetAddressSummaryAsync(@case, address, cancellationToken);

        /// <inheritdoc/>
        public Task<BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>> GetOutputSpendsAsync(
            ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default) =>
            inner.GetOutputSpendsAsync(@case, txid, mode, cancellationToken);

        /// <inheritdoc/>
        public Task<BitcoinLookup<BitcoinBlock>> GetBlockAsync(
            ICase @case, BlockHash hash, CancellationToken cancellationToken = default) =>
            inner.GetBlockAsync(@case, hash, cancellationToken);

        /// <inheritdoc/>
        public Task<BitcoinLookup<BitcoinTip>> GetTipHeightAsync(
            ICase @case, CancellationToken cancellationToken = default) =>
            inner.GetTipHeightAsync(@case, cancellationToken);

        /// <inheritdoc/>
        public async Task<BitcoinLookup<BitcoinTip>> GetTipHeightAsync(
            ICase @case, LookupMode mode, CancellationToken cancellationToken = default)
        {
            if (mode == LookupMode.InStep)
            {
                await ProbeOnceAsync(cancellationToken).ConfigureAwait(false);
            }

            return await inner.GetTipHeightAsync(@case, mode, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public Task<BitcoinLookup<ChainTip>> GetTipAsync(ICase @case, CancellationToken cancellationToken = default) =>
            inner.GetTipAsync(@case, cancellationToken);

        /// <inheritdoc/>
        public Task<BitcoinLookup<ChainTip>> GetTipAsync(
            ICase @case, LookupMode mode, CancellationToken cancellationToken = default) =>
            inner.GetTipAsync(@case, mode, cancellationToken);

        /// <inheritdoc/>
        public Task<IReadOnlyList<BlockChange>> GetBlockChangesAsync(
            ICase @case, CancellationToken cancellationToken = default) =>
            inner.GetBlockChangesAsync(@case, cancellationToken);

        /// <summary>Tries the endpoint the first time it is needed in step, and never again.</summary>
        /// <param name="cancellationToken">Stops the wait.</param>
        /// <returns>A task that completes when the endpoint has been tried.</returns>
        private async Task ProbeOnceAsync(CancellationToken cancellationToken)
        {
            await _probing.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_probed)
                {
                    await sources.ProbeAsync(Chain.Bitcoin, cancellationToken).ConfigureAwait(false);
                    _probed = true;
                }
            }
            finally
            {
                _probing.Release();
            }
        }
    }
}
#endif
