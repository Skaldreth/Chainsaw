using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using static Chainsaw.Modules.Tracing.Tests.Synthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// What an expansion held back (sketches EX1 and EX2): the address's history past the graph is read without adding
/// anything, chosen transactions come into the graph as part of the expansion, and the clue counts what is left.
/// </summary>
public sealed class HeldBackTests : IDisposable
{
    private readonly CaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    /// <summary>Gives an address that received once from each of a number of payers, the first newest.</summary>
    private BitcoinAddress Receiving(string name, int payers)
    {
        var address = Wpkh(name);
        _harness.Source.Add([
            .. Enumerable.Range(0, payers).Select(index => Tx($"{name}-in-{index}", 900_000 + index)
                .In(Wpkh($"{name}-payer-{index}"), 100_000).Out(address, 90_000).Build()),
        ]);
        _harness.Source.TransactionCounts[address] = payers;
        return address;
    }

    [Fact]
    public async Task The_history_past_the_graph_is_read_as_facts_without_adding_to_the_graph()
    {
        var address = Receiving("deep", 7);
        await _harness.Expand(address, maximum: 2);

        var list = await _harness.Service.ListHeldBackAsync(_harness.Case, address);

        Assert.True(list.IsComplete);
        Assert.Equal(5, list.Transactions.Count);
        var oldest = list.Transactions[^1];
        Assert.Equal(PileSide.In, oldest.Side);
        Assert.Equal(new SharedKernel.Satoshi(90_000), oldest.Amount);
        Assert.Equal(Wpkh("deep-payer-0"), oldest.Counterparty);
        Assert.Equal(1, oldest.Counterparties);
        Assert.NotNull(oldest.Time);
        Assert.Equal(2, (await _harness.Load()).Transactions.Count);
    }

    [Fact]
    public async Task A_payment_out_names_the_payee_that_took_the_most_and_what_left_the_address()
    {
        var payer = Wpkh("spender");
        _harness.Source.Add(
            Tx("pay", 900_010).In(payer, 1_000_000).Out(Wpkh("big"), 600_000).Out(Wpkh("small"), 100_000)
                .Out(payer, 290_000).Build(),
            Tx("newer", 900_011).In(Wpkh("funder"), 50_000).Out(payer, 40_000).Build());
        _harness.Source.TransactionCounts[payer] = 2;
        await _harness.Expand(payer, maximum: 1);

        var held = Assert.Single((await _harness.Service.ListHeldBackAsync(_harness.Case, payer)).Transactions);

        Assert.Equal(PileSide.Out, held.Side);
        Assert.Equal(new SharedKernel.Satoshi(700_000), held.Amount);
        Assert.Equal(Wpkh("big"), held.Counterparty);
        Assert.Equal(2, held.Counterparties);
    }

    [Fact]
    public async Task Chosen_transactions_come_into_the_graph_and_the_clue_counts_what_is_left()
    {
        var address = Receiving("chosen", 7);
        await _harness.Expand(address, maximum: 2);
        var list = await _harness.Service.ListHeldBackAsync(_harness.Case, address);
        var chosen = list.Transactions.Take(2).Select(held => held.Txid).ToList();

        var result = await _harness.Service.TakeHeldBackAsync(_harness.Case, address, chosen);

        Assert.Equal(ExpansionOutcome.Expanded, result.Outcome);
        Assert.Equal(chosen.ToHashSet(), result.NewTransactions.ToHashSet());
        _harness.Reopen();
        var clue = Assert.Single((await _harness.Load()).HeldBack);
        Assert.Equal(3, clue.Remaining);
        Assert.Equal(2, clue.Chosen);
        Assert.Equal(address, clue.Address);
        Assert.NotNull(clue.Oldest);
        Assert.Equal(3, (await _harness.Service.ListHeldBackAsync(_harness.Case, address)).Transactions.Count);
    }

