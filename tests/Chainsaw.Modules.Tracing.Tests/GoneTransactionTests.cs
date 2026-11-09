using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Tracing.Tests.Synthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// The trace at a transaction the source no longer knows (sketch GR9a, <em>Finns inte längre</em>), against a
/// synthetic facit: the victim's coin was spent into a transaction that later left the chain, and a replacer spends
/// the same coin (replace-by-fee, a double spend), or nothing does. The trail ends at the gone transaction with a
/// reason, nothing is removed from the case, and the replacer is followed only by the user's choice.
/// </summary>
public sealed class GoneTransactionTests : IDisposable
{
    private readonly CaseHarness _harness = new();

    /// <summary>The victim's coin.</summary>
    private readonly BitcoinTransaction _fund =
        Tx("fund", 900_000).In(Wpkh("victim"), 100_000_000).Out(Wpkh("victim-wallet"), 99_990_000).Build();

    private readonly BitcoinTransaction _gone;

    private readonly BitcoinTransaction _onward;

    /// <summary>A fee bump: the same receiver, a higher fee.</summary>
    private readonly BitcoinTransaction _rbf;

    /// <summary>A double spend: the same coin to someone else.</summary>
    private readonly BitcoinTransaction _double;

    public GoneTransactionTests()
    {
        _gone = Tx("gone", 900_001).Spend(_fund, 0).Out(Wpkh("bob"), 99_980_000).Build();
        _onward = Tx("onward", 900_002).Spend(_gone, 0).Out(Wpkh("carol"), 99_970_000).Build();
        _rbf = Tx("rbf", null).Spend(_fund, 0).Out(Wpkh("bob"), 99_960_000).Build();
        _double = Tx("double", null).Spend(_fund, 0).Out(Wpkh("thief"), 99_950_000).Build();
        _harness.Source.Add(_fund, _gone, _onward);
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Before_the_transaction_is_gone_the_money_goes_on_through_it()
    {
        await ExpandAsync();

        var result = Trace(await _harness.Load());

        Assert.Equal(Btc(0), result.EndedBy(TrailEndReason.TransactionGone));
        Assert.Contains(result.Coins, coin => coin.Coin.Txid == _onward.Txid);
    }

    [Fact]
    public async Task The_trail_ends_at_a_gone_transaction_and_nothing_after_it_is_counted()
    {
        await ExpandAsync();
        MarkGone(Replaced(_rbf));

        var graph = await _harness.Load();
        var result = Trace(graph);

        var end = Assert.Single(result.Ends, end => end.Reason == TrailEndReason.TransactionGone);
        Assert.Equal(Btc(99_990_000), end.Amount.Amount);
        Assert.Equal(_gone.Txid, end.Transaction);
        Assert.Equal(GoneTransaction.Source, Assert.Single(end.Basis).Source);
        Assert.Contains("finns inte i kedjan", end.Explanation, StringComparison.Ordinal);
        Assert.Contains(_rbf.Txid.Value, end.Explanation, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Coins, coin => coin.Coin.Txid == _gone.Txid || coin.Coin.Txid == _onward.Txid);
        var carol = graph.NodeOf(Wpkh("carol"))!.Key;
        Assert.DoesNotContain(result.Edges, edge => graph.Edges.Single(line => line.Key == edge.EdgeKey).To == carol);
        Assert.Equal(TrailEndReason.TransactionGone, graph.TransactionEnds[_gone.Txid][0].Reason);
        Assert.True(graph.Transactions.ContainsKey(_gone.Txid));
        Assert.True(graph.Transactions.ContainsKey(_onward.Txid));
        Assert.True(TrailEndReason.TransactionGone.EndsTrail());
    }

    [Fact]
    public async Task A_seed_in_a_gone_transaction_ends_there_at_once()
    {
        await ExpandAsync();
        MarkGone(null);

        var result = Trace(await _harness.Load(), new Outpoint(_gone.Txid, 0));

        var end = Assert.Single(result.Ends);
        Assert.Equal(TrailEndReason.TransactionGone, end.Reason);
        Assert.Equal(Btc(99_980_000), end.Amount.Amount);
        Assert.Contains("inte hämtat", end.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_transaction_dropped_without_a_replacer_says_the_money_is_back()
    {
        await ExpandAsync();
        MarkGone(new TransactionReplacement(
            _gone.Txid, [new ReplacedCoin(0, _fund.Txid, 0, ReplacedCoinState.Unspent, null, 70)]));

        var end = Assert.Single(Trace(await _harness.Load()).Ends);

        Assert.Equal(TrailEndReason.TransactionGone, end.Reason);
        Assert.Contains("utan ersättare", end.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_replacer_is_not_followed_until_the_user_chooses_it()
    {
        await ExpandAsync();
        _harness.Source.Add(_double);
        MarkGone(Replaced(_double));

        var graph = await _harness.Load();

        Assert.False(graph.Transactions.ContainsKey(_double.Txid));
        Assert.Equal([_double.Txid], graph.Gone[_gone.Txid].Replacers);
        Assert.Empty(graph.FollowedReplacements);
    }

    [Fact]
    public async Task Following_the_replacer_brings_it_in_and_the_trace_goes_on_through_it_as_a_judgement()
    {
        await ExpandAsync();
        _harness.Source.Add(_rbf);
        MarkGone(Replaced(_rbf));
        var before = _harness.Case.ReadJournal().Count;

        var follow = await _harness.Service.FollowReplacementAsync(_harness.Case, _gone.Txid, _rbf.Txid, null);

        var graph = await _harness.Load();
        var result = Trace(graph);
        Assert.Equal(_rbf.Txid, follow.Replacer);
        Assert.Contains(follow, graph.FollowedReplacements);
        Assert.True(graph.Transactions.ContainsKey(_rbf.Txid));
        Assert.Equal(Btc(0), result.EndedBy(TrailEndReason.TransactionGone));
        var held = Assert.Single(result.Coins, coin => coin.Coin.Txid == _rbf.Txid);
        Assert.Equal([_gone.Txid], held.Amount.Replaced);
        Assert.True(held.Amount.RestsOnReplacement);
        Assert.Contains("din bedömning", held.Amount.Explain(), StringComparison.Ordinal);
        Assert.All(result.Edges.Where(edge => edge.Amount.Through.Contains(_rbf.Txid)),
            edge => Assert.True(edge.Amount.RestsOnReplacement));
        Assert.Equal(Btc(99_990_000), Sum(result.Ends));
        var journal = _harness.Case.ReadJournal();
        Assert.Equal(before + 1, journal.Count);
        Assert.Equal(TracingJournalKinds.ReplacementFollowed, journal[^1].Kind);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task Following_twice_seals_once_and_taking_back_ends_the_trail_at_the_gone_one_again()
    {
        await ExpandAsync();
        _harness.Source.Add(_rbf);
        MarkGone(Replaced(_rbf));
        var follow = await _harness.Service.FollowReplacementAsync(
            _harness.Case, _gone.Txid, _rbf.Txid, "Samma mottagare.");
        var again = await _harness.Service.FollowReplacementAsync(_harness.Case, _gone.Txid, _rbf.Txid, null);

        Assert.Equal(follow, again);
        Assert.True(_harness.Service.TakeBackReplacement(_harness.Case, follow, null));
        Assert.False(_harness.Service.TakeBackReplacement(_harness.Case, follow, null));

        var graph = await _harness.Load();
        var result = Trace(graph);
        Assert.Empty(graph.FollowedReplacements);
        Assert.False(graph.Transactions.ContainsKey(_rbf.Txid));
        Assert.Equal(Btc(99_990_000), result.EndedBy(TrailEndReason.TransactionGone));
        Assert.Equal(TracingJournalKinds.ReplacementTakenBack, _harness.Case.ReadJournal()[^1].Kind);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task A_replacer_is_followed_only_for_a_transaction_that_is_gone_and_a_replacer_the_case_knows()
    {
        await ExpandAsync();
        _harness.Source.Add(_rbf, _double);

        await Assert.ThrowsAsync<ArgumentException>(
            () => _harness.Service.FollowReplacementAsync(_harness.Case, _gone.Txid, _rbf.Txid, null));
        MarkGone(Replaced(_rbf));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _harness.Service.FollowReplacementAsync(_harness.Case, _gone.Txid, _double.Txid, null));
    }

    [Fact]
    public async Task Offline_the_replacer_cannot_be_followed_and_nothing_is_sealed()
    {
        await ExpandAsync();
        _harness.Source.Add(_rbf);
        MarkGone(Replaced(_rbf));
        _harness.Source.Online = false;
        var before = _harness.Case.ReadJournal().Count;

        await Assert.ThrowsAsync<ReplacementUnavailableException>(
            () => _harness.Service.FollowReplacementAsync(_harness.Case, _gone.Txid, _rbf.Txid, null));

        Assert.Equal(before, _harness.Case.ReadJournal().Count);
    }

    [Fact]
    public async Task One_endpoints_word_alone_does_not_end_the_trail_but_stops_it_as_not_adding_up()
    {
        await ExpandAsync();
        MarkGone(null, corroborated: false);

        var graph = await _harness.Load();
        var end = Assert.Single(Trace(graph).Ends);

        Assert.Equal(TrailEndReason.GoneUncertain, end.Reason);
        Assert.False(end.Reason.EndsTrail());
        Assert.Contains("Bara en värd", end.Explanation, StringComparison.Ordinal);
        Assert.False(graph.TransactionEnds.ContainsKey(_gone.Txid));
    }

    [Fact]
    public async Task An_honest_second_host_answering_about_the_coins_settles_what_one_endpoint_said()
    {
        await ExpandAsync();
        MarkGone(Free("second.example"), corroborated: false);

        var end = Assert.Single(Trace(await _harness.Load()).Ends);

        Assert.Equal(TrailEndReason.TransactionGone, end.Reason);
        Assert.DoesNotContain("enda källa", end.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_lying_endpoint_among_two_cannot_confirm_itself_through_the_coins()
    {
        await ExpandAsync();
        MarkGone(Free("esplora.test"), corroborated: false);

        var end = Assert.Single(Trace(await _harness.Load()).Ends);

        Assert.Equal(TrailEndReason.GoneUncertain, end.Reason);
        Assert.Contains("Bara en värd", end.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_a_single_source_the_end_says_it_rests_on_it()
    {
        await ExpandAsync();
        MarkGone(Free("esplora.test"), corroborated: true, singleSource: true);

        var end = Assert.Single(Trace(await _harness.Load()).Ends);

        Assert.Equal(TrailEndReason.TransactionGone, end.Reason);
        Assert.Contains(GoneTransaction.OneSourceText("esplora.test"), end.Explanation, StringComparison.Ordinal);
    }

    /// <summary>The answer, from a host, that the victim's coin is free again.</summary>
    private TransactionReplacement Free(string host) => new(
        _gone.Txid, [new ReplacedCoin(0, _fund.Txid, 0, ReplacedCoinState.Unspent, null, 70) { Host = host }]);

    [Fact]
    public async Task Answers_that_still_name_the_gone_transaction_never_end_the_trail_nor_let_a_replacer_be_followed()
    {
        await ExpandAsync();
        _harness.Source.Add(_rbf);
        MarkGone(new TransactionReplacement(_gone.Txid,
        [
            new ReplacedCoin(0, _fund.Txid, 0, ReplacedCoinState.SpentByItself, null, 70),
            new ReplacedCoin(1, _rbf.Txid, 0, ReplacedCoinState.SpentByOther, new BitcoinSpend(_rbf.Txid, 0, null), 71),
        ]));

        var end = Assert.Single(Trace(await _harness.Load()).Ends);

        Assert.Equal(TrailEndReason.GoneUncertain, end.Reason);
        Assert.Contains("går inte ihop", end.Explanation, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ArgumentException>(
            () => _harness.Service.FollowReplacementAsync(_harness.Case, _gone.Txid, _rbf.Txid, null));
    }

    [Fact]
    public async Task A_replacer_whose_own_input_does_not_spend_the_coin_is_refused()
    {
        await ExpandAsync();
        _harness.Source.Add(_rbf);
        MarkGone(new TransactionReplacement(_gone.Txid,
            [new ReplacedCoin(
                0, _fund.Txid, 0, ReplacedCoinState.SpentByOther, new BitcoinSpend(_rbf.Txid, 3, null), 70)]));
        var before = _harness.Case.ReadJournal().Count;

        await Assert.ThrowsAsync<ArgumentException>(
            () => _harness.Service.FollowReplacementAsync(_harness.Case, _gone.Txid, _rbf.Txid, null));

        Assert.Equal(before, _harness.Case.ReadJournal().Count);
    }

    [Fact]
    public async Task Exposure_paid_out_stops_at_the_gone_transaction_and_counts_only_the_replacer_once_followed()
    {
        await ExpandAsync();
        _harness.Source.Add(_rbf);
        MarkGone(Replaced(_rbf));
        var before = Exposure(await _harness.Load(), "victim-wallet", ExposureDirection.Out, "bob");

        await _harness.Service.FollowReplacementAsync(_harness.Case, _gone.Txid, _rbf.Txid, null);
        var after = Exposure(await _harness.Load(), "victim-wallet", ExposureDirection.Out, "bob");

        Assert.Equal(Btc(99_980_000), before.Total);
        Assert.Equal(99_980_000, (long)before.Rest[ExposureRest.TrailEnds].Units);
        Assert.Empty(before.Reached);
        Assert.Equal(Btc(99_960_000), after.Total);
        Assert.False(after.Rest.ContainsKey(ExposureRest.TrailEnds));
        Assert.Equal(99_960_000, (long)Assert.Single(after.Reached).Amount.Units);
    }

    [Fact]
    public async Task Exposure_got_reads_back_through_a_followed_replacer_as_the_trace_does()
    {
        await ExpandAsync();
        _harness.Source.Add(_rbf);
        MarkGone(Replaced(_rbf));
        await _harness.Service.FollowReplacementAsync(_harness.Case, _gone.Txid, _rbf.Txid, null);

        var got = Exposure(await _harness.Load(), "bob", ExposureDirection.In, "victim-wallet");

        Assert.Equal(Btc(99_960_000), got.Total);
        Assert.False(got.Rest.ContainsKey(ExposureRest.Conflicting));
        Assert.Single(got.Reached);
    }

    [Fact]
    public async Task A_confirmed_replacer_followed_still_rests_on_the_users_judgement()
    {
        var confirmed = Tx("rbf-confirmed", 900_003).Spend(_fund, 0).Out(Wpkh("bob"), 99_960_000).Build();
        await ExpandAsync();
        _harness.Source.Add(confirmed);
        MarkGone(new TransactionReplacement(_gone.Txid,
        [
            new ReplacedCoin(0, _fund.Txid, 0, ReplacedCoinState.SpentByOther,
                new BitcoinSpend(confirmed.Txid, 0, confirmed.Block), 70),
        ]));
        await _harness.Service.FollowReplacementAsync(_harness.Case, _gone.Txid, confirmed.Txid, null);

        var result = Trace(await _harness.Load());

        var lines = result.Edges.Where(edge => edge.Amount.Through.Contains(confirmed.Txid)).ToList();
        Assert.NotEmpty(lines);
        Assert.All(lines, edge => Assert.True(edge.Amount.RestsOnReplacement));
    }

    /// <summary>Reads exposure from an actor by FIFO, with one actor counted as a service.</summary>
    private static ExposureResult Exposure(
        TraceGraph graph, string subject, ExposureDirection direction, string service) =>
        new ExposureAnalysis([new FifoMethod(), new HaircutMethod()]).Analyse(new ExposureRequest(
            graph, graph.NodeOf(Wpkh(subject))!.Key, Asset.Bitcoin, direction, 3, TracingMethodKind.Fifo,
            new HashSet<string>(StringComparer.Ordinal) { graph.NodeOf(Wpkh(service))!.Key }));

    /// <summary>Expands the victim's coin and the transaction after it, so that the graph knows where they went.
    /// </summary>
    private async Task ExpandAsync()
    {
        await _harness.Expand(_fund);
        await _harness.Expand(_gone);
    }

    /// <summary>Says, as Chains would from the sealed answers, that the gone transaction left the chain.</summary>
    private void MarkGone(TransactionReplacement? replacement, bool corroborated = true, bool singleSource = false)
    {
        var before = new BlockSighting(900_001, _gone.Block!.Hash.Value, DateTimeOffset.UnixEpoch, 10)
        {
            Endpoint = "esplora.test",
        };
        var now = new BlockSighting(null, null, DateTimeOffset.UnixEpoch.AddHours(1), 60) { Endpoint = "esplora.test" };
        _harness.Source.Changes.Add(
            new BlockChange(_gone.Txid, BlockChangeKind.Gone, before, now)
            {
                IsCorroborated = corroborated, GoneHosts = ["esplora.test"], IsSingleSource = singleSource,
            });
        if (replacement is not null)
        {
            _harness.Source.Replacements[_gone.Txid] = replacement;
        }
    }

    /// <summary>The case's answer that a transaction spends the victim's coin in place of the gone one.</summary>
    private TransactionReplacement Replaced(BitcoinTransaction replacer) => new(
        _gone.Txid,
        [new ReplacedCoin(
            0, _fund.Txid, 0, ReplacedCoinState.SpentByOther, new BitcoinSpend(replacer.Txid, 0, null), 70)]);

    /// <summary>Traces the victim's coin, or another, by FIFO.</summary>
    private TraceResult Trace(TraceGraph graph, Outpoint? seed = null) => _harness.Service.Trace(
        graph, new TraceRequest(TracingMethodKind.Fifo, [new TraceSeed(seed ?? new Outpoint(_fund.Txid, 0))]));

    private static AssetAmount Sum(IEnumerable<TraceEnd> ends) =>
        ends.Aggregate(Btc(0), (sum, end) => sum + end.Amount.Amount);

    private static AssetAmount Btc(long satoshi) => new Satoshi(satoshi).ToAmount();
}
