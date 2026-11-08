using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Tracing.Tests.Synthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// How a transaction is read from an address's history (sketch EX1): the side, what moved and every counterparty; an
/// amount that is the address's share when others paid in too is marked uncertain (principle 2).
/// </summary>
public sealed class HeldBackReaderTests
{
    private static readonly BitcoinAddress Own = Wpkh("reader-own");

    [Fact]
    public void An_address_on_both_sides_with_change_pays_what_left_it_and_names_every_payee()
    {
        var tx = Tx("both").In(Own, 1_000_000).Out(Wpkh("big"), 600_000).Out(Own, 300_000)
            .Out(Wpkh("small"), 90_000).Build();

        var held = HeldBackReader.Read(tx, Own)!;

        Assert.Equal(PileSide.Out, held.Side);
        Assert.Equal(new Satoshi(690_000), held.Amount);
        Assert.Equal([Wpkh("big"), Wpkh("small")], held.Parties);
        Assert.Equal(Wpkh("big"), held.Counterparty);
        Assert.Equal(2, held.Counterparties);
        Assert.False(held.IsShared);
        Assert.Equal(new Satoshi(10_000), held.Fee);
        Assert.Equal(Own, held.Address);
    }

    [Fact]
    public void Several_inputs_of_the_address_alone_are_a_fact()
    {
        var tx = Tx("own-inputs").In(Own, 400_000).In(Own, 500_000).Out(Wpkh("payee"), 880_000).Build();

        var held = HeldBackReader.Read(tx, Own)!;

        Assert.Equal(new Satoshi(880_000), held.Amount);
        Assert.False(held.IsShared);
    }

    [Fact]
    public void In_a_coinjoin_the_address_pays_its_own_input_less_its_change_not_everyones_coins()
    {
        var tx = Tx("coinjoin").In(Own, 1_000_000).In(Wpkh("other-1"), 5_000_000_000).In(Wpkh("other-2"), 2_000_000)
            .Out(Wpkh("mix-1"), 4_000_000_000).Out(Wpkh("mix-2"), 1_000_000_000).Out(Own, 200_000)
            .Out(Wpkh("mix-3"), 2_700_000).Build();

        var held = HeldBackReader.Read(tx, Own)!;

        Assert.Equal(PileSide.Out, held.Side);
        Assert.Equal(new Satoshi(800_000), held.Amount);
        Assert.True(held.IsShared);
        Assert.Equal(3, held.Counterparties);
    }

    [Fact]
    public void Getting_back_more_than_it_put_in_with_others_is_a_payment_in_and_uncertain()
    {
        var tx = Tx("net-in").In(Own, 100_000).In(Wpkh("payer"), 900_000).Out(Own, 700_000)
            .Out(Wpkh("payer"), 290_000).Build();

        var held = HeldBackReader.Read(tx, Own)!;

        Assert.Equal(PileSide.In, held.Side);
        Assert.Equal(new Satoshi(600_000), held.Amount);
        Assert.True(held.IsShared);
        Assert.Equal([Wpkh("payer")], held.Parties);
    }

    [Fact]
    public void Without_the_inputs_amounts_the_share_is_by_how_many_inputs_are_the_addresss()
    {
        var tx = WithInputs(
            "unknown-amounts",
            [Input(0, Own, null), Input(1, Wpkh("x"), null), Input(2, Wpkh("y"), null), Input(3, Wpkh("z"), null)],
            [Output(0, Wpkh("to"), 4_000_000)]);

        var held = HeldBackReader.Read(tx, Own)!;

        Assert.Equal(new Satoshi(1_000_000), held.Amount);
        Assert.True(held.IsShared);
    }

    [Fact]
    public void A_payment_in_with_inputs_without_amounts_names_its_payers()
    {
        var tx = WithInputs(
            "in-unknown",
            [Input(0, Wpkh("p-1"), null), Input(1, Wpkh("p-2"), 30_000)],
            [Output(0, Own, 25_000)]);

        var held = HeldBackReader.Read(tx, Own)!;

        Assert.Equal(PileSide.In, held.Side);
        Assert.Equal(new Satoshi(25_000), held.Amount);
        Assert.Equal([Wpkh("p-2"), Wpkh("p-1")], held.Parties);
        Assert.False(held.IsShared);
    }

    [Fact]
    public void A_coinbase_pays_the_address_with_no_counterparty()
    {
        var coinbase = new BitcoinInput(0, true, null, null, null, null, null, 0xFFFFFFFF);
        var tx = WithInputs("coinbase", [coinbase], [Output(0, Own, 312_500_000)]);

        var held = HeldBackReader.Read(tx, Own)!;

        Assert.Equal(PileSide.In, held.Side);
        Assert.Equal(new Satoshi(312_500_000), held.Amount);
        Assert.Null(held.Counterparty);
        Assert.Empty(held.Parties);
    }

    [Fact]
    public void A_consolidation_to_itself_is_said_with_what_came_back_not_as_nothing()
    {
        var tx = Tx("consolidate").In(Own, 300_000).In(Own, 200_000).Out(Own, 495_000).Build();

        var held = HeldBackReader.Read(tx, Own)!;

        Assert.True(held.IsSelf);
        Assert.Equal(new Satoshi(495_000), held.Amount);
        Assert.Null(held.Counterparty);
    }

    [Fact]
    public void A_transaction_without_the_address_is_not_read()
    {
        var tx = Tx("elsewhere").In(Wpkh("a"), 10_000).Out(Wpkh("b"), 9_000).Build();

        Assert.Null(HeldBackReader.Read(tx, Own));
    }

    private static BitcoinInput Input(int index, BitcoinAddress address, long? satoshi) => new(
        index, false, Txid($"funding-{index}-{address.Value}"), 0, address, address.Type,
        satoshi is { } value ? new Satoshi(value) : null, 0xFFFFFFFD);

    private static BitcoinOutput Output(int index, BitcoinAddress address, long satoshi) =>
        new(index, address.Type, address, new Satoshi(satoshi), "", null);

    private static BitcoinTransaction WithInputs(
        string label, IReadOnlyList<BitcoinInput> inputs, IReadOnlyList<BitcoinOutput> outputs) =>
        new(Txid(label), Block(900_000), inputs, outputs, Satoshi.Zero, 250, 1000, 2, 0);
}
