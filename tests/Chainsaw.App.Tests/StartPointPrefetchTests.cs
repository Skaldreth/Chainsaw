using System.Data.Common;
using Chainsaw.App.Links;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

/// <summary>
/// Nytt ärende's lookups in advance (sketch NC2a): only facts the source gave, nothing sent without the exit, and
/// everything fetched sealed in the case once it exists, or dropped with the panel.
/// </summary>
public sealed class StartPointPrefetchTests
{
    private static readonly BitcoinAddress Address = StartPointText.Parse(Samples.Address).Bitcoin!.Address!;

    private readonly MemoryCases _memory = new();
    private readonly FakeTransfer _transfer = new();
    private readonly FakeBitcoin _bitcoin = new();
    private readonly FakeSanctions _sanctions = new();

    [Fact]
    public async Task An_address_shows_its_balance_count_activity_and_height_from_the_source()
    {
        var prefetch = Prefetch();

        var preview = await prefetch.PreviewAsync(StartPointText.Parse(Samples.Address), mayFetch: true);

        Assert.Equal(PreviewState.Fetched, preview.State);
        Assert.Equal(["0,0412", "2", "mars 2024 → 29 okt 2025"], preview.Facts.Select(fact => fact.Value));
        Assert.Equal([" BTC", " tx", string.Empty], preview.Facts.Select(fact => fact.Unit));
        Assert.Equal(921_344, preview.Height);
        Assert.Contains("Hämtat vid block 921\u00A0344", preview.HeightTip, StringComparison.Ordinal);
        Assert.Contains(FakeBitcoin.Hash.Value, preview.HeightTip, StringComparison.Ordinal);
        Assert.False(preview.IsSanctioned);
    }

