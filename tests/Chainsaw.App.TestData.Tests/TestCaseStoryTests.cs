// The test case and its made-up source exist only in a Debug build.
#if DEBUG
using Chainsaw.App.Graph;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Pricing.TestData;
using Chainsaw.Modules.Requests.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.TestData.Tests;

/// <summary>
/// What has been built since the test case was rebuilt can be seen in it from the start (BACKLOG, Testärendet): made-up
/// rates that never go out, a changed block, a sanction hit, the user's own decisions, split, edited comment and
/// trash, mixer after mixer, a cash-out that gives a request and a report, and exposure over several hops.
/// </summary>
public sealed class TestCaseStoryTests : IDisposable
{
    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task The_test_case_gets_made_up_rates_and_nothing_goes_out()
    {
        using var @case = await OpenAsync();
        var rates = _harness.Get<TestRatesHttp>();
        var at = _harness.Get<TestChain>()[TestStoryChain.VictimPayment].Height;
        var time = _harness.Get<TestChain>().TimeOf(at);

        var lookup = await _harness.Get<IPriceService>().GetPriceAsync(@case, new PriceRequest(Chain.Bitcoin, time));

        Assert.NotNull(lookup.Value);
        Assert.NotEmpty(lookup.Origins);
        Assert.All(lookup.Origins, origin => Assert.Contains(
            origin.Source, (string[])[TestDataPricing.CryptoSourceName, TestDataPricing.FiatSourceName]));
        Assert.True(rates.Requests > 0);
        Assert.Equal(0, _harness.Outside.Attempts);
    }

    [Fact]
    public async Task A_real_case_never_gets_made_up_rates()
    {
        var rates = _harness.Get<TestRatesHttp>();
        using var real = _harness.Get<ICaseStore>().Create("2026-0002");

        var lookup = await _harness.Get<IPriceService>().GetPriceAsync(
            real, new PriceRequest(Chain.Bitcoin, new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero)));

