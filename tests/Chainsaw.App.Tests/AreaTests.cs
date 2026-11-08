using Chainsaw.App.Graph;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using static Chainsaw.App.Tests.CheckSamples;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>The areas behind the actors (sketch KF1b): what is drawn around what, and where.</summary>
public sealed class AreaTests
{
    private static IReadOnlyList<AreaView> Build(
        GraphScene? scene = null, IReadOnlyList<StoredCheck>? checks = null, IReadOnlyList<OwnArea>? own = null) =>
        GraphAreas.Build(scene ?? Scene(), checks ?? [], own ?? [], TimeZoneInfo.Utc);

    [Fact]
    public void A_suggestion_waits_dashed_around_the_mixer_and_its_candidates()
    {
        var area = Assert.Single(Build(checks: [Mixer()]), a => a.CheckKey is not null);

        Assert.Equal(AreaLine.Dashed, area.Line);
        Assert.Equal([VKey, UKey, EKey], area.Nodes);
        Assert.Equal(AreaTint.Red, area.Look.Tint);
        Assert.Equal("mixer-outputs v1/deposit:0", area.Key);
    }

    [Fact]
    public void A_dismissed_candidate_leaves_the_area_and_a_decided_suggestion_stays_dashed()
    {
        var u = Candidate(U, 1, 0.8).Key;
        var e = Candidate(E, 2, 0.5).Key;
        var area = Assert.Single(
            Build(checks: [Mixer(Decision(u, CheckVerdict.Dismissed), Decision(e, CheckVerdict.Approved))]),
            a => a.CheckKey is not null);

        Assert.Equal([VKey, EKey], area.Nodes);
        Assert.Equal(AreaLine.Dashed, area.Line);
    }

    [Fact]
    public void An_address_stays_while_one_of_its_candidates_waits()
    {
        var first = Candidate(U, 1, 0.8);
        var second = Candidate(U, 3, 0.6);
        var check = Mixer(Decision(first.Key, CheckVerdict.Dismissed)) is var m
            ? m with { Finding = m.Finding with { Candidates = [first, second], Addresses = [V, U] } }
            : null;

        var area = Assert.Single(Build(checks: [check!]), a => a.CheckKey is not null);

        Assert.Equal([VKey, UKey], area.Nodes);
    }

    [Fact]
    public void A_suggestion_whose_candidates_are_all_dismissed_has_no_area()
    {
        var checks = new[]
        {
            Mixer(
                Decision(Candidate(U, 1, 0.8).Key, CheckVerdict.Dismissed),
                Decision(Candidate(E, 2, 0.5).Key, CheckVerdict.Dismissed)),
        };

        Assert.DoesNotContain(Build(checks: checks), a => a.CheckKey is not null);
    }

    [Fact]
    public void At_most_one_fill_per_actor_and_an_area_on_a_fill_is_only_an_edge()
    {
        var areas = Build(checks: [Mixer(), CoinJoin()]);
        var mixer = areas.Single(a => a.CheckKey == "mixer-outputs v1/deposit:0");
        var coinjoin = areas.Single(a => a.CheckKey == $"coinjoin-area v1/{Tx[2].Value}");

        Assert.True(mixer.IsFilled);
        Assert.False(coinjoin.IsFilled);
    }

    [Fact]
    public void A_wallet_needs_two_actors_and_is_grey()
    {
        var wallet = Assert.Single(Build(checks: [Wallet()]), a => a.Sort == AreaSort.Wallet);

        Assert.Equal([WKey, VKey], wallet.Nodes);
        Assert.Equal(AreaTint.Grey, wallet.Look.Tint);
    }

    [Fact]
    public void The_money_within_two_hours_is_a_dotted_time_area_that_is_never_filled()
    {
        var time = Assert.Single(Build(), a => a.Sort == AreaSort.Time);

        Assert.Equal(AreaLine.Dotted, time.Line);
        Assert.False(time.IsFilled);
        Assert.Equal([WKey, VKey, KKey, UKey, EKey], time.Nodes);
        Assert.Equal("Inom 2 h · 12:00–14:00", time.Label);
    }

    [Fact]
    public void Two_actors_with_the_same_exchanges_label_are_a_blue_service_area()
    {
        var labels = new Dictionary<string, IReadOnlyList<Label>>
        {
            [UKey] = [Label(U, LabelKind.Exchange, "Börs A")],
            [EKey] = [Label(E, LabelKind.Exchange, "börs a ")],
            [WKey] = [Label(W, LabelKind.Exchange, "Börs B")],
        };

        var service = Assert.Single(Build(Scene(labels)), a => a.Sort == AreaSort.Service);

        Assert.Equal([UKey, EKey], service.Nodes);
        Assert.Equal("Börs A · 2 aktörer", service.Label);
    }

