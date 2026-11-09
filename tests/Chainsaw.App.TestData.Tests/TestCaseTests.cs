// The test case and its made-up source exist only in a Debug build.
#if DEBUG
using Chainsaw.App.Graph;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Chains.TestData;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.TestData.Tests;

/// <summary>
/// The test case is built through the real modules from the made-up chain, everything the graph can show is in it,
/// and the made-up source is never used for anything but a test case.
/// </summary>
public sealed class TestCaseTests : IDisposable
{
    /// <summary>
    /// What the built graph does not show as a badge: the start point and the watch are drawn by the graph from the
    /// trace and the watches, so they are checked on their own below, and an exported request's mark is drawn from the
    /// case's exports, not from the scene. Kan frysas is there since round 8: USDT on TRON.
    /// </summary>
    // A former sanction comes only from a list (round 5), and the made-up list has no removed entries yet (BACKLOG).
    private static readonly NodeBadge[] NotAsBadges =
        [NodeBadge.StartPoint, NodeBadge.Watched, NodeBadge.RequestExported, NodeBadge.FormerlySanctioned];


    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void The_made_up_chain_adds_up_and_every_txid_is_its_bytes_hash()
    {
        var chain = _harness.Get<TestChain>();

        Assert.All(chain.Transactions, transaction =>
        {
            Assert.Equal(transaction.Txid, TestTransaction.DisplayHash(transaction.Bytes));
            Assert.True(transaction.Fee >= 0, transaction.Label);
        });
        Assert.Equal(TestStoryChain.Tip, chain.Tip);
        Assert.NotNull(chain.BlockAt(TestStoryChain.First));
    }

    [Fact]
    public async Task The_scenario_is_built_without_problems_and_the_case_verifies()
    {
        var build = await BuildAsync();

        Assert.Empty(build.Problems);
        using var @case = _harness.Get<ICaseStore>().Open(build.CaseNumber);
        Assert.Equal(TestStory.Title, @case.Title);
        Assert.True(_harness.Get<ICaseVerifier>().Verify(@case).IsIntact);
    }

    [Fact]
    public async Task Every_sort_and_mark_of_the_visual_language_is_in_the_test_case_graph()
    {
        var build = await BuildAsync();
        using var @case = _harness.Get<ICaseStore>().Open(build.CaseNumber);

        // The hub is left for the user to expand; once expanded, what it held back is the clue node (EX1).
        await _harness.Get<ITracingService>().ExpandAsync(
            @case, ExpansionRequest.ForAddress(_harness.Get<TestActors>().Hub.Address!));
        var scene = await Source().LoadAsync(@case, TracingMethodKind.Fifo, CancellationToken.None);

        var sorts = scene.Nodes.Select(node => node.Look.Sort).ToHashSet();
        Assert.All(VisualLanguage.Sorts, look => Assert.Contains(look.Sort, sorts));
        var badges = scene.Nodes.SelectMany(node => node.Badges).ToHashSet();
        Assert.All(
            VisualLanguage.Badges.Where(look => !NotAsBadges.Contains(look.Badge)),
            look => Assert.Contains(look.Badge, badges));
        Assert.Contains(scene.Nodes, node => node.IsStartPoint);
        var watched = _harness.Get<IMonitoringService>().GetWatches(@case).Single().Subject.Anchor.Value;
        Assert.Contains(scene.Nodes, node => node.Node.GraphAddresses.Any(address => address.Value == watched));
    }

    [Fact]
    public async Task The_test_case_has_a_waiting_hit_a_suggestion_an_own_area_and_a_cluster_with_a_why()
    {
        var build = await BuildAsync();
        using var @case = _harness.Get<ICaseStore>().Open(build.CaseNumber);
        var actors = _harness.Get<TestActors>();

        var monitoring = _harness.Get<IMonitoringService>();
        var hit = Assert.Single(monitoring.GetHits(@case));
        Assert.False(monitoring.GetSeen(@case).TryGetValue(hit.WatchId, out var seen) && seen >= hit.Sequence);
        var checks = _harness.Get<ICheckService>();
        var graph = await _harness.Get<ITracingService>().LoadAsync(@case);
        Assert.Contains(checks.Run(@case, graph).Checks, check => check.Finding.Kind == CheckKind.Suggestion);
        Assert.Contains(checks.GetAreas(@case), area => area.Name == "Uttagsvägar");
        var cluster = _harness.Get<IClusteringService>().GetCluster(@case, actors.Scammer.Address!);
        Assert.Contains(actors.Scammer3.Address!, cluster.Addresses);
        Assert.NotEmpty(cluster.Links);
    }

