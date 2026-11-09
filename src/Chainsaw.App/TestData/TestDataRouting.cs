#if DEBUG
using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.TestData;

/// <summary>
/// The Bitcoin source Chainsaw uses in a Debug build: the made-up source for a test case, and the real one for every
/// other case. Neither is ever asked for the other's cases, so the made-up chain never reaches a real case and the
/// test case never sends anything out.
/// </summary>
/// <param name="real">The real source (Esplora through the exit).</param>
/// <param name="testData">The made-up source, made the first time a test case asks.</param>
public class TestDataBitcoinRouter(IBitcoinSource real, Lazy<IBitcoinSource> testData) : IBitcoinSource
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
    public Task<BitcoinLookup<BitcoinTransaction>> VerifyInBlockAsync(
        ICase @case, Txid txid, CancellationToken cancellationToken = default) =>
        For(@case).VerifyInBlockAsync(@case, txid, cancellationToken);

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
    public Task<BitcoinLookup<BitcoinTip>> GetTipHeightAsync(
        ICase @case, LookupMode mode, CancellationToken cancellationToken = default) =>
        For(@case).GetTipHeightAsync(@case, mode, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<ChainTip>> GetTipAsync(ICase @case, CancellationToken cancellationToken = default) =>
        For(@case).GetTipAsync(@case, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<ChainTip>> GetTipAsync(
        ICase @case, LookupMode mode, CancellationToken cancellationToken = default) =>
        For(@case).GetTipAsync(@case, mode, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<BlockChange>> GetBlockChangesAsync(
        ICase @case, CancellationToken cancellationToken = default) =>
        For(@case).GetBlockChangesAsync(@case, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<TransactionReplacement>> GetReplacementAsync(
        ICase @case, Txid txid, LookupMode mode = LookupMode.CaseOnly, CancellationToken cancellationToken = default) =>
        For(@case).GetReplacementAsync(@case, txid, mode, cancellationToken);

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

/// <summary>
/// The price service Chainsaw uses in a Debug build: made-up rates for a test case, and the real ones (Bitstamp and the
/// Riksbank through the exit) for every other case, so that showing the test case's graph never sends a rate lookup.
/// </summary>
/// <param name="real">The real service.</param>
/// <param name="testData">The service over made-up rates, made the first time a test case asks.</param>
public sealed class TestDataPriceRouter(IPriceService real, Lazy<IPriceService> testData) : IPriceService
{
    /// <inheritdoc/>
    public Task<PriceLookup<Price>> GetPriceAsync(
        ICase @case, PriceRequest request, PriceLookupMode mode = PriceLookupMode.AsNeeded,
        CancellationToken cancellationToken = default) =>
        For(@case).GetPriceAsync(@case, request, mode, cancellationToken);

    /// <inheritdoc/>
    public RateSummary Summarize(ICase @case) => For(@case).Summarize(@case);

    /// <summary>Chooses the service for a case.</summary>
    /// <param name="case">The case.</param>
    /// <returns>The made-up rates for a test case, the real ones otherwise.</returns>
    private IPriceService For(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return TestCases.IsTestCase(@case) ? testData.Value : real;
    }
}

/// <summary>
/// Gives the sanction lists a made-up hit while <c>/testdata</c> builds the test case: the story's sanctioned actor is
/// on a made-up list, under the list's own made-up name, so that the test case has a sanction hit (sketch ET1a) without
/// a real list being imported or replaced, and the OTC desk is an uncertain entry on a made-up list in the EU's form, a
/// possible sanction. Every other time, and for every other address, the lists are the real ones.
/// </summary>
/// <param name="inner">The real lists.</param>
/// <param name="actors">The story's actors.</param>
public sealed class TestDataSanctionLists(ISanctionLists inner, TestActors actors) : ISanctionLists
{
    /// <summary>The made-up list's name, as the sanction hit names it.</summary>
    public const string ListName = "Påhittad sanktionslista (testdata)";

    /// <summary>The made-up entry's name.</summary>
    public const string EntryName = "Sanktionerad aktör (påhittad)";

    /// <summary>The made-up entry of the story's dark market, a sanction beside its own sort.</summary>
    public const string DarknetEntryName = "Skuggtorget";

    /// <summary>The made-up list in the EU's form, whose one entry is uncertain.</summary>
    public const string UncertainListName = "Påhittad EU-lista (testdata)";

    /// <summary>The made-up uncertain entry's name: the OTC desk, a possible sanction.</summary>
    public const string UncertainEntryName = "Torgväxlaren (påhittad)";

    /// <summary>The made-up entry's id on the list.</summary>
    private const string EntryUid = "testdata-1";

    /// <summary>The made-up sanctions programme.</summary>
    private const string Programme = "TESTDATA";

    /// <summary>Where on the made-up uncertain entry the address stood, as the EU's list says it.</summary>
    private const string UncertainListedAs = "Anmärkning (påhittad)";

    /// <summary>The made-up uncertain entry's id on the list.</summary>
    private const string UncertainUid = "testdata-eu-1";

    /// <summary>The made-up list, the same in every run.</summary>
    private static readonly SanctionListInfo MadeUp = new(
        ListName,
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ListName))),
        new DateOnly(2026, 10, 1),
        new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
        new Dictionary<string, int>(StringComparer.Ordinal) { [SanctionCurrency.Bitcoin] = 2 },
        0);

    /// <summary>The made-up list in the EU's form, the same in every run.</summary>
    private static readonly SanctionListInfo MadeUpUncertain = new(
        UncertainListName,
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(UncertainListName))),
        new DateOnly(2026, 10, 1),
        new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
        new Dictionary<string, int>(StringComparer.Ordinal) { [SanctionCurrency.Bitcoin] = 1 },
        0)
    {
        Uncertain = 1,
    };

    /// <inheritdoc/>
    public SanctionListInfo? Current => inner.Current;

    /// <inheritdoc/>
    public SanctionListInfo Import(Stream content) => inner.Import(content);

    /// <inheritdoc/>
    public IReadOnlyList<SanctionListInfo> List() => inner.List();

    /// <inheritdoc/>
    public SanctionMatch? Find(BitcoinAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return MadeUpFor(address) ?? inner.Find(address);
    }

    /// <inheritdoc/>
    public SanctionMatch? Find(EthereumAddress address) => inner.Find(address);

    /// <inheritdoc/>
    public IReadOnlyList<SanctionListInfo> CurrentLists => inner.CurrentLists;

    /// <inheritdoc/>
    public IReadOnlyList<SanctionMatch> FindAll(BitcoinAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return MadeUpFor(address) is { } madeUp ? [madeUp] : inner.FindAll(address);
    }

    /// <inheritdoc/>
    public IReadOnlyList<SanctionMatch> FindAll(EthereumAddress address) => inner.FindAll(address);

    /// <inheritdoc/>
    public SanctionLookup FindMany(IReadOnlyCollection<ChainAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var lookup = inner.FindMany(addresses);
        var matches = new Dictionary<ChainAddress, IReadOnlyList<SanctionMatch>>(lookup.Matches);
        foreach (var address in addresses)
        {
            if (BitcoinAddress.TryParse(address.Value, out var bitcoin) && MadeUpFor(bitcoin) is { } madeUp)
            {
                matches[address] = [madeUp];
            }
        }

        return lookup with { Matches = matches };
    }

    /// <summary>
    /// Gives the made-up list's entry for an address while the test case is built: the story's sanctioned actor and
    /// the dark market (so that a sanction also stands as a mark beside another sort), never any other address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>The made-up match, or <see langword="null"/>.</returns>
    private SanctionMatch? MadeUpFor(BitcoinAddress address)
    {
        if (!TestDataScope.IsBuilding)
        {
            return null;
        }

        if (address == actors.Otc.Address)
        {
            // As on the EU's list, where an address in a remark does not say its chain for certain: a possible sanction.
            var uncertain = new SanctionedAddress(
                SanctionCurrency.Bitcoin, address.Value, UncertainUid, UncertainEntryName, [Programme])
            {
                ListedAs = UncertainListedAs,
                IsUncertain = true,
            };
            return new SanctionMatch(MadeUpUncertain, [uncertain]);
        }

        var name = (address == actors.Sanctioned.Address, address == actors.Darknet.Address) switch
        {
            (true, _) => EntryName,
            (_, true) => DarknetEntryName,
            _ => null,
        };
        return name is null
            ? null
            : new SanctionMatch(
                MadeUp, [new SanctionedAddress(SanctionCurrency.Bitcoin, address.Value, EntryUid, name, [Programme])]);
    }

    /// <inheritdoc/>
    public bool Remove(string sha256) => inner.Remove(sha256);
}