    [Fact]
    public void An_own_area_follows_its_addresses_into_the_actors_that_hold_them()
    {
        var own = Assert.Single(Build(own: [Own("Säljaren", K2, U)]), a => a.IsOwn);

        Assert.Equal([KKey, UKey], own.Nodes);
        Assert.Equal(AreaLine.Solid, own.Line);
        Assert.Equal("Säljaren", own.Label);
        Assert.True(own.IsFilled);
    }

    [Fact]
    public void The_members_of_chosen_actors_are_their_addresses()
    {
        Assert.Equal(
            [CheckRules.MemberOf(K1), CheckRules.MemberOf(K2), CheckRules.MemberOf(U)],
            GraphAreas.MembersOf(Scene(), [KKey, UKey, "address:none"]));
    }

    [Fact]
    public void An_own_area_holds_ethereum_accounts_and_is_drawn_around_them()
    {
        var scene = EthSamples.Scene();
        var keys = new[] { EthSamples.Key(EthSamples.S), EthSamples.Key(EthSamples.M) };

        var members = GraphAreas.MembersOf(scene, keys);
        var own = Own("Säljaren") with { Members = members };
        var area = Assert.Single(GraphAreas.Build(scene, [], [own], TimeZoneInfo.Utc), a => a.IsOwn);

        Assert.Equal(
            [CheckRules.MemberOf(EthSamples.S.ToChainAddress()), CheckRules.MemberOf(EthSamples.M.ToChainAddress())],
            members);
        Assert.Equal(keys, area.Nodes);
    }

    [Fact]
    public void Areas_are_placed_around_their_actors_with_the_chip_at_the_top_right()
    {
        var area = Build(own: [Own("Säljaren", U)]).Single(a => a.IsOwn);
        var places = new Dictionary<string, GraphPoint> { [UKey] = new(100, 200) };

        var placed = Assert.Single(AreaGeometry.Place([area], places));

        Assert.Equal(new GraphRect(76, 176, 132 + 48, 40 + 48), placed.Frame);
        Assert.Equal(placed.Frame.Right - AreaGeometry.ChipInset, placed.Chip.Right);
        Assert.Equal(AreaGeometry.ChipWidth + AreaGeometry.PersonWidth, placed.Chip.Width);
    }

    [Fact]
    public void The_pointer_finds_a_chip_and_a_chosen_own_areas_tools_only()
    {
        var area = Build(own: [Own("Säljaren", U)]).Single(a => a.IsOwn);
        var placed = AreaGeometry.Place([area], new Dictionary<string, GraphPoint> { [UKey] = new(100, 200) });
        var place = placed[0];

        Assert.Equal(AreaPart.Chip, AreaGeometry.HitTest(placed, place.Chip.Centre, null)!.Value.Part);
        Assert.Null(AreaGeometry.HitTest(placed, place.RemoveButton.Centre, null));
        Assert.Equal(AreaPart.Remove, AreaGeometry.HitTest(placed, place.RemoveButton.Centre, area.Key)!.Value.Part);
        Assert.Equal(AreaPart.Rename, AreaGeometry.HitTest(placed, place.RenameButton.Centre, area.Key)!.Value.Part);
        Assert.Null(AreaGeometry.HitTest(placed, new GraphPoint(0, 0), area.Key));
    }

    [Fact]
    public void An_area_without_a_placed_actor_is_not_drawn()
    {
        var area = Build(own: [Own("Säljaren", U)]).Single(a => a.IsOwn);

        Assert.Empty(AreaGeometry.Place([area], new Dictionary<string, GraphPoint>()));
    }

    [Fact]
    public void A_link_to_another_chain_is_a_purple_area_around_both_ends()
    {
        var graph = GraphSamples.Graph();
        var link = new GraphEdge(
            "chain-link", EKey, "chain:eth", GraphEdgeKind.ContinuesOnOtherChain, Btc(0.5m), [], null,
            EdgeDirection.Forward);
        var chain = Node("chain:eth", GraphNodeKind.OtherChain, [], 3);
        var scene = Scene(graph: graph with { Nodes = [.. graph.Nodes, chain], Edges = [.. graph.Edges, link] });

        var area = Assert.Single(Build(scene), a => a.Sort == AreaSort.OtherChain);

        Assert.Equal([EKey, "chain:eth"], area.Nodes);
        Assert.Equal(AreaTint.Lila, area.Look.Tint);
    }
}
