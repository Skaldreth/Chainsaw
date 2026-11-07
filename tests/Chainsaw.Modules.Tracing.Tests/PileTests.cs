using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using static Chainsaw.Modules.Tracing.Tests.Synthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// The piles and the clues (sketches EX1 and EX2b): an expansion's many new counterparties lie in a pile on their side
/// of the actor, what was held back is a clue with the next lookup, the user's picks are kept in the case, and nothing
/// leaves the graph (principle 1).
/// </summary>
public sealed class PileTests : IDisposable
{
    private readonly CaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    /// <summary>Gives an address that received once from each of a number of payers.</summary>
    private BitcoinAddress Receiving(string name, int payers)
    {
        var address = Wpkh(name);
        _harness.Source.Add([
            .. Enumerable.Range(0, payers).Select(index => Tx($"{name}-in-{index}", 900_000 + index)
                .In(Wpkh($"{name}-payer-{index}"), 100_000).Out(address, 90_000).Build()),
        ]);
        return address;
    }

    [Fact]
    public async Task Many_new_payers_lie_in_one_pile_on_the_in_side_and_stay_in_the_graph()
    {
        var address = Receiving("hub", GraphPile.MostLoose + 4);

        var graph = (await _harness.Expand(address)).Graph;

        var pile = Assert.Single(graph.Piles);
        var hub = graph.NodeOf(address)!;
        Assert.Equal(hub.Key, pile.Anchor);
        Assert.Equal(PileSide.In, pile.Side);
        Assert.Equal(GraphPile.MostLoose + 4, pile.Members.Count);
        Assert.Empty(pile.PickedOut);
        Assert.All(pile.Members, key => Assert.NotNull(graph.Node(key)));
        Assert.Equal("pile:in:" + hub.Key, pile.Key);
    }

    [Fact]
    public async Task A_few_new_payers_are_drawn_loose()
    {
        var address = Receiving("quiet", GraphPile.MostLoose);

        var graph = (await _harness.Expand(address)).Graph;

        Assert.Empty(graph.Piles);
    }

    [Fact]
    public async Task Many_payees_lie_in_a_pile_on_the_out_side()
    {
        var payer = Wpkh("payer");
        var builder = Tx("batch").In(payer, 20_000_000);
        for (var index = 0; index < GraphPile.MostLoose + 1; index++)
        {
            builder.Out(Wpkh($"payee-{index}"), 1_000_000);
        }

        _harness.Source.Add(builder.Build());

        var graph = (await _harness.Expand(payer)).Graph;

        var pile = Assert.Single(graph.Piles);
        Assert.Equal(PileSide.Out, pile.Side);
        Assert.Equal(GraphPile.MostLoose + 1, pile.Members.Count);
    }

    [Fact]
    public async Task An_actor_where_the_trail_ends_never_lies_in_a_pile()
    {
        var address = Receiving("seen", GraphPile.MostLoose + 3);
        _harness.Labels.Addresses[Wpkh("seen-payer-0")] = TrailEndReason.Exchange;

        var graph = (await _harness.Expand(address)).Graph;

        var pile = Assert.Single(graph.Piles);
        Assert.DoesNotContain(graph.NodeOf(Wpkh("seen-payer-0"))!.Key, pile.Members);
        Assert.Equal(GraphPile.MostLoose + 2, pile.Members.Count);
    }

    [Fact]
    public async Task A_pick_is_kept_in_the_case_and_can_be_put_back()
    {
        var address = Receiving("kept", GraphPile.MostLoose + 2);
        var graph = (await _harness.Expand(address)).Graph;
        var picked = graph.NodeOf(Wpkh("kept-payer-3"))!.Key;

        _harness.Service.PickFromPile(_harness.Case, [picked]);
        _harness.Reopen();
        var pile = Assert.Single((await _harness.Load()).Piles);

        Assert.Equal([picked], pile.PickedOut);
        Assert.DoesNotContain(picked, pile.Members);

        _harness.Service.ReturnToPile(_harness.Case, [picked]);
        var back = Assert.Single((await _harness.Load()).Piles);
        Assert.Empty(back.PickedOut);
        Assert.Contains(picked, back.Members);
    }

    [Fact]
    public async Task A_counterparty_already_in_the_graph_does_not_move_into_a_later_pile()
    {
        var first = Wpkh("first");
        var shared = Wpkh("shared-payer");
        var early = Tx("early").In(shared, 500_000).Out(first, 400_000).Build();
        _harness.Source.Add(early);
        await _harness.Expand(first);
        var address = Receiving("later", GraphPile.MostLoose + 1);
        _harness.Source.Add(Tx("shared-to-later", 900_100).In(shared, 300_000, "s2").Out(address, 200_000).Build());

        var graph = (await _harness.Expand(address)).Graph;

        var pile = Assert.Single(graph.Piles);
        Assert.DoesNotContain(graph.NodeOf(shared)!.Key, pile.Members);
    }

    [Fact]
    public async Task What_an_address_expansion_held_back_is_a_clue_with_the_next_lookup_also_after_reopening()
    {
        var address = Receiving("busy", 6);
        _harness.Source.TransactionCounts[address] = 6;
        await _harness.Expand(address, maximum: 2);

        _harness.Reopen();
        var graph = await _harness.Load();

        var clue = Assert.Single(graph.HeldBack);
        Assert.Equal(graph.NodeOf(address)!.Key, clue.Anchor);
        Assert.Equal(4, clue.Remaining);
        Assert.Equal([ExpansionRequest.ForAddress(address, 2 + HeldBackClue.NextPart)], clue.Next);
    }

    [Fact]
    public async Task The_clue_goes_when_its_next_lookup_brings_in_the_rest()
    {
        var address = Receiving("rest", 6);
        _harness.Source.TransactionCounts[address] = 6;
        var first = await _harness.Expand(address, maximum: 2);
        var clue = Assert.Single(first.Graph.HeldBack);

        var result = await _harness.Service.ExpandAsync(_harness.Case, clue.Next[0]);

        Assert.Equal(4, result.NewTransactions.Count);
        Assert.Empty(result.Graph.HeldBack);
    }

    [Fact]
    public async Task Without_a_count_the_clue_says_only_that_there_are_more()
    {
        var address = Receiving("unknown", 4);

        var graph = (await _harness.Expand(address, maximum: 2)).Graph;

        Assert.Null(Assert.Single(graph.HeldBack).Remaining);
    }
}
