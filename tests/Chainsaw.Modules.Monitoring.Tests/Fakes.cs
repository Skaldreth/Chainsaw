using System.Security.Cryptography;
using System.Text;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring.Tests;

/// <summary>A vault the test locks and unlocks.</summary>
internal sealed class FakeVault : IVault
{
    public VaultState State { get; set; } = VaultState.Unlocked;

    public event EventHandler<VaultState>? StateChanged
    {
        add { }
        remove { }
    }

    public void Create(ReadOnlySpan<char> masterKey) => throw new NotSupportedException();

    public void Unlock(ReadOnlySpan<char> masterKey) => throw new NotSupportedException();

    public void Lock() => throw new NotSupportedException();

    public void ChangeMasterKey(ReadOnlySpan<char> currentMasterKey, ReadOnlySpan<char> newMasterKey) =>
        throw new NotSupportedException();
}

/// <summary>Purpose keys from a fixed secret.</summary>
internal sealed class FakePurposeKeys : IPurposeKeys
{
    public void DeriveKey(string purpose, Span<byte> destination)
    {
        var secret = SHA256.HashData(Encoding.UTF8.GetBytes("monitoring-tests"));
        HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, destination, [], Encoding.ASCII.GetBytes(purpose));
    }
}

/// <summary>An operator profile in memory.</summary>
internal sealed class FakeProfiles : IOperatorProfileStore
{
    public OperatorProfile? Profile { get; set; } = new("Testa Testsson", "Utredare", "Enheten");

    public OperatorProfile? Get() => Profile;

    public void Save(OperatorProfile profile) => Profile = profile;
}

/// <summary>A clock the test moves; waits pass at once.</summary>
internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 7, 1, 2, 3, TimeSpan.Zero);

    public Task Delay(TimeSpan wait, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UtcNow += wait;
        return Task.CompletedTask;
    }
}

/// <summary>A gate the test opens and closes.</summary>
internal sealed class FakeGate : IWatchGate
{
    public WatchGate State { get; set; } = WatchGate.Open;

    public WatchGate For(Chain chain) => State;
}

/// <summary>Bitcoin's source from a table of synthetic pages; records every lookup and its mode.</summary>
internal sealed class FakeBitcoinSource : IBitcoinSource
{
    /// <summary>The pages of each address, first page first.</summary>
    public Dictionary<string, List<BitcoinAddressPage>> Pages { get; } = [];

    public long? Tip { get; set; } = 900_000;

    public LookupStatus Status { get; set; } = LookupStatus.Fetched;

    public List<(string Address, LookupMode Mode)> Asked { get; } = [];

    public int TipAsked { get; private set; }

    public BitcoinNetwork Network => BitcoinNetwork.Mainnet;

    /// <summary>Puts the transactions on an address as one page, newest first.</summary>
    public void Holds(BitcoinAddress address, params BitcoinTransaction[] transactions) =>
        Pages[address.Value] = [new BitcoinAddressPage(address, null, transactions, null)];

