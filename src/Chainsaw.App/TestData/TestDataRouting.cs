#if DEBUG
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.TestData;

/// <summary>
/// The Bitcoin source Chainsaw uses in a Debug build: the made-up source for a test case, and the real one for every
/// other case. Neither is ever asked for the other's cases, so the made-up chain never reaches a real case and the
/// test case never sends anything out.
/// </summary>
/// <param name="real">The real source (Esplora through the exit).</param>
/// <param name="testData">The made-up source, made the first time a test case asks.</param>
public sealed class TestDataBitcoinRouter(IBitcoinSource real, Lazy<IBitcoinSource> testData) : IBitcoinSource
{
    /// <inheritdoc/>
    public BitcoinNetwork Network => real.Network;

    /// <inheritdoc/>
    public Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
        ICase @case, BitcoinAddress address, Txid? after = null, CancellationToken cancellationToken = default) =>
        For(@case).GetAddressTransactionsAsync(@case, address, after, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
        ICase @case, BitcoinAddress address, Txid? after, LookupMode mode,
        CancellationToken cancellationToken = default) =>
        For(@case).GetAddressTransactionsAsync(@case, address, after, mode, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<BitcoinTransaction>> GetTransactionAsync(
        ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default) =>
        For(@case).GetTransactionAsync(@case, txid, mode, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<BitcoinTransaction>> VerifyTransactionAsync(
        ICase @case, Txid txid, CancellationToken cancellationToken = default) =>
        For(@case).VerifyTransactionAsync(@case, txid, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<BitcoinAddressSummary>> GetAddressSummaryAsync(
        ICase @case, BitcoinAddress address, CancellationToken cancellationToken = default) =>
        For(@case).GetAddressSummaryAsync(@case, address, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>> GetOutputSpendsAsync(
        ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default) =>
        For(@case).GetOutputSpendsAsync(@case, txid, mode, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<BitcoinBlock>> GetBlockAsync(
        ICase @case, BlockHash hash, CancellationToken cancellationToken = default) =>
        For(@case).GetBlockAsync(@case, hash, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<BitcoinTip>> GetTipHeightAsync(ICase @case, CancellationToken cancellationToken = default) =>
        For(@case).GetTipHeightAsync(@case, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<ChainTip>> GetTipAsync(ICase @case, CancellationToken cancellationToken = default) =>
        For(@case).GetTipAsync(@case, cancellationToken);

    /// <summary>Chooses the source for a case.</summary>
    /// <param name="case">The case.</param>
    /// <returns>The made-up source for a test case, the real one otherwise.</returns>
    private IBitcoinSource For(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return TestCases.IsTestCase(@case) ? testData.Value : real;
    }
}

/// <summary>
/// Opens the watches' gate while <c>/testdata</c> builds the test case, so that its watch gets its hit from the
/// made-up chain also when no exit is connected; nothing goes out, since the test case's source is the made-up one.
/// Every other time, and for every other check running at the same time, the gate is the real one.
/// </summary>
/// <param name="inner">The real gate.</param>
public sealed class TestDataWatchGate(IWatchGate inner) : IWatchGate
{
    /// <inheritdoc/>
    public WatchGate For(Chain chain) => TestDataScope.IsBuilding ? WatchGate.Open : inner.For(chain);
}

/// <summary>
/// Keeps the test case's watches out of the background's plan, so that the background process, which asks the real
/// sources, never asks for a made-up address.
/// </summary>
/// <param name="inner">The real background watches.</param>
public sealed class TestDataBackgroundWatches(IBackgroundWatches inner) : IBackgroundWatches
{
    /// <inheritdoc/>
    public BackgroundCase Describe(ICase @case, DateTimeOffset utc)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return TestCases.IsTestCase(@case) ? new BackgroundCase(@case.Info.Id, [], utc) : inner.Describe(@case, utc);
    }

    /// <inheritdoc/>
    public BackgroundImport Import(ICase @case) => inner.Import(@case);
}

/// <summary>Marks the work of building the test case, in the flow that does it and nowhere else.</summary>
public static class TestDataScope
{
    /// <summary>Whether the current flow builds the test case.</summary>
    private static readonly AsyncLocal<bool> Flag = new();

    /// <summary>Gets whether the current flow builds the test case.</summary>
    public static bool IsBuilding => Flag.Value;

    /// <summary>Marks the current flow as building the test case until the result is disposed.</summary>
    /// <returns>Ends the mark.</returns>
    public static IDisposable Building()
    {
        Flag.Value = true;
        return new End();
    }

    /// <summary>Ends the mark.</summary>
    private sealed class End : IDisposable
    {
        /// <inheritdoc/>
        public void Dispose() => Flag.Value = false;
    }
}
#endif