        Assert.Null(lookup.Value);
        Assert.Equal(0, rates.Requests);
    }

    [Fact]
    public async Task The_cash_out_has_moved_block_with_both_answers_kept()
    {
        using var @case = await OpenAsync();
        var txid = Txid.Parse(_harness.Get<TestChain>()[TestStoryChain.PeelHops[^1]].Txid);

        var changes = await _harness.Get<IBitcoinSource>().GetBlockChangesAsync(@case);

        var change = Assert.Single(changes, each => each.TransactionId == txid.Value);
        Assert.Equal(BlockChangeKind.Moved, change.Kind);
        Assert.Equal(change.Before.Height, change.Now.Height);
        Assert.NotEqual(change.Before.Hash, change.Now.Hash);
    }

    [Fact]
    public async Task The_sanctioned_actor_is_a_hit_on_the_made_up_list_and_only_while_building()
    {
        using var @case = await OpenAsync();
        var sanctioned = _harness.Get<TestActors>().Sanctioned.Address!;

        var labels = _harness.Get<IAttributionService>().GetLabels(@case, sanctioned);

        var hit = Assert.Single(labels, label => label.Kind == LabelKind.Sanctioned);
        Assert.Contains(hit.Origins, origin => origin.Source == LabelSource.SanctionList
            && origin.SourceName == TestDataSanctionLists.ListName);
        Assert.Null(_harness.Get<ISanctionLists>().Find(sanctioned));
    }

    [Fact]
    public async Task The_users_own_decisions_split_and_comment_history_are_there()
    {
        using var @case = await OpenAsync();
        var actors = _harness.Get<TestActors>();
        var graph = await _harness.Get<ITracingService>().LoadAsync(@case);

        var checks = _harness.Get<ICheckService>().Run(@case, graph).Checks;

        var wallet = Assert.Single(checks, check => check.Analysis.Name == CheckAnalysisNames.Wallets
            && check.Finding.Addresses.Contains(actors.MixerOut1.Address!));
        Assert.Contains(wallet.Decisions, decision => decision.Verdict == CheckVerdict.Approved);
        var deposit = _harness.Get<TestChain>()[TestStoryChain.Distribution].Txid + ":";
        var outputs = Assert.Single(checks, check => check.Analysis.Name == DecideStep.MixerOutputs
            && check.Finding.Key.StartsWith(deposit, StringComparison.Ordinal));
        Assert.Contains(outputs.Decisions, decision => decision.Verdict == CheckVerdict.Approved);
        Assert.True(outputs.Waiting > 0);
        Assert.Contains(checks, check => check.Analysis.Name == "mixer-chain");
        var clustering = _harness.Get<IClusteringService>();
        Assert.NotEmpty(clustering.GetSplitHistory(@case));
        var consolidated = clustering.GetCluster(@case, actors.Consolidated.Address!);
        Assert.DoesNotContain(actors.TopUp.Address!, consolidated.Addresses);
        var comments = _harness.Get<IGraphCommentService>();
        var anchor = GraphPile.MemberKey(actors.Victim.Address!);
        var comment = comments.GetComments(@case).Single(each => each.Anchor == anchor);
        Assert.Contains(comments.GetCommentHistory(@case, comment.Id), record => record.Action == CommentAction.Edited);
    }

    [Fact]
    public async Task A_comment_and_an_own_area_lie_in_the_trash()
    {
        using var @case = await OpenAsync();

        var members = _harness.Get<IGraphTrash>().GetThrows(@case).SelectMany(thrown => thrown.Members).ToList();

        Assert.Contains(members, TrashKeys.IsComment);
        Assert.Contains(members, TrashKeys.IsArea);
    }

    [Fact]
    public async Task The_cash_out_gives_a_request_and_a_report()
    {
        using var @case = await OpenAsync();
        var scene = await SceneAsync(@case);

        var exit = Assert.Single(RequestExits.Find(scene), each => each.Name == "Nordbörsen");
        var draft = new RequestDraft(
            exit.Kind, RequestLanguage.Swedish, exit.Target, exit.Deposits, exit.Trail, TimeZoneInfo.Utc);
        var document = _harness.Get<IRequestService>().Compose(@case, draft);
        var picked = scene.Nodes.Where(node => node.Node.GraphAddresses.Count > 0).Select(node => node.Key).ToList();
        var report = ReportSelection.Build(scene, picked, new Dictionary<string, Cluster>());

        Assert.NotEmpty(exit.Deposits);
        Assert.NotNull(exit.Trail);
        Assert.Contains(exit.Deposits[0].Transaction, document.Markdown, StringComparison.Ordinal);
        Assert.NotEmpty(report.Trails);
    }

    [Fact]
    public async Task The_scammers_exposure_reaches_services_over_several_hops()
    {
        using var @case = await OpenAsync();
        var scene = await SceneAsync(@case);
        var scammer = _harness.Get<TestActors>().Scammer.Address!;
        var subject = scene.Nodes.First(node => node.Node.GraphAddresses.Contains(scammer)).Key;

        var result = _harness.Get<IExposureAnalysis>().Analyse(
            ExposureMap.Request(scene, subject, Asset.Bitcoin, ExposureDirection.Out, ExposureRequest.MaxDepth));

        Assert.Contains(result.Reached, reach => reach.Hops >= 2);
    }

    [Fact]
    public async Task The_swap_continues_on_tron_where_usdt_reaches_sydborsen_and_nothing_goes_out()
    {
        using var @case = await OpenAsync();

        var scene = await SceneAsync(@case);
        var graph = await _harness.Get<ITracingService>().LoadAsync(@case);

        var link = Assert.Single(graph.CrossChainLinks);
        Assert.Equal(Chain.Tron, link.To.Chain);
        Assert.Equal(TestTronChain.Id(TestTronChain.SwapPayout), link.To.Id);
        var mule = scene.Nodes.Single(node => node.Node.Addresses.Contains(TestTronChain.Mule.ToChainAddress()));
        Assert.Equal(Chain.Tron, mule.Node.Chain);
        Assert.Contains(NodeBadge.CanBeFrozen, mule.Badges);
        Assert.Contains(graph.Edges, edge => edge.Amount.Asset == KnownTokens.TronUsdt
            && edge.To == graph.NodeOf(TestTronChain.Exchange.ToChainAddress())!.Key);
        Assert.Equal(0, _harness.Outside.Attempts);
    }

    [Fact]
    public async Task The_obfuscation_checks_and_the_service_from_behaviour_are_in_the_test_case()
    {
        using var @case = await OpenAsync();
        var actors = _harness.Get<TestActors>();
        var chain = _harness.Get<TestChain>();
        var graph = await _harness.Get<ITracingService>().LoadAsync(@case);

        var checks = _harness.Get<ICheckService>().Run(@case, graph).Checks;

        Assert.Contains(checks, check => check.Finding.Key == "dust:" + chain[TestStoryChain.Dusting].Txid);
        var poison = Assert.Single(checks, check => check.Finding.Key == "poison:" + actors.LookAlike.Address!.Value);
        Assert.Contains(actors.SouthExchange.Address!, poison.Finding.Addresses);
        Assert.Contains(checks, check => check.Analysis.Name == "pass-through"
            && check.Finding.Key == chain[TestStoryChain.PostMixHops[0]].Txid
            && check.Finding.Title.Contains("fördröjning", StringComparison.Ordinal));
        var merge = checks.Where(check => check.Analysis.Name == "split-merge").ToList();
        Assert.True(merge.Count >= 2, "The split and the parts that meet.");
        Assert.Contains(checks, check => check.Analysis.Name == "payjoin-hint"
            && check.Finding.Key == chain[TestStoryChain.PayJoin].Txid && check.Finding.Title == "Möjlig PayJoin");
        Assert.Contains(checks, check => check.Analysis.Name == "service-behaviour"
            && check.Finding.Addresses.Contains(actors.SouthHot.Address!));
    }

    [Fact]
    public async Task The_replaced_spend_is_gone_and_replaced_by_the_one_with_the_higher_fee()
    {
        using var @case = await OpenAsync();
        var chain = _harness.Get<TestChain>();
        var txid = Txid.Parse(chain[TestStoryChain.ReplacedSpend].Txid);
        var source = _harness.Get<IBitcoinSource>();

        var change = Assert.Single(await source.GetBlockChangesAsync(@case), each => each.TransactionId == txid.Value);
        var replacement = await source.GetReplacementAsync(@case, txid);

        Assert.Equal(BlockChangeKind.Gone, change.Kind);
        Assert.Equal(ReplacementKind.Replaced, replacement.Value!.Kind);
        Assert.Equal(chain[TestStoryChain.ReplacingSpend].Txid, Assert.Single(replacement.Value.Replacers).Value);
        Assert.Equal(0, _harness.Outside.Attempts);
    }

    [Fact]
    public async Task The_change_conclusion_that_rests_on_the_gone_spend_is_held_back()
    {
        using var @case = await OpenAsync();
        var chain = _harness.Get<TestChain>();

        var held = Assert.Single(_harness.Get<IClusteringService>().GetHeldBack(@case));

        Assert.Equal(chain[TestStoryChain.ReusedDeposit].Txid, held.Txid.Value);
        Assert.Equal([chain[TestStoryChain.ReplacedSpend].Txid], held.Missing.Select(txid => txid.Value));
        Assert.True(held.IsWaiting);
        Assert.NotEmpty(held.Evidence);
        Assert.Equal(0, _harness.Outside.Attempts);
    }

    [Fact]
    public async Task A_possible_sanction_a_reported_address_and_a_label_on_a_transaction_are_there()
    {
        using var @case = await OpenAsync();
        var actors = _harness.Get<TestActors>();
        var attribution = _harness.Get<IAttributionService>();
        var join = _harness.Get<TestChain>()[TestStoryChain.Join].Txid;

        var possible = attribution.GetLabels(@case, actors.Otc.Address!);
        var reported = attribution.GetLabels(@case, actors.Scammer2.Address!);
        var onTransaction = _harness.Get<ITransactionLabels>().For(@case, Chain.Bitcoin, join);

        Assert.Contains(possible, label => label.Kind == LabelKind.PossiblySanctioned
            && label.Name == TestDataSanctionLists.UncertainEntryName);
        Assert.Contains(reported, label => label.Kind == LabelKind.Reported
            && label.Name == TestDataOpenSources.EntryName);
        Assert.Equal(LabelKind.SwapBridge, Assert.Single(onTransaction).Kind);
        Assert.Equal(0, _harness.Outside.Attempts);
    }

    [Fact]
    public async Task On_ethereum_the_usdt_is_swapped_through_the_router_into_tornado_cash_and_nothing_goes_out()
    {
        using var @case = await OpenAsync();

        var scene = await SceneAsync(@case);

        var swap = Assert.Single(scene.Edges, edge => edge.Swaps.Count > 0
            && edge.Swaps.Any(each => each.Transaction.Value == TestEthereumChain.HashOf(TestEthereumChain.Swap)));
        Assert.NotNull(swap.SwapRoute);
        Assert.False(swap.IsSwapPriceAbsurd);
        var scammer = scene.Nodes.Single(node =>
            node.Node.Addresses.Contains(TestEthereumChain.Scammer.ToChainAddress()));
        Assert.Contains(NodeBadge.CanBeFrozen, scammer.Badges);
        var tornado = scene.Nodes.Single(node =>
            node.Node.Addresses.Contains(TestEthereumChain.Tornado.ToChainAddress()));
        Assert.Contains(NodeBadge.FormerlySanctioned, tornado.Badges);
        Assert.Equal(0, _harness.Outside.Attempts);
    }

    [Fact]
    public async Task The_two_victims_money_goes_the_same_way_and_a_run_of_alike_amounts_reaches_sydborsen()
    {
        using var @case = await OpenAsync();
        var actors = _harness.Get<TestActors>();
        var chain = _harness.Get<TestChain>();
        var graph = await _harness.Get<ITracingService>().LoadAsync(@case);

        var checks = _harness.Get<ICheckService>().Run(@case, graph).Checks
            .Where(check => check.Analysis.Name == "pattern")
            .ToList();

        var way = Assert.Single(checks, check => check.Finding.Key.StartsWith(
            "route:" + actors.PatternDeposit.Address!.Value, StringComparison.Ordinal));
        Assert.StartsWith("Samma väg, 2 delar", way.Finding.Title, StringComparison.Ordinal);
        Assert.All(
            TestStoryChain.PatternHops.SelectMany(hops => hops),
            hop => Assert.Contains(way.Finding.Basis, basis => basis.Txid.Value == chain[hop].Txid));
        var alike = Assert.Single(checks, check =>
            check.Finding.Key == "struct:" + chain[TestStoryChain.StructuringPayments[0]].Txid);
        Assert.StartsWith("Likadana belopp, 4 i följd", alike.Finding.Title, StringComparison.Ordinal);
        Assert.Equal(0, _harness.Outside.Attempts);
    }

    [Fact]
    public async Task The_payment_to_the_swap_service_gets_its_continuation_on_ethereum_suggested()
    {
        using var @case = await OpenAsync();
        var chain = _harness.Get<TestChain>();
        var graph = await _harness.Get<ITracingService>().LoadAsync(@case);

        var checks = _harness.Get<ICheckService>().Run(@case, graph).Checks;

        var suggestion = Assert.Single(checks, check => check.Analysis.Name == CheckAnalysisNames.CrossChain
            && check.Finding.Departure?.Coin?.Txid.Value == chain[TestStoryChain.BridgePayment].Txid);
        Assert.Equal(CheckKind.Suggestion, suggestion.Finding.Kind);
        var first = suggestion.Finding.Candidates.MaxBy(candidate => candidate.Score)!;
        Assert.Equal(Chain.Ethereum, first.Arrival!.Transaction.Chain);
        Assert.Equal(
            TestEthereumChain.HashOf(TestEthereumChain.Swap),
            first.Arrival.Transaction.Id,
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(0, _harness.Outside.Attempts);
    }

    private async Task<ICase> OpenAsync()
    {
        var builder = _harness.Get<TestCaseBuilder>();
        var build = await builder.BuildAsync(builder.NextNumber());
        Assert.Empty(build.Problems);
        return _harness.Get<ICaseStore>().Open(build.CaseNumber);
    }

    private Task<GraphScene> SceneAsync(ICase @case) => new GraphSource(
        _harness.Get<ITracingService>(),
        _harness.Get<IAttributionService>(),
        _harness.Get<IClusteringService>(),
        _harness.Get<ILabelLibrary>()).LoadAsync(@case, TracingMethodKind.Fifo, CancellationToken.None);
}
#endif