    [Fact]
    public async Task A_transaction_that_does_not_touch_the_address_is_left_out()
    {
        var address = Receiving("strict", 4);
        _harness.Source.Add(Tx("elsewhere", 900_100).In(Wpkh("a"), 10_000).Out(Wpkh("b"), 9_000).Build());
        await _harness.Expand(address, maximum: 2);

        var result = await _harness.Service.TakeHeldBackAsync(_harness.Case, address, [Txid("elsewhere")]);

        Assert.Equal(ExpansionOutcome.NotExpanded, result.Outcome);
        Assert.Equal(2, (await _harness.Load()).Transactions.Count);
    }

    [Fact]
    public async Task A_transaction_expansion_held_back_by_one_spender_has_a_clue()
    {
        var payer = Wpkh("batcher");
        var batch = Tx("batch", 900_000).In(payer, 3_100_000)
            .Out(Wpkh("o-0"), 1_000_000).Out(Wpkh("o-1"), 1_000_000).Out(Wpkh("o-2"), 1_000_000).Build();
        _harness.Source.Add(batch);
        _harness.Source.Add([
            .. Enumerable.Range(0, 3).Select(index => Tx($"onward-{index}", 900_001 + index)
                .Spend(batch, index).Out(Wpkh($"next-{index}"), 990_000).Build()),
        ]);

        var result = await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForTransaction(batch.Txid) with { MaximumTransactions = 2 });