    [Fact]
    public async Task The_test_case_has_a_comment_tied_to_the_victim_with_a_link_to_the_exchange()
    {
        var build = await BuildAsync();
        using var @case = _harness.Get<ICaseStore>().Open(build.CaseNumber);
        var actors = _harness.Get<TestActors>();

        var anchor = GraphPile.MemberKey(actors.Victim.Address!);
        var comment = Assert.Single(_harness.Get<IGraphCommentService>().GetComments(@case), c => c.Anchor == anchor);
        Assert.Contains("chainsaw:bitcoin/" + actors.Exchange.Address!.Value, comment.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_response_in_the_test_case_is_marked_as_made_up_and_nothing_went_out()
    {
        var build = await BuildAsync();
        using var @case = _harness.Get<ICaseStore>().Open(build.CaseNumber);

        var transfer = _harness.Get<IResponseTransfer>();
        var responses = Enumerable.Range(1, 2_000)
            .Select(sequence => @case.Read(reader => transfer.Read(reader, sequence)))
            .OfType<SealedResponse>()
            .ToList();

        Assert.NotEmpty(responses);
        Assert.All(responses, response =>
        {
            Assert.Equal(TestDataBitcoin.SourceCode, response.Source);
            Assert.Equal(TestDataBitcoin.EndpointName, response.EndpointName);
        });
        Assert.Equal(0, _harness.Outside.Attempts);
    }

    [Fact]
    public async Task A_real_case_never_asks_the_made_up_source()
    {
        var chain = _harness.Get<TestChainHttp>();
        using var real = _harness.Get<ICaseStore>().Create("2026-0001");
        var txid = Txid.Parse(_harness.Get<TestChain>()[TestStoryChain.VictimPayment].Txid);

        var lookup = await _harness.Get<IBitcoinSource>().GetTransactionAsync(real, txid);

        Assert.Null(lookup.Value);
        Assert.Equal(0, chain.Requests);
        Assert.True(_harness.Outside.Attempts > 0);
        Assert.False(TestCases.IsTestCase(real));
    }

    [Fact]
    public async Task The_test_case_never_asks_the_real_source_and_its_watches_stay_out_of_the_background()
    {
        var build = await BuildAsync();
        using var @case = _harness.Get<ICaseStore>().Open(build.CaseNumber);
        var address = _harness.Get<TestActors>().Hub.Address!;

        var page = await _harness.Get<IBitcoinSource>().GetAddressTransactionsAsync(@case, address);

        Assert.Equal(TestChainHttp.PageSize, page.Value!.Transactions.Count);
        Assert.Equal(0, _harness.Outside.Attempts);
        Assert.Empty(_harness.Get<IBackgroundWatches>().Describe(@case, DateTimeOffset.UtcNow).Watches);
    }

    [Fact]
    public async Task The_hub_holds_transactions_back_when_the_user_expands_it()
    {
        var build = await BuildAsync();
        using var @case = _harness.Get<ICaseStore>().Open(build.CaseNumber);

        var result = await _harness.Get<ITracingService>().ExpandAsync(
            @case, ExpansionRequest.ForAddress(_harness.Get<TestActors>().Hub.Address!));

        Assert.NotNull(result.HeldBack);
        Assert.True(result.HeldBack!.Available > result.HeldBack.Taken);
    }

    [Fact]
    public async Task A_second_build_takes_the_next_number()
    {
        var first = await BuildAsync();
        var builder = _harness.Get<TestCaseBuilder>();

        Assert.Equal("TEST-0001", first.CaseNumber);
        Assert.Equal("TEST-0002", builder.NextNumber());
    }

    [Fact]
    public async Task The_label_from_the_colleagues_case_comes_in_as_from_another_case()
    {
        var build = await BuildAsync();
        using var @case = _harness.Get<ICaseStore>().Open(build.CaseNumber);

        var labels = _harness.Get<IAttributionService>().GetLabels(@case, _harness.Get<TestActors>().Escrow.Address!);

        Assert.Contains(labels, label => label.Kind == LabelKind.EscrowMarket && label.IsOnlyFromOtherCases);
    }

    private async Task<TestBuild> BuildAsync()
    {
        var builder = _harness.Get<TestCaseBuilder>();
        return await builder.BuildAsync(builder.NextNumber());
    }

    private GraphSource Source() => new(
        _harness.Get<ITracingService>(),
        _harness.Get<IAttributionService>(),
        _harness.Get<IClusteringService>(),
        _harness.Get<ILabelLibrary>());
}
#endif