    [Fact]
    public async Task A_transaction_shows_the_chains_height_and_names_its_own_block_apart()
    {
        var prefetch = Prefetch();

        var preview = await prefetch.PreviewAsync(StartPointText.Parse(Samples.Txid), mayFetch: true);

        Assert.Equal(PreviewState.Fetched, preview.State);
        Assert.Equal(921_344, preview.Height);
        Assert.Contains("Hämtat vid block 921\u00A0344", preview.HeightTip, StringComparison.Ordinal);
        Assert.Contains("I block 830\u00A0000", preview.HeightTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_the_chains_height_no_other_block_is_shown_as_it()
    {
        _bitcoin.TipFails = true;
        var prefetch = Prefetch();

        var address = await prefetch.PreviewAsync(StartPointText.Parse(Samples.Address), mayFetch: true);
        var transaction = await prefetch.PreviewAsync(StartPointText.Parse(Samples.Txid), mayFetch: true);

        Assert.Null(address.Height);
        Assert.Null(transaction.Height);
        Assert.DoesNotContain("Hämtat vid", address.HeightTip, StringComparison.Ordinal);
        Assert.Contains("Senaste tx i block 921\u00A0300", address.HeightTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sanction_list_that_cannot_be_read_fails_the_row_instead_of_hanging()
    {
        _sanctions.Locked = true;
        var prefetch = Prefetch();

        var preview = await prefetch.PreviewAsync(StartPointText.Parse(Samples.Address), mayFetch: true);

        Assert.Equal(PreviewState.Failed, preview.State);
        Assert.Contains("Sanktionslistan", preview.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_response_that_cannot_be_read_keeps_none_of_the_others_out()
    {
        var prefetch = Prefetch();
        await prefetch.PreviewAsync(StartPointText.Parse(Samples.Address), mayFetch: true);
        _transfer.Broken = 2;
        var target = new MemoryCase();

        var count = prefetch.SealInto(target);

        Assert.Equal(2, count);
        Assert.Contains("1 kunde inte läsas", Assert.Single(target.Journal).Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_the_last_activity_is_shown_when_older_transactions_were_not_fetched()
    {
        _bitcoin.ConfirmedCount = 40;
        var prefetch = Prefetch();

        var preview = await prefetch.PreviewAsync(StartPointText.Parse(Samples.Address), mayFetch: true);

        Assert.Equal("senast 29 okt 2025", preview.Facts[^1].Value);
    }

    [Fact]
    public async Task Without_the_exit_nothing_is_asked_but_the_sanction_list_is()
    {
        _sanctions.Named = true;
        var prefetch = Prefetch();

        var preview = await prefetch.PreviewAsync(StartPointText.Parse(Samples.Address), mayFetch: false);

        Assert.Equal(PreviewState.WaitingForExit, preview.State);
        Assert.Equal(0, _bitcoin.Asked);
        Assert.Null(_memory.Made);
        Assert.True(preview.IsSanctioned);
        Assert.Contains("OFAC SDN 2026-11-01 · Blandaren", preview.SanctionTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_offline_answer_waits_and_an_unknown_address_says_so()
    {
        var prefetch = Prefetch();

        _bitcoin.Status = LookupStatus.Offline;
        var offline = await prefetch.PreviewAsync(StartPointText.Parse(Samples.Address), mayFetch: true);
        _bitcoin.Status = LookupStatus.NotFound;
        var unknown = await prefetch.PreviewAsync(StartPointText.Parse(Samples.Address), mayFetch: true);

        Assert.Equal(PreviewState.WaitingForExit, offline.State);
        Assert.Equal(PreviewState.Failed, unknown.State);
        Assert.Equal("Källan känner inte till den", unknown.Problem);
    }

    [Fact]
    public async Task TRON_and_the_unknown_are_never_asked()
    {
        var prefetch = Prefetch();

        var tron = await prefetch.PreviewAsync(StartPointText.Parse(Samples.Tron), mayFetch: true);
        var unknown = await prefetch.PreviewAsync(StartPointText.Parse("hej"), mayFetch: true);

        Assert.Equal(PreviewState.NotBuilt, tron.State);
        Assert.Equal(PreviewState.NotRecognised, unknown.State);
        Assert.Equal(0, _bitcoin.Asked);
    }

    [Fact]
    public async Task What_was_fetched_is_sealed_in_the_new_case_with_a_journal_line()
    {
        var prefetch = Prefetch();
        await prefetch.PreviewAsync(StartPointText.Parse(Samples.Address), mayFetch: true);
        var target = new MemoryCase();

        var sealedCount = prefetch.SealInto(target);

        Assert.Equal(3, sealedCount);
        Assert.Equal([1L, 2L, 3L], _transfer.Sealed.Select(response => response.Sequence));
        Assert.Same(target.Transaction, _transfer.SealedIn);
        Assert.Equal(AppJournalKinds.PrefetchSealed, Assert.Single(target.Journal).Kind);
    }

    [Fact]
    public async Task Forget_drops_the_case_in_memory_and_nothing_is_sealed_after()
    {
        var prefetch = Prefetch();
        await prefetch.PreviewAsync(StartPointText.Parse(Samples.Address), mayFetch: true);

        prefetch.Forget();

        Assert.True(_memory.Made!.IsClosed);
        Assert.Equal(0, prefetch.SealInto(new MemoryCase()));
        Assert.Empty(_transfer.Sealed);
    }

    [Fact]
    public async Task The_panel_shows_what_the_prefetch_found_and_fetches_waiting_rows_when_the_exit_comes()
    {
        var prefetch = Prefetch();
        var rig = new GateRig(VaultState.Unlocked);
        var panel = new NewCaseViewModel(rig.Workflow, prefetch);
        panel.Show();
        panel.FollowExit(isLive: false, isWindows: false, "Stockholm");

        panel.Paste(Samples.Address);
        await WaitUntil(() => panel.Rows[0].IsWaiting);
        panel.FollowExit(isLive: true, isWindows: false, "Stockholm");
        await WaitUntil(() => panel.Rows[0].IsFetched);

        Assert.True(panel.Rows[0].IsFetched);
        Assert.Equal("921\u00A0344", panel.Rows[0].HeightText);
        Assert.Equal("via Stockholm", panel.ExitChipText);
    }

    [Fact]
    public async Task Creating_the_case_seals_what_was_fetched_and_then_drops_it()
    {
        var prefetch = Prefetch();
        var rig = new GateRig(VaultState.Unlocked);
        var panel = new NewCaseViewModel(rig.Workflow, prefetch);
        var replies = new List<string>();
        panel.Created += (_, reply) => replies.Add(reply.Text);
        panel.Show();
        panel.FollowExit(isLive: true, isWindows: true, "Windows");
        panel.Paste(Samples.Address);
        await WaitUntil(() => panel.Rows[0].IsFetched);
        panel.CaseNumber = "A1";
        var shown = _memory.Made!;

        await panel.CreateAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));

        // The test case refuses writes, so the workflow says the step failed; it was asked all the same, and the
        // case is kept.
        Assert.False(panel.IsOpen);
        Assert.True(shown.IsClosed);
        Assert.Contains("förseglades inte", Assert.Single(replies), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancel_drops_what_was_fetched()
    {
        var prefetch = Prefetch();
        var panel = new NewCaseViewModel(new GateRig(VaultState.Unlocked).Workflow, prefetch);
        panel.Show();
        panel.FollowExit(isLive: true, isWindows: true, "Windows");
        panel.Paste(Samples.Address);
        await WaitUntil(() => panel.Rows[0].IsFetched);

        panel.Cancel();

        Assert.True(_memory.Made!.IsClosed);
        Assert.Empty(_transfer.Sealed);
    }

    private StartPointPrefetch Prefetch() =>
        new(_memory, _transfer, _bitcoin, new FakeEthereum(), _sanctions, new FixedClock());

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var tries = 0; tries < 300 && !condition(); tries++)
        {
            await Task.Delay(10);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class MemoryCases : IMemoryCases
    {
        public MemoryCase? Made { get; private set; }

        public IMemoryCase Create(string sealedChain)
        {
            Assert.Equal(SealedResponses.Chain, sealedChain);
            return Made = new MemoryCase();
        }
    }

    /// <summary>A case in memory and a target case at once: it counts what a source seals and keeps a journal.</summary>
    private sealed class MemoryCase : IMemoryCase, ICaseTransaction
    {
        private readonly List<long> _sealed = [];

        public ICaseTransaction Transaction => this;

        public List<JournalDraft> Journal { get; } = [];

        public CaseInfo Info { get; } = new(Guid.NewGuid(), "minne", DateTimeOffset.UnixEpoch);

        public string Title => string.Empty;

        public string Path => string.Empty;

        public bool IsClosed { get; private set; }

        public bool HasSharingPassword => false;

        public IReadOnlyList<string> RecoveryFingerprints => [];

        public void Seal() => _sealed.Add(_sealed.Count + 1);

        public int Mark() => _sealed.Count;

        public IReadOnlyList<long> SealedSince(string chain, int mark) => [.. _sealed.Skip(mark)];

        public T Read<T>(Func<ICaseReader, T> work) => IsClosed ? throw new CaseException(CaseError.Closed) : work(this);

        public T Write<T>(Func<ICaseTransaction, T> work) => work(this);

        public JournalEntry AppendJournal(JournalDraft draft)
        {
            Journal.Add(draft);
            return null!;
        }

        public bool SetTitle(string title) => throw new NotSupportedException();

        public IReadOnlyList<JournalEntry> ReadJournal(long fromSequence = 1, int maximumCount = int.MaxValue) => [];

        public void SetSharingPassword(ReadOnlySpan<char> password) => throw new NotSupportedException();

        public void RemoveSharingPassword() => throw new NotSupportedException();

        public void AddOperatorAccess() => throw new NotSupportedException();

        public void Save()
        {
        }

        public void Close() => IsClosed = true;

        public void Dispose() => Close();

        public DbCommand CreateCommand(string sql) => throw new NotSupportedException();

        public SealedRecord AppendSealed(string chain, IReadOnlyList<object?> values) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTransfer : IResponseTransfer
    {
        public List<SealedResponse> Sealed { get; } = [];

        public ICaseTransaction? SealedIn { get; private set; }

        public long? Broken { get; set; }

        public SealedResponse? Read(ICaseReader reader, long sequence) => sequence == Broken
            ? throw new CaseException(CaseError.Corrupt)
            : new(
            sequence, "bitcoin-mainnet", "esplora", Guid.Empty, "test", "https://example.invalid", "kind", "subject",
            200, null, "t", "t", "t", "UTC", null, "windows", "{}", new string('0', 64), []);

        public ResponseOrigin Seal(ICaseTransaction transaction, SealedResponse response)
        {
            SealedIn = transaction;
            Sealed.Add(response);
            return null!;
        }
    }

    private sealed class FakeSanctions : ISanctionLists
    {
        private static readonly SanctionListInfo Info = new(
            "OFAC SDN", new string('a', 64), new DateOnly(2026, 11, 1), DateTimeOffset.UnixEpoch,
            new Dictionary<string, int>(), 0);

        public bool Named { get; set; }

        public SanctionListInfo? Current => Info;

        public SanctionListInfo Import(Stream content) => throw new NotSupportedException();

        public IReadOnlyList<SanctionListInfo> List() => [Info];

        public bool Locked { get; set; }

        public SanctionMatch? Find(BitcoinAddress address) => Locked ? throw new IOException("låst") : new(
            Info, Named ? [new SanctionedAddress(SanctionCurrency.Bitcoin, address.Value, "1", "Blandaren", [])] : []);
    }

    /// <summary>A Bitcoin source that answers from made-up values and seals one response per lookup.</summary>
    private sealed class FakeBitcoin : IBitcoinSource
    {
        public static readonly BlockHash Hash = BlockHash.Parse(new string('0', 60) + "3f1a");

        public LookupStatus Status { get; set; } = LookupStatus.Fetched;

        public long ConfirmedCount { get; set; } = 2;

        public int Asked { get; private set; }

        public BitcoinNetwork Network => BitcoinNetwork.Mainnet;

        public Task<BitcoinLookup<BitcoinAddressSummary>> GetAddressSummaryAsync(
            ICase @case, BitcoinAddress address, CancellationToken cancellationToken = default)
        {
            var stats = new BitcoinAddressStats(ConfirmedCount, 2, new Satoshi(5_000_000), 1, new Satoshi(880_000));
            var none = new BitcoinAddressStats(0, 0, Satoshi.Zero, 0, Satoshi.Zero);
            return Answer(@case, new BitcoinAddressSummary(address, stats, none));
        }

        public Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
            ICase @case, BitcoinAddress address, Txid? after = null, CancellationToken cancellationToken = default) =>
            Answer(@case, new BitcoinAddressPage(address, null,
            [
                Transaction('1', 921_300, new DateTimeOffset(2025, 10, 29, 12, 0, 0, TimeSpan.Zero)),
                Transaction('2', 830_000, new DateTimeOffset(2024, 3, 11, 12, 0, 0, TimeSpan.Zero)),
            ], null));

        public Task<BitcoinLookup<BitcoinTip>> GetTipHeightAsync(
            ICase @case, CancellationToken cancellationToken = default) => TipFails
            ? Task.FromResult(new BitcoinLookup<BitcoinTip>(LookupStatus.Failed, null, [], "nej"))
            : Answer(@case, new BitcoinTip(921_344));

        public bool TipFails { get; set; }

        public Task<BitcoinLookup<BitcoinTransaction>> GetTransactionAsync(
            ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default) =>
            Answer(@case, Transaction('3', 830_000, new DateTimeOffset(2024, 3, 11, 12, 0, 0, TimeSpan.Zero)));

        public Task<BitcoinLookup<BitcoinTransaction>> VerifyTransactionAsync(
            ICase @case, Txid txid, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>> GetOutputSpendsAsync(
            ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BitcoinLookup<BitcoinBlock>> GetBlockAsync(
            ICase @case, BlockHash hash, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private static BitcoinTransaction Transaction(char digit, long height, DateTimeOffset time) => new(
            Txid.Parse(new string(digit, 64)), new BlockReference(height, Hash, time), [], [], Satoshi.Zero, 0, 0, 1,
            0);

        private Task<BitcoinLookup<T>> Answer<T>(ICase @case, T value)
            where T : class
        {
            Asked++;
            if (Status != LookupStatus.Fetched)
            {
                return Task.FromResult(new BitcoinLookup<T>(Status, null, [], "nej"));
            }

            ((MemoryCase)@case).Seal();
            return Task.FromResult(new BitcoinLookup<T>(Status, value, [], null));
        }
    }

    private sealed class FakeEthereum : IEthereumSource
    {
        public Task<BitcoinLookup<EthereumAddressPage>> GetAddressHistoryAsync(
            ICase @case, EthereumAddress address, EthereumHistory history, long? atOrBelow = null,
            LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BitcoinLookup<EthereumTransaction>> GetTransactionAsync(
            ICase @case, EthereumTxHash hash, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BitcoinLookup<EthereumBlockReference>> GetBlockAsync(
            ICase @case, long height, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BitcoinLookup<EthereumTip>> GetTipHeightAsync(
            ICase @case, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
