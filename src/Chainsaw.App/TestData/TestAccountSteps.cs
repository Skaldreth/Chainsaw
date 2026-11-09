#if DEBUG
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.TestData;

/// <summary>
/// The Ethereum source Chainsaw uses in a Debug build: the made-up source for a test case, and the real one
/// (Blockscout through the exit) for every other case, as <see cref="TestDataBitcoinRouter"/> does for Bitcoin.
/// </summary>
/// <param name="real">The real source.</param>
/// <param name="testData">The made-up source, made the first time a test case asks.</param>
public sealed class TestDataEthereumRouter(IEthereumSource real, Lazy<IEthereumSource> testData) : IEthereumSource
{
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
    private IEthereumSource For(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return TestCases.IsTestCase(@case) ? testData.Value : real;
    }
}

/// <summary>
/// Labels the graph's known contracts from Chainsaw's own list, as showing the graph does (sketch ET1a): Tornado Cash's
/// pool as a mixer and a former sanction, so that the test case has <em>Tidigare sanktionerad</em>.
/// </summary>
public sealed record KnownContractsStep : ITestStep
{
    /// <inheritdoc/>
    public async Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Services.KnownContracts is not { } known)
        {
            run.Note("De kända kontrakten kunde inte märkas: listan finns inte.");
            return;
        }

        var graph = await run.Services.Tracing.LoadAsync(run.Case, cancellationToken).ConfigureAwait(false);
        var addresses = graph.Nodes.SelectMany(node => node.Addresses).Distinct().ToList();
        if (!known.Check(run.Case, addresses).Any(label => label.Kind == LabelKind.FormerlySanctioned))
        {
            run.Note("Inget känt kontrakt i grafen märktes som tidigare sanktionerat.");
        }
    }
}

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
    public Task<BitcoinLookup<EthereumTransaction>> GetDoubtedTransactionAsync(
        ICase @case, EthereumTxHash hash, LookupMode mode = LookupMode.AsNeeded,
        CancellationToken cancellationToken = default) =>
        For(@case).GetDoubtedTransactionAsync(@case, hash, mode, cancellationToken);

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

/// <summary>Makes an address on TRON or Ethereum a start point, as pasting it in Nytt ärende does.</summary>
/// <param name="Address">The address.</param>
public sealed record AccountStartPointStep(EthereumAddress Address) : ITestStep
{
    /// <inheritdoc/>
    public Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.Services.Tracing.AddStartPoint(run.Case, TraceStartPoint.ForEthereumAddress(Address));
        return Task.CompletedTask;
    }
}

/// <summary>Expands an address on TRON or Ethereum: brings in its coin and tokens through the made-up source.</summary>
/// <param name="Address">The address.</param>
public sealed record AccountExpandStep(EthereumAddress Address) : ITestStep
{
    /// <inheritdoc/>
    public async Task RunAsync(TestRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var result = await run.Services.Tracing.ExpandAsync(
            run.Case, ExpansionRequest.ForEthereumAddress(Address), cancellationToken).ConfigureAwait(false);
        if (result.Outcome != ExpansionOutcome.Expanded)
        {
            run.Note($"Expansionen av {Address.Chain}-adressen {Address} blev inte hel: {result.Problem}");
        }
    }
}

/// <summary>Labels an address on TRON or Ethereum from the made-up open source.</summary>
/// <param name="Address">The address.</param>
/// <param name="Kind">The label's sort.</param>
/// <param name="Name">Its name.</param>
public sealed record AccountLabelStep(EthereumAddress Address, LabelKind Kind, string Name) : ITestStep
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