    public Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
        ICase @case, BitcoinAddress address, Txid? after = null, CancellationToken cancellationToken = default) =>
        GetAddressTransactionsAsync(@case, address, after, LookupMode.AsNeeded, cancellationToken);

    public Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
        ICase @case, BitcoinAddress address, Txid? after, LookupMode mode,
        CancellationToken cancellationToken = default)
    {
        Asked.Add((address.Value, mode));
        if (Status != LookupStatus.Fetched)
        {
            return Task.FromResult(new BitcoinLookup<BitcoinAddressPage>(Status, null, [], "Inget svar."));
        }

        var pages = Pages.GetValueOrDefault(address.Value) ?? [new BitcoinAddressPage(address, null, [], null)];
        var index = after is null ? 0 : pages.FindIndex(page => page.After == after);
        var page = index < 0 ? new BitcoinAddressPage(address, after, [], null) : pages[index];
        return Task.FromResult(new BitcoinLookup<BitcoinAddressPage>(LookupStatus.Fetched, page, [Origin(7)], null));
    }

    public Task<BitcoinLookup<BitcoinTip>> GetTipHeightAsync(ICase @case, CancellationToken cancellationToken = default)
    {
        TipAsked++;
        return Task.FromResult(Tip is { } tip
            ? new BitcoinLookup<BitcoinTip>(LookupStatus.Fetched, new BitcoinTip(tip), [Origin(3)], null)
            : new BitcoinLookup<BitcoinTip>(LookupStatus.Offline, null, [], "Offline."));
    }

    public Task<BitcoinLookup<BitcoinTransaction>> GetTransactionAsync(
        ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<BitcoinLookup<BitcoinTransaction>> VerifyTransactionAsync(
        ICase @case, Txid txid, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<BitcoinLookup<BitcoinAddressSummary>> GetAddressSummaryAsync(
        ICase @case, BitcoinAddress address, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>> GetOutputSpendsAsync(
        ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<BitcoinLookup<BitcoinBlock>> GetBlockAsync(
        ICase @case, BlockHash hash, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public static ResponseOrigin Origin(long sequence) => new(
        sequence, "esplora", new Uri("https://esplora.test/api/"), 200, DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch, new OperatorProfile("T", "T", "T"), new string('0', 64));
}

/// <summary>Ethereum's source from a table of synthetic movements; records every history asked for.</summary>
internal sealed class FakeEthereumSource : IEthereumSource
{
    public Dictionary<(string, EthereumHistory), List<EthereumTransfer>> Histories { get; } = [];

    public Dictionary<long, string> BlockHashes { get; } = [];

    public List<(string Address, EthereumHistory History, LookupMode Mode)> Asked { get; } = [];

    public List<(long Height, LookupMode Mode)> BlocksAsked { get; } = [];

    public void Holds(EthereumAddress address, EthereumHistory history, params EthereumTransfer[] transfers) =>
        Histories[(address.Value, history)] = [.. transfers];

    public Task<BitcoinLookup<EthereumAddressPage>> GetAddressHistoryAsync(
        ICase @case, EthereumAddress address, EthereumHistory history, long? atOrBelow = null,
        LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
    {
        Asked.Add((address.Value, history, mode));
        var transfers = Histories.GetValueOrDefault((address.Value, history)) ?? [];
        var page = new EthereumAddressPage(address, history, atOrBelow, transfers, [], null, false, false);
        return Task.FromResult(new BitcoinLookup<EthereumAddressPage>(
            LookupStatus.Fetched, page, [FakeBitcoinSource.Origin(11)], null));
    }

    public Task<BitcoinLookup<EthereumBlockReference>> GetBlockAsync(
        ICase @case, long height, LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
    {
        BlocksAsked.Add((height, mode));
        return Task.FromResult(BlockHashes.TryGetValue(height, out var hash)
            ? new BitcoinLookup<EthereumBlockReference>(
                LookupStatus.Fetched, new EthereumBlockReference(height, hash, DateTimeOffset.UnixEpoch),
                [FakeBitcoinSource.Origin(12)], null)
            : new BitcoinLookup<EthereumBlockReference>(LookupStatus.Failed, null, [], "Inget block."));
    }

    public Task<BitcoinLookup<EthereumTransaction>> GetTransactionAsync(
        ICase @case, EthereumTxHash hash, LookupMode mode = LookupMode.AsNeeded,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<BitcoinLookup<EthereumTip>> GetTipHeightAsync(
        ICase @case, CancellationToken cancellationToken = default) =>
        Task.FromResult(new BitcoinLookup<EthereumTip>(LookupStatus.Fetched, new EthereumTip(21_000_100), [], null));
}

/// <summary>A feed from a table of transactions per address, for the check without the case.</summary>
internal sealed class FakeFeed : IWatchFeed
{
    public Dictionary<ChainAddress, WatchFeedPage> Pages { get; } = [];

    public List<(ChainAddress Address, Asset? Asset, long DownTo)> Asked { get; } = [];

    public Task<WatchFeedPage> ReadAsync(
        ChainAddress address, Asset? asset, long downTo, CancellationToken cancellationToken = default)
    {
        Asked.Add((address, asset, downTo));
        return Task.FromResult(Pages.GetValueOrDefault(address)
            ?? new WatchFeedPage(WatchFeedStatus.Read, [], [], false, null));
    }
}

/// <summary>The exit, without any network: answers from a table, and refuses when not connected.</summary>
internal sealed class FakeExitHttp(FakeClock clock) : IExitHttp
{
    private readonly Dictionary<string, (int Status, byte[] Body, string ContentType)> _answers = [];

    public List<ExitRequest> Sent { get; } = [];

    public void Answer(string url, byte[] body, string contentType = "application/json") =>
        _answers[url] = (200, body, contentType);

    public Task<ExitResponse> SendAsync(ExitRequest request, CancellationToken cancellationToken = default)
    {
        Sent.Add(request);
        var (status, body, contentType) = _answers.TryGetValue(request.Uri.AbsoluteUri, out var answer)
            ? answer
            : (404, "Not found"u8.ToArray(), "text/plain");
        var sent = clock.UtcNow;
        return Task.FromResult(new ExitResponse(
            request.Uri, Guid.Parse("0b9e1e4a-5c39-4a8e-9a55-3f1f6a1c0002"), status, contentType, body, sent,
            sent.AddMilliseconds(50), ExitRoute.Vpn));
    }
}
