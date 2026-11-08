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