        var clue = Assert.Single(result.Graph.HeldBack);
        Assert.Equal(1, clue.Remaining);
        Assert.Null(clue.Address);
    }

    [Fact]
    public async Task The_history_is_read_over_several_pages_and_a_transaction_in_the_mempool_comes_first()
    {
        var address = Receiving("paged", 8);
        await _harness.Expand(address, maximum: 1);
        _harness.Source.Add(Tx("paged-pending", null).In(Wpkh("paged-late"), 70_000).Out(address, 60_000).Build());

        var list = await _harness.Service.ListHeldBackAsync(_harness.Case, address);

        Assert.True(list.IsComplete);
        Assert.Equal(8, list.Transactions.Count);
        Assert.Null(list.Transactions[0].Time);
        Assert.Equal(Txid("paged-pending"), list.Transactions[0].Txid);
        Assert.All(list.Transactions, held => Assert.Equal(address, held.Address));
    }

    [Fact]
    public async Task Offline_the_pages_come_from_the_case_and_a_missing_later_page_gives_a_partial_list()
    {
        var read = Receiving("offline-read", 7);
        await _harness.Expand(read, maximum: 1);
        var online = await _harness.Service.ListHeldBackAsync(_harness.Case, read);
        var partial = Receiving("offline-part", 7);
        await _harness.Expand(partial, maximum: 2);
        _harness.Source.Online = false;

        var offline = await _harness.Service.ListHeldBackAsync(_harness.Case, read);
        var cut = await _harness.Service.ListHeldBackAsync(_harness.Case, partial);

        Assert.Equal(online.Transactions.Select(held => held.Txid), offline.Transactions.Select(held => held.Txid));
        Assert.True(offline.IsComplete);
        Assert.False(cut.IsComplete);
        Assert.Single(cut.Transactions);
        Assert.Contains("Offline", cut.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_history_of_exactly_the_most_is_whole_and_one_more_is_said_to_be_longer()
    {
        var exactly = Receiving("exact", HeldBackList.Most);
        var longer = Receiving("longer", HeldBackList.Most + 1);
        await _harness.Expand(exactly, maximum: 1);
        await _harness.Expand(longer, maximum: 1);

        var whole = await _harness.Service.ListHeldBackAsync(_harness.Case, exactly);
        var cut = await _harness.Service.ListHeldBackAsync(_harness.Case, longer);

        Assert.True(whole.IsComplete);
        Assert.Null(whole.Problem);
        Assert.Equal(HeldBackList.Most - 1, whole.Transactions.Count);
        Assert.False(cut.IsComplete);
        Assert.Equal(HeldBackList.Most - 1, cut.Transactions.Count);
        Assert.Equal("Adressens historik är längre än 2000 tx; de nyaste 2000 lästes.", cut.Problem);
    }

    [Fact]
    public async Task On_a_busy_address_the_time_round_the_traced_arrival_is_read_too()
    {
        var busy = Receiving("busy", HeldBackList.Most + 60);
        await _harness.Expand(busy, maximum: 1);
        var arrival = _harness.Source.Transaction(Txid("busy-in-20"));
        await _harness.Expand(_harness.Source.Transaction(Txid("busy-in-30")));

        var list = await _harness.Service.ListHeldBackAsync(_harness.Case, busy, arrival.Block!.Time);

        Assert.False(list.IsComplete);
        Assert.Contains(list.Transactions, held => held.Txid == arrival.Txid);
        Assert.EndsWith("Tiden runt den spårade ankomsten lästes också.", list.Problem, StringComparison.Ordinal);
        Assert.True(list.Transactions.Zip(list.Transactions.Skip(1)).All(pair => pair.First.Time >= pair.Second.Time));
    }

    [Fact]
    public async Task A_clusters_clue_counts_and_reads_every_expanded_address_and_spenders_get_a_clue_of_their_own()
    {
        var first = Receiving("pair-a", 5);
        var second = Receiving("pair-b", 6);
        var join = Tx("pair-join", 950_000).In(first, 50_000).In(second, 50_000)
            .Out(Wpkh("pair-o-0"), 40_000).Out(Wpkh("pair-o-1"), 30_000).Out(Wpkh("pair-o-2"), 20_000).Build();
        _harness.Source.Add(join);
        _harness.Source.Add([
            .. Enumerable.Range(0, 3).Select(index => Tx($"pair-onward-{index}", 950_001 + index)
                .Spend(join, index).Out(Wpkh($"pair-next-{index}"), 10_000).Build()),
        ]);
        _harness.Source.TransactionCounts[first] = 6;
        _harness.Source.TransactionCounts[second] = 7;
        await _harness.Expand(first, maximum: 2);
        await _harness.Expand(second, maximum: 2);

        var result = await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForTransaction(join.Txid) with { MaximumTransactions = 2 });

        var clues = result.Graph.HeldBack;
        var history = Assert.Single(clues, clue => clue.Addresses.Count > 0);
        Assert.Equal([first, second], history.Addresses);
        Assert.Equal(4 + 5, history.Remaining);
        var reachable = 0;
        foreach (var address in history.Addresses)
        {
            reachable += (await _harness.Service.ListHeldBackAsync(_harness.Case, address)).Transactions.Count;
        }

        Assert.Equal(history.Remaining, reachable);
        var spends = Assert.Single(clues, clue => clue.Addresses.Count == 0);
        Assert.Equal(1, spends.Remaining);
        Assert.StartsWith(HeldBackClue.SpendsKeyPrefix, spends.Key, StringComparison.Ordinal);
        Assert.Equal(history.Anchor, spends.Anchor);
    }

    [Fact]
    public async Task Picking_without_an_earlier_expansion_leaves_the_address_not_whole()
    {
        var address = Receiving("unexpanded", 3);

        await _harness.Service.TakeHeldBackAsync(_harness.Case, address, [Txid("unexpanded-in-0")]);

        var expansion = Assert.Single(_harness.Case.Read(GraphStateStore.Read).Expansions);
        Assert.False(expansion.IsComplete);
    }

    [Fact]
    public async Task Expanding_again_keeps_the_first_time_so_the_order_of_the_expansions_holds()
    {
        var first = Receiving("one", 2);
        var second = Receiving("two", 2);
        await _harness.Expand(first, maximum: 1);
        _harness.Clock.UtcNow += TimeSpan.FromMinutes(1);
        await _harness.Expand(second);
        _harness.Clock.UtcNow += TimeSpan.FromMinutes(1);
        await _harness.Expand(first);

        var clue = (await _harness.Load()).HeldBack;

        Assert.Empty(clue);
        var expansions = _harness.Case.Read(GraphStateStore.Read).Expansions;
        Assert.Equal([first, second], expansions.Select(expansion => expansion.Address));
    }
}
