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
    private readonly FakeEthereum _ethereum = new();
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
        Assert.Equal(
            [
                "Hämtat vid block 921\u00A0344", FakeBitcoin.TipHash.Value,
                "Senaste tx i block 921\u00A0300", FakeBitcoin.Hash.Value,
            ],
            preview.HeightTip.Split(Environment.NewLine));
        Assert.False(preview.IsSanctioned);
    }

    [Fact]
    public async Task The_chains_newest_block_is_asked_for_before_anything_else()
    {
        var prefetch = Prefetch();

        await prefetch.PreviewAsync(StartPointText.Parse(Samples.Address), mayFetch: true);
        await prefetch.PreviewAsync(StartPointText.Parse(Samples.Txid), mayFetch: true);
        await prefetch.PreviewAsync(StartPointText.Parse(Samples.Ethereum), mayFetch: true);
        await prefetch.PreviewAsync(StartPointText.Parse(Samples.EthereumTx), mayFetch: true);

        // What is fetched after the newest block is known to be at least that deep in the new case.
        Assert.Equal(["tip", "summary", "page", "tip", "tx"], _bitcoin.Calls);
        Assert.Equal(["tip", "history", "balance", "tip", "tx"], _ethereum.Calls);
    }

    [Fact]
    public async Task An_ethereum_address_shows_its_ether_its_stablecoins_count_activity_and_the_chains_hash()
    {
        var prefetch = Prefetch();

        var preview = await prefetch.PreviewAsync(StartPointText.Parse(Samples.Ethereum), mayFetch: true);

        Assert.Equal(PreviewState.Fetched, preview.State);
        Assert.Equal(
            ["1,2040", "12\u00A0500,0000", "2", "mars 2024 → 29 okt 2025"], preview.Facts.Select(fact => fact.Value));
        Assert.Equal([" ETH", " USDT", " tx", string.Empty], preview.Facts.Select(fact => fact.Unit));
        Assert.Equal(23_118_406, preview.Height);
        Assert.Equal(
            [
                "Hämtat vid block 23\u00A0118\u00A0406", FakeEthereum.TipHash,
                "Senaste tx i block 23\u00A0000\u00A0000", FakeEthereum.BlockHash,
            ],
            preview.HeightTip.Split(Environment.NewLine));
    }

    [Fact]
    public async Task Without_a_balance_from_the_source_none_is_shown()
    {
        _ethereum.BalanceFails = true;
        var prefetch = Prefetch();

        var preview = await prefetch.PreviewAsync(StartPointText.Parse(Samples.Ethereum), mayFetch: true);

        Assert.Equal(PreviewState.Fetched, preview.State);
        Assert.Equal([" tx", string.Empty], preview.Facts.Select(fact => fact.Unit));
    }

    [Fact]
    public async Task An_ethereum_transaction_shows_its_block_apart_from_the_chains()
    {
        var prefetch = Prefetch();

        var preview = await prefetch.PreviewAsync(StartPointText.Parse(Samples.EthereumTx), mayFetch: true);

        Assert.Equal(PreviewState.Fetched, preview.State);
        Assert.Equal(23_118_406, preview.Height);
        Assert.Contains("I block 23\u00A0000\u00A0000", preview.HeightTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_sanction_list_is_read_for_an_ethereum_address_and_names_a_token_it_is_listed_under()
    {
        _sanctions.Named = true;
        _sanctions.EthereumCurrency = "USDT";
        var prefetch = Prefetch();

        var preview = await prefetch.PreviewAsync(StartPointText.Parse(Samples.Ethereum), mayFetch: false);

        Assert.Equal(PreviewState.WaitingForExit, preview.State);
        Assert.True(preview.IsSanctioned);
        Assert.Contains("Blandaren (listad som USDT)", preview.SanctionTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_ethereum_address_named_under_another_chain_is_not_sanctioned_here()
    {
        _sanctions.Named = true;
        _sanctions.EthereumCurrency = "BSC";
        var prefetch = Prefetch();

        var preview = await prefetch.PreviewAsync(StartPointText.Parse(Samples.Ethereum), mayFetch: false);

        Assert.False(preview.IsSanctioned);
    }

    [Theory]
    [InlineData("1204000000000000000", 18, "1,2040")]
    [InlineData("1204012345678901234", 18, "≈ 1,2040")]
    [InlineData("31", 18, "< 0,0001")]
    [InlineData("0", 18, "0,0000")]
    [InlineData("12345678901200000000000000", 18, "12\u00A0345\u00A0678,9012")]
    [InlineData("12500000000", 6, "12\u00A0500,0000")]
    public void A_holding_is_cut_to_four_decimals_only_when_it_has_more_than_eight(
        string units, int decimals, string expected)
    {
        var asset = Asset.Token(Chain.Ethereum, Samples.Ethereum, "TOK", decimals);

        var text = StartPointPrefetch.Holding(new AssetAmount(asset, System.Numerics.BigInteger.Parse(units)));

        Assert.Equal(expected, text);
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

        Assert.Equal(PreviewState.Fetched, address.State);
        Assert.Null(address.Height);
        Assert.Null(transaction.Height);
        Assert.DoesNotContain("Hämtat vid", address.HeightTip, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeBitcoin.TipHash.Value, address.HeightTip, StringComparison.Ordinal);
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
    public async Task An_uncertain_hit_is_only_a_possible_sanction_and_a_certain_one_comes_first()
    {
        _sanctions.Named = true;
        _sanctions.Uncertain = true;
        var possible = await Prefetch().PreviewAsync(StartPointText.Parse(Samples.Address), mayFetch: false);
        _sanctions.AlsoCertain = true;
        var certain = await Prefetch().PreviewAsync(StartPointText.Parse(Samples.Address), mayFetch: false);

        Assert.False(possible.IsSanctioned);
        Assert.True(possible.IsPossiblySanctioned);
        Assert.StartsWith("Möjlig sanktion enligt OFAC SDN", possible.SanctionTip, StringComparison.Ordinal);
        Assert.Equal("Möjlig sanktion", possible.SanctionChipText);
        Assert.True(certain.IsSanctioned);
        Assert.Contains("· Säker", certain.SanctionTip, StringComparison.Ordinal);
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
    public async Task A_tron_address_shows_its_trx_its_usdt_count_activity_and_the_chains_id()
    {
        var tron = new FakeTron();
        var prefetch = new StartPointPrefetch(
            _memory, _transfer, _bitcoin, _ethereum, _sanctions, new FixedClock(), tron);

        var preview = await prefetch.PreviewAsync(StartPointText.Parse(Samples.Tron), mayFetch: true);

        Assert.Equal(PreviewState.Fetched, preview.State);
        Assert.Equal(["5,0000", "1\u00A0000,0000", "1", "okt 2025 → 29 okt 2025"],
            preview.Facts.Select(fact => fact.Value));
        Assert.Equal([" TRX", " USDT", " tx", string.Empty], preview.Facts.Select(fact => fact.Unit));
        Assert.Equal(80_000_000, preview.Height);
        Assert.Contains(FakeTron.TipHash, preview.HeightTip, StringComparison.Ordinal);
        Assert.Equal(["tip", "history", "balance"], tron.Calls);
        Assert.Empty(_ethereum.Calls);
    }

    [Fact]
    public async Task TRON_without_its_source_and_the_unknown_are_never_asked()
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
    public async Task What_answered_a_lookup_is_sealed_with_its_index_so_that_it_can_answer_again()
    {
        var prefetch = Prefetch();
        await prefetch.PreviewAsync(StartPointText.Parse(Samples.Address), mayFetch: true);
        _transfer.Answers = [1, 3];
        var target = new MemoryCase();

        var sealedCount = prefetch.SealInto(target);

        Assert.Equal(3, sealedCount);
        Assert.Equal([1L, 3L], _transfer.SealedWithIndex.Select(sealedResponse => sealedResponse.Response.Sequence));
        Assert.All(_transfer.SealedWithIndex, each => Assert.Equal(each.Response.Sequence, each.Index.Height));
        var journal = Assert.Single(target.Journal);
        Assert.Contains("2 av dem används som svar", journal.Summary, StringComparison.Ordinal);
        Assert.Contains("\"answers\":2", journal.Details, StringComparison.Ordinal);
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
        new(_memory, _transfer, _bitcoin, _ethereum, _sanctions, new FixedClock());

    [Fact]
    public async Task A_litecoin_address_is_fetched_through_litecoins_source_in_ltc()
    {
        var litecoin = new FakeLitecoin();
        var prefetch = new StartPointPrefetch(
            _memory, _transfer, _bitcoin, _ethereum, _sanctions, new FixedClock(), litecoin: litecoin);
        var address = BitcoinAddress.FromWitness(
            0, Enumerable.Repeat((byte)0x33, 20).ToArray(), BitcoinNetwork.Litecoin).Value;

        var preview = await prefetch.PreviewAsync(StartPointText.Parse(address), mayFetch: true);

        Assert.Equal(PreviewState.Fetched, preview.State);
        Assert.Equal(" LTC", preview.Facts[0].Unit);
        Assert.Equal(["tip", "summary", "page"], litecoin.Calls);
        Assert.Empty(_bitcoin.Calls);
    }

    [Fact]
    public async Task A_bsc_start_point_without_bscs_source_is_not_built_and_nothing_is_asked()
    {
        var preview = await Prefetch().PreviewAsync(
            StartPointText.Parse("bsc:" + Samples.Ethereum), mayFetch: true);

        Assert.Equal(PreviewState.NotBuilt, preview.State);
        Assert.Empty(_ethereum.Calls);
    }

    [Fact]
    public async Task A_solana_start_point_is_recognised_with_or_without_its_chain_and_not_built_without_its_source()
    {
        var key = Base58.Encode(Enumerable.Repeat((byte)0x44, 32).ToArray());

        var named = StartPointText.Parse("sol:" + key);
        var preview = await Prefetch().PreviewAsync(named, mayFetch: true);

        Assert.Equal(Chain.Solana, named.Chain);
        Assert.Equal(Chain.Solana, StartPointText.Parse(key).Chain);
        Assert.Equal(Chain.Solana, WatchFormViewModel.ChainOf(key));
        Assert.NotNull(CaseWorkflow.ToTrace(named));
        Assert.Equal(PreviewState.NotBuilt, preview.State);
        Assert.Empty(_ethereum.Calls);
    }

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

        public IReadOnlyList<CaseEnvelope> DescribeEnvelopes() => [];

        public FileKeyReplacement ReplaceFileKey(ReadOnlySpan<char> masterKey, ReadOnlySpan<char> sharingPassword) =>
            throw new NotSupportedException();

        public void Save()
        {
        }

        public event EventHandler<CaseSaveState>? SaveStateChanged
        {
            add { }
            remove { }
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

        public List<(SealedResponse Response, SealedResponseIndex Index)> SealedWithIndex { get; } = [];

        public ICaseTransaction? SealedIn { get; private set; }

        public long? Broken { get; set; }

        /// <summary>The responses that answered a lookup, whose index says their own number as the height.</summary>
        public HashSet<long> Answers { get; set; } = [];

        public SealedResponseIndex? ReadIndex(ICaseReader reader, long sequence) =>
            Answers.Contains(sequence) ? new SealedResponseIndex(sequence, []) : null;

        public ResponseOrigin Seal(ICaseTransaction transaction, SealedResponse response, SealedResponseIndex index)
        {
            SealedWithIndex.Add((response, index));
            return Seal(transaction, response);
        }

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

        public string EthereumCurrency { get; set; } = SanctionCurrency.Ether;

        public SanctionListInfo? Current => Info;

        public SanctionListInfo Import(Stream content) => throw new NotSupportedException();

        public IReadOnlyList<SanctionListInfo> List() => [Info];

        public bool Locked { get; set; }

        public bool Uncertain { get; set; }

        public bool AlsoCertain { get; set; }

        public SanctionMatch? Find(BitcoinAddress address) => Locked ? throw new IOException("låst") : new(
            Info, Named ? BitcoinHits(address) : []);

        private List<SanctionedAddress> BitcoinHits(BitcoinAddress address)
        {
            var hits = new List<SanctionedAddress>
            {
                new(SanctionCurrency.Bitcoin, address.Value, "1", "Blandaren", []) { IsUncertain = Uncertain },
            };
            if (AlsoCertain)
            {
                hits.Add(new(SanctionCurrency.Bitcoin, address.Value, "2", "Säker", []));
            }

            return hits;
        }

        public SanctionMatch? Find(EthereumAddress address) => Locked ? throw new IOException("låst") : new(
            Info, Named ? [new SanctionedAddress(EthereumCurrency, address.Checksummed, "1", "Blandaren", [])] : []);
    }

    /// <summary>A Bitcoin source that answers from made-up values and seals one response per lookup.</summary>
    private class FakeBitcoin : IBitcoinSource
    {
        public static readonly BlockHash Hash = BlockHash.Parse(new string('0', 60) + "3f1a");

        public static readonly BlockHash TipHash = BlockHash.Parse(new string('0', 60) + "9c2e");

        public List<string> Calls { get; } = [];

        public LookupStatus Status { get; set; } = LookupStatus.Fetched;

        public long ConfirmedCount { get; set; } = 2;

        public int Asked { get; private set; }

        public virtual BitcoinNetwork Network => BitcoinNetwork.Mainnet;

        public Task<BitcoinLookup<BitcoinAddressSummary>> GetAddressSummaryAsync(
            ICase @case, BitcoinAddress address, CancellationToken cancellationToken = default)
        {
            Calls.Add("summary");
            var stats = new BitcoinAddressStats(ConfirmedCount, 2, new Satoshi(5_000_000), 1, new Satoshi(880_000));
            var none = new BitcoinAddressStats(0, 0, Satoshi.Zero, 0, Satoshi.Zero);
            return Answer(@case, new BitcoinAddressSummary(address, stats, none));
        }

        public Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
            ICase @case, BitcoinAddress address, Txid? after = null, CancellationToken cancellationToken = default)
        {
            Calls.Add("page");
            return Answer(@case, new BitcoinAddressPage(address, null,
            [
                Transaction('1', 921_300, new DateTimeOffset(2025, 10, 29, 12, 0, 0, TimeSpan.Zero)),
                Transaction('2', 830_000, new DateTimeOffset(2024, 3, 11, 12, 0, 0, TimeSpan.Zero)),
            ], null));
        }

        public Task<BitcoinLookup<BitcoinTip>> GetTipHeightAsync(
            ICase @case, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BitcoinLookup<ChainTip>> GetTipAsync(ICase @case, CancellationToken cancellationToken = default)
        {
            Calls.Add("tip");
            return TipFails
                ? Task.FromResult(new BitcoinLookup<ChainTip>(LookupStatus.Failed, null, [], "nej"))
                : Answer(@case, new ChainTip(921_344, TipHash.Value, DateTimeOffset.UnixEpoch));
        }

        public bool TipFails { get; set; }

        public Task<BitcoinLookup<BitcoinTransaction>> GetTransactionAsync(
            ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("tx");
            return Answer(@case, Transaction('3', 830_000, new DateTimeOffset(2024, 3, 11, 12, 0, 0, TimeSpan.Zero)));
        }

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

    /// <summary>A TRON source that answers from made-up values and seals one response per lookup.</summary>
    private sealed class FakeTron : ITronSource
    {
        public static readonly string TipHash = "0000000004c4b400" + new string('c', 48);

        private static readonly EthereumAddress Other = EthereumAddress.Parse(Chain.Tron, "41" + new string('2', 40));

        public List<string> Calls { get; } = [];

        public long PageStartOf(EthereumBlockReference block) => block.Time.ToUnixTimeMilliseconds();

        public Task<BitcoinLookup<EthereumAddressPage>> GetAddressHistoryAsync(
            ICase @case, EthereumAddress address, EthereumHistory history, long? atOrBelow = null,
            LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
        {
            Calls.Add("history");
            var block = new EthereumBlockReference(
                79_000_000, null, new DateTimeOffset(2025, 10, 29, 12, 0, 0, TimeSpan.Zero));
            var hash = EthereumTxHash.Parse(Chain.Tron, new string('1', 64));
            EthereumTransfer[] transfers =
            [
                new(hash, EthereumTransferKind.Native, Other, address, new AssetAmount(Asset.Tronix, 1), 0, block),
            ];
            return Answer(@case, new EthereumAddressPage(
                address, history, atOrBelow, transfers, [], null, IsCutShort: false, IsIncomplete: false));
        }

        public Task<BitcoinLookup<EthereumTransaction>> GetTransactionAsync(
            ICase @case, EthereumTxHash hash, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BitcoinLookup<EthereumBlockReference>> GetBlockAsync(
            ICase @case, long height, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BitcoinLookup<EthereumTip>> GetTipHeightAsync(
            ICase @case, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BitcoinLookup<ChainTip>> GetTipAsync(ICase @case, CancellationToken cancellationToken = default)
        {
            Calls.Add("tip");
            return Answer(@case, new ChainTip(80_000_000, TipHash, DateTimeOffset.UnixEpoch));
        }

        public Task<BitcoinLookup<EthereumBalance>> GetBalanceAsync(
            ICase @case, EthereumAddress address, CancellationToken cancellationToken = default)
        {
            Calls.Add("balance");
            return Answer(@case, new EthereumBalance(
                address,
                new AssetAmount(Asset.Tronix, 5_000_000),
                [new AssetAmount(KnownTokens.TronUsdt, 1_000_000_000)]));
        }

        private static Task<BitcoinLookup<T>> Answer<T>(ICase @case, T value)
            where T : class
        {
            ((MemoryCase)@case).Seal();
            return Task.FromResult(new BitcoinLookup<T>(LookupStatus.Fetched, value, [], null));
        }
    }

    /// <summary>An Ethereum source that answers from made-up values and seals one response per lookup.</summary>

    /// <summary>Litecoin's source, answering as Bitcoin's fake on Litecoin's network.</summary>
    private sealed class FakeLitecoin : FakeBitcoin, ILitecoinSource
    {
        public override BitcoinNetwork Network => BitcoinNetwork.Litecoin;
    }

    private sealed class FakeEthereum : IEthereumSource
    {
        public static readonly string TipHash = "0x" + new string('0', 60) + "c27e";

        public static readonly string BlockHash = "0x" + new string('0', 60) + "5b0d";

        private static readonly EthereumAddress Other = EthereumAddress.Parse("0x" + new string('2', 40));

        public List<string> Calls { get; } = [];

        public bool BalanceFails { get; set; }

        public Task<BitcoinLookup<EthereumAddressPage>> GetAddressHistoryAsync(
            ICase @case, EthereumAddress address, EthereumHistory history, long? atOrBelow = null,
            LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
        {
            Calls.Add("history");
            var newest = new EthereumBlockReference(
                23_000_000, BlockHash, new DateTimeOffset(2025, 10, 29, 12, 0, 0, TimeSpan.Zero));
            var oldest = new EthereumBlockReference(
                19_000_000, null, new DateTimeOffset(2024, 3, 11, 12, 0, 0, TimeSpan.Zero));
            EthereumTransfer[] transfers =
            [
                Transfer('1', address, newest),
                Transfer('2', address, oldest),
            ];
            return Answer(@case, new EthereumAddressPage(
                address, history, atOrBelow, transfers, [], null, IsCutShort: false, IsIncomplete: false));
        }

        public Task<BitcoinLookup<EthereumTransaction>> GetTransactionAsync(
            ICase @case, EthereumTxHash hash, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("tx");
            var block = new EthereumBlockReference(23_000_000, BlockHash, DateTimeOffset.UnixEpoch);
            var ether = new AssetAmount(Asset.Ether, 1_000_000_000_000_000_000);
            return Answer(@case, new EthereumTransaction(
                hash, block, Other, Other, ether, new AssetAmount(Asset.Ether, 21_000), EthereumOutcome.Succeeded,
                [new EthereumTransfer(hash, EthereumTransferKind.Native, Other, Other, ether, 0, block)]));
        }

        public Task<BitcoinLookup<EthereumBlockReference>> GetBlockAsync(
            ICase @case, long height, LookupMode mode = LookupMode.AsNeeded,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BitcoinLookup<EthereumTip>> GetTipHeightAsync(
            ICase @case, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<BitcoinLookup<ChainTip>> GetTipAsync(ICase @case, CancellationToken cancellationToken = default)
        {
            Calls.Add("tip");
            return Answer(@case, new ChainTip(23_118_406, TipHash, DateTimeOffset.UnixEpoch));
        }

        public Task<BitcoinLookup<EthereumBalance>> GetBalanceAsync(
            ICase @case, EthereumAddress address, CancellationToken cancellationToken = default)
        {
            Calls.Add("balance");
            if (BalanceFails)
            {
                return Task.FromResult(new BitcoinLookup<EthereumBalance>(LookupStatus.Failed, null, [], "nej"));
            }

            var spam = Asset.Token(Chain.Ethereum, "0x" + new string('9', 40), "GRATIS", 18);

            // Anyone can make a contract call its token USDT; only Tether's contract is shown in the row.
            var fakeUsdt = Asset.Token(Chain.Ethereum, "0x" + new string('8', 40), "USDT", 6);
            return Answer(@case, new EthereumBalance(
                address,
                new AssetAmount(Asset.Ether, 1_204_000_000_000_000_000),
                [
                    new AssetAmount(KnownTokens.EthereumUsdt, 12_500_000_000),
                    new AssetAmount(fakeUsdt, 99_000_000_000),
                    new AssetAmount(spam, 1_000_000_000_000_000_000),
                ]));
        }

        private static EthereumTransfer Transfer(char digit, EthereumAddress address, EthereumBlockReference block) =>
            new(EthereumTxHash.Parse("0x" + new string(digit, 64)), EthereumTransferKind.Native, Other, address,
                new AssetAmount(Asset.Ether, 1), 0, block);

        private static Task<BitcoinLookup<T>> Answer<T>(ICase @case, T value)
            where T : class
        {
            ((MemoryCase)@case).Seal();
            return Task.FromResult(new BitcoinLookup<T>(LookupStatus.Fetched, value, [], null));
        }
    }
}