/// <summary>
/// Gives the open sources a made-up hit while <c>/testdata</c> builds the test case: the scammer's second address is a
/// ransomware payment address on a made-up source in Ransomwhere's form, so that the test case has a <em>Rapporterad</em>
/// label from an open source without a real file being imported. Every other time, and for every other address, the
/// sources are the real ones.
/// </summary>
/// <param name="inner">The real sources.</param>
/// <param name="actors">The story's actors.</param>
public sealed class TestDataOpenSources(IOpenSourceLists inner, TestActors actors) : IOpenSourceLists
{
    /// <summary>The made-up source's name, as the label names it.</summary>
    public const string SourceName = "Påhittad öppen källa (testdata)";

    /// <summary>The made-up entry's name.</summary>
    public const string EntryName = "Ransomware: Påhittad";

    /// <summary>The made-up source, the same in every run.</summary>
    private static readonly OpenSourceListInfo MadeUp = new(
        SourceName,
        "Påhittad i testärendet, ingen licens",
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(SourceName))),
        new DateOnly(2026, 10, 1),
        new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
        new Dictionary<string, int>(StringComparer.Ordinal) { [nameof(Chain.Bitcoin)] = 1 },
        0);

    /// <inheritdoc/>
    public IReadOnlyList<OpenSourceListInfo> CurrentLists => inner.CurrentLists;

    /// <inheritdoc/>
    public OpenSourceListInfo Import(Stream content) => inner.Import(content);

    /// <inheritdoc/>
    public IReadOnlyList<OpenSourceListInfo> List() => inner.List();

    /// <inheritdoc/>
    public OpenSourceLookup Find(IReadOnlyCollection<ChainAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var lookup = inner.Find(addresses);
        var reported = actors.Scammer2.Address?.ToChainAddress();
        if (!TestDataScope.IsBuilding || reported is null || !addresses.Contains(reported))
        {
            return lookup;
        }

        var entry = new OpenSourceEntry(
            reported, LabelKind.Reported, EntryName,
            "Påhittad post i testärendet: en adress som offer för utpressningsprogram betalade till.");
        var matches = new Dictionary<ChainAddress, IReadOnlyList<OpenSourceMatch>>(lookup.Matches)
        {
            [reported] = [new OpenSourceMatch(MadeUp, [entry])],
        };
        return lookup with { Lists = [MadeUp, .. lookup.Lists], Matches = matches };
    }

    /// <inheritdoc/>
    public bool Remove(string sha256) => inner.Remove(sha256);
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
