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
        var outputs = Assert.Single(checks, check => check.Analysis.Name == DecideStep.MixerOutputs);
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
