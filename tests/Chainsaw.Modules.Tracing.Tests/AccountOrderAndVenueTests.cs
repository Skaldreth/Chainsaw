using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Tracing.Tests.EthSynthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// Two Ethereum transactions in one block are applied in their place in the block when an answer gives it, also to the
/// movements of the same transaction from an answer that does not; and a swap names the known exchange it went
/// through, never a contract that is not one.
/// </summary>
public sealed class AccountOrderAndVenueTests
{
    private static readonly EthereumAddress Owner = Address("owner");

    private static readonly EthereumAddress Payer = Address("payer");

    private static readonly EthereumAddress Shop = Address("shop");

    private static readonly EthereumAddress Pool = Address("pool");

    [Fact]
    public void Transactions_in_one_block_are_ordered_by_their_place_not_by_hash()
    {
        // The places go against the hashes' order, so that only the places can put the money in first.
        var first = Transfer("in", Payer, Owner, 1m);
        var second = Transfer("out", Owner, Shop, 1m);
        var hashFirst = string.CompareOrdinal(first.Transaction.Value, second.Transaction.Value) < 0;
        var incoming = Placed(first, hashFirst ? 9 : 3);
        var outgoing = Placed(second, hashFirst ? 3 : 9);
        var expected = hashFirst
            ? new[] { outgoing.Transaction, incoming.Transaction }
            : [incoming.Transaction, outgoing.Transaction];

        var data = Combine(incoming, outgoing);

        Assert.Equal(expected, data.Transfers.Select(t => t.Transaction));
        Assert.NotEqual(
            expected, Combine(Unplaced(incoming), Unplaced(outgoing)).Transfers.Select(t => t.Transaction));
    }

    [Fact]
    public void A_movement_without_a_place_takes_it_from_another_answer_about_its_transaction()
    {
        var native = Placed(Transfer("tx", Payer, Owner, 1m), 4);
        var internalCall = new EthereumTransfer(
            native.Transaction, EthereumTransferKind.Internal, Owner, Shop, Ether(0.5m), 2, Block(900));

        var data = Combine(native, internalCall);

        Assert.All(data.Transfers, transfer => Assert.Equal(4, transfer.Block!.TransactionIndex));
    }

    [Fact]
    public void A_swap_through_a_known_router_names_it_and_one_through_an_unknown_contract_does_not()
    {
        var router = KnownContracts.All.First(contract => contract.Category == KnownContractCategory.Dex);
        var throughRouter = Tx("swap-known", 900, Owner, 0.001m,
            Token(Owner, router.Address, Usdt(100m)), (EthereumTransferKind.Internal, router.Address, Owner, Ether(1m)));
        var throughPool = Tx("swap-unknown", 901, Owner, 0.001m,
            Token(Owner, Pool, Usdt(100m)), (EthereumTransferKind.Internal, Pool, Owner, Ether(1m)));

        var swaps = Whole(throughRouter, throughPool).Swaps();

        Assert.Equal(2, swaps.Count);
        Assert.Same(router, swaps.Single(swap => swap.Transaction == throughRouter.Hash).Venue);
        Assert.Null(swaps.Single(swap => swap.Transaction == throughPool.Hash).Venue);
    }

    [Fact]
    public void A_mixer_or_a_bridge_on_the_other_side_is_never_a_swap()
    {
        // The trail must reach them (SW1a, the second review): a transaction with one is never read as a swap.
        foreach (var category in new[] { KnownContractCategory.Mixer, KnownContractCategory.Bridge })
        {
            var other = KnownContracts.All.First(contract => contract.Category == category);
            var swap = Tx("swap-" + category, 900, Owner, 0.001m,
                Token(Owner, other.Address, Usdt(100m)),
                (EthereumTransferKind.Internal, other.Address, Owner, Ether(1m)));

            Assert.Empty(Whole(swap).Swaps());
        }
    }

    [Fact]
    public void An_address_whose_label_ends_the_trail_is_never_a_swaps_other_side()
    {
        var pool = Address("labelled-exchange");
        var swap = Tx("swap-exchange", 900, Owner, 0.001m,
            Token(Owner, pool, Usdt(100m)), (EthereumTransferKind.Internal, pool, Owner, Ether(1m)));

        Assert.Single(Whole(swap).Swaps());
        var (swaps, refused) = Whole(swap).ReadSwaps(address => address == pool ? "Börs (etikett)" : null);
        Assert.Empty(swaps);
        var refusal = Assert.Single(refused);
        Assert.Equal((pool.ToChainAddress(), "Börs (etikett)"), (refusal.Counterparty, refusal.Why));
    }

    [Fact]
    public void Dust_of_an_unknown_token_back_never_makes_a_payment_a_swap()
    {
        // A deliberate obfuscation: 1 unit of a made-up token from the recipient back in the same transaction.
        var fake = Asset.Token(SharedKernel.Chain.Ethereum, "0x1111111111111111111111111111111111111111", "FAKE", 18);
        var payment = Tx("dust", 900, Owner, 0.001m,
            Token(Owner, Shop, Usdt(1000m)), Token(Shop, Owner, new AssetAmount(fake, 1)));

        Assert.Empty(Whole(payment).Swaps());
    }

    private static EthereumTransfer Transfer(string label, EthereumAddress from, EthereumAddress to, decimal ether) =>
        new(Hash(label), EthereumTransferKind.Native, from, to, Ether(ether), 0, Block(900));

    private static EthereumTransfer Placed(EthereumTransfer transfer, long place) =>
        transfer with { Block = transfer.Block! with { TransactionIndex = place } };

    private static EthereumTransfer Unplaced(EthereumTransfer transfer) =>
        transfer with { Block = transfer.Block! with { TransactionIndex = null } };

    private static AccountData Combine(params EthereumTransfer[] history) => AccountData.Combine(
        new Dictionary<EthereumTxHash, EthereumTransaction>(), history, [], new HashSet<ChainAddress>(),
        new HashSet<ChainAddress>());

    private static AccountData Whole(params EthereumTransaction[] transactions) => AccountData.Combine(
        transactions.ToDictionary(transaction => transaction.Hash), [], [], new HashSet<ChainAddress>(),
        new HashSet<ChainAddress>());
}
