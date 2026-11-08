#if DEBUG
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.TestData;

/// <summary>
/// The TRON source Chainsaw uses in a Debug build: the made-up source for a test case, and the real one (TronGrid
/// through the exit) for every other case, as <see cref="TestDataBitcoinRouter"/> does for Bitcoin.
/// </summary>
/// <param name="real">The real source.</param>
/// <param name="testData">The made-up source, made the first time a test case asks.</param>
public sealed class TestDataTronRouter(ITronSource real, Lazy<ITronSource> testData) : ITronSource
{
    /// <inheritdoc/>
    public long PageStartOf(EthereumBlockReference block) => real.PageStartOf(block);

    /// <inheritdoc/>
    public Task<BitcoinLookup<EthereumAddressPage>> GetAddressHistoryAsync(
        ICase @case, EthereumAddress address, EthereumHistory history, long? atOrBelow = null,
        LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default) =>
        For(@case).GetAddressHistoryAsync(@case, address, history, atOrBelow, mode, cancellationToken);

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
    private ITronSource For(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return TestCases.IsTestCase(@case) ? testData.Value : real;
    }
}

/// <summary>Makes the scammer's address on TRON a start point, as pasting it in Nytt ärende does.</summary>
/// <param name="Address">The address.</param>
public sealed record TronStartPointStep(EthereumAddress Address) : ITestStep
{
    /// <inheritdoc/>
    public Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.Services.Tracing.AddStartPoint(run.Case, TraceStartPoint.ForEthereumAddress(Address));
        return Task.CompletedTask;
    }
}

/// <summary>Expands an address on TRON: brings in its TRX and USDT through the made-up TRON source.</summary>
/// <param name="Address">The address.</param>
public sealed record TronExpandStep(EthereumAddress Address) : ITestStep
{
    /// <inheritdoc/>
    public async Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var result = await run.Services.Tracing.ExpandAsync(
            run.Case, ExpansionRequest.ForEthereumAddress(Address), cancellationToken).ConfigureAwait(false);
        if (result.Outcome != ExpansionOutcome.Expanded)
        {
            run.Note($"Expansionen av TRON-adressen {Address} blev inte hel: {result.Problem}");
        }
    }
}

/// <summary>Labels an address on TRON from the made-up open source.</summary>
/// <param name="Address">The address.</param>
/// <param name="Kind">The label's sort.</param>
/// <param name="Name">Its name.</param>
public sealed record TronLabelStep(EthereumAddress Address, LabelKind Kind, string Name) : ITestStep
{
    /// <inheritdoc/>
    public Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var subject = LabelSubject.ForAccount(Address.ToChainAddress());
        run.Services.Attribution.Add(run.Case, new LabelDraft(subject, Kind, Name, TestStory.LabelReason)
        {
            Source = LabelSource.OpenSource,
            SourceName = TestStory.LabelSource,
            SourceVersion = TestStory.Version,
        });
        return Task.CompletedTask;
    }
}
#endif
