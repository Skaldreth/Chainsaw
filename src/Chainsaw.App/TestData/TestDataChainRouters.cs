#if DEBUG
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.TestData;

/// <summary>
/// The Litecoin source Chainsaw uses in a Debug build: the made-up source for a test case, and the real one
/// (litecoinspace.org through the exit) for every other case, as <see cref="TestDataBitcoinRouter"/> does for Bitcoin.
/// </summary>
/// <param name="real">The real source.</param>
/// <param name="testData">The made-up source, made the first time a test case asks.</param>
public sealed class TestDataLitecoinRouter(ILitecoinSource real, Lazy<ILitecoinSource> testData)
    : TestDataBitcoinRouter(real, new Lazy<IBitcoinSource>(() => testData.Value)), ILitecoinSource;

/// <summary>
/// The Zcash source Chainsaw uses in a Debug build: the made-up source for a test case, and the real one (Blockbook
/// through the exit) for every other case, as <see cref="TestDataBitcoinRouter"/> does for Bitcoin.
/// </summary>
/// <param name="real">The real source.</param>
/// <param name="testData">The made-up source, made the first time a test case asks.</param>
public sealed class TestDataZcashRouter(IZcashSource real, Lazy<IZcashSource> testData)
    : TestDataBitcoinRouter(real, new Lazy<IBitcoinSource>(() => testData.Value)), IZcashSource;

/// <summary>
/// The BSC source Chainsaw uses in a Debug build: the made-up source for a test case, and the real one (the user's
/// Blockscout endpoint through the exit) for every other case.
/// </summary>
/// <param name="real">The real source.</param>
/// <param name="testData">The made-up source, made the first time a test case asks.</param>
public sealed class TestDataBscRouter(IBscSource real, Lazy<IBscSource> testData)
    : TestDataAccountRouter(real, new Lazy<IAccountSource>(() => testData.Value)), IBscSource;

/// <summary>
/// The Solana source Chainsaw uses in a Debug build: the made-up source for a test case, and the real one (Solana's
/// JSON-RPC through the exit) for every other case.
/// </summary>
/// <param name="real">The real source.</param>
/// <param name="testData">The made-up source, made the first time a test case asks.</param>
public sealed class TestDataSolanaRouter(ISolanaSource real, Lazy<ISolanaSource> testData)
    : TestDataAccountRouter(real, new Lazy<IAccountSource>(() => testData.Value)), ISolanaSource;

/// <summary>
/// An account chain's source in a Debug build: the made-up source for a test case and the real one for every other
/// case, every lookup forwarded whole, so that neither is ever asked for the other's cases.
/// </summary>
/// <param name="real">The real source.</param>
/// <param name="testData">The made-up source, made the first time a test case asks.</param>
public abstract class TestDataAccountRouter(IAccountSource real, Lazy<IAccountSource> testData) : IAccountSource
{
    /// <inheritdoc/>
    public Chain Chain => real.Chain;

    /// <inheritdoc/>
    public long PageStartOf(EthereumBlockReference block) => real.PageStartOf(block);

    /// <inheritdoc/>
    public Task<BitcoinLookup<EthereumAddressPage>> GetAddressHistoryAsync(
        ICase @case, EthereumAddress address, EthereumHistory history, long? atOrBelow = null,
        LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default) =>
        For(@case).GetAddressHistoryAsync(@case, address, history, atOrBelow, mode, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<EthereumAddressPage>> GetRecentHistoryAsync(
        ICase @case, EthereumAddress address, EthereumHistory history, long? atOrBelow, LookupMode mode,
        DateTimeOffset notBefore, CancellationToken cancellationToken = default) =>
        For(@case).GetRecentHistoryAsync(@case, address, history, atOrBelow, mode, notBefore, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<EthereumTransaction>> GetTransactionAsync(
        ICase @case, EthereumTxHash hash, LookupMode mode = LookupMode.AsNeeded,
        CancellationToken cancellationToken = default) =>
        For(@case).GetTransactionAsync(@case, hash, mode, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<EthereumBlockReference>> GetBlockAsync(
        ICase @case, long height, LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default) =>
        For(@case).GetBlockAsync(@case, height, mode, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<EthereumTip>> GetTipHeightAsync(
        ICase @case, CancellationToken cancellationToken = default) =>
        For(@case).GetTipHeightAsync(@case, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<EthereumTip>> GetTipHeightAsync(
        ICase @case, LookupMode mode, CancellationToken cancellationToken = default) =>
        For(@case).GetTipHeightAsync(@case, mode, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<ChainTip>> GetTipAsync(ICase @case, CancellationToken cancellationToken = default) =>
        For(@case).GetTipAsync(@case, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<EthereumBalance>> GetBalanceAsync(
        ICase @case, EthereumAddress address, CancellationToken cancellationToken = default) =>
        For(@case).GetBalanceAsync(@case, address, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<BlockChange>> GetBlockChangesAsync(
        ICase @case, CancellationToken cancellationToken = default) =>
        For(@case).GetBlockChangesAsync(@case, cancellationToken);

    /// <summary>Chooses the source for a case.</summary>
    /// <param name="case">The case.</param>
    /// <returns>The made-up source for a test case, the real one otherwise.</returns>
    private IAccountSource For(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return TestCases.IsTestCase(@case) ? testData.Value : real;
    }
}
#endif
