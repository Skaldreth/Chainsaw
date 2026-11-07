using Chainsaw.Modules.Checks.Contracts;
using static Chainsaw.Modules.Checks.Tests.Synthetic;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>
/// The facit for the checks that show the clustering's findings: CoinJoin and peel chains, and the wallets it proposes,
/// with the real clustering over the synthetic patterns.
/// </summary>
public sealed class ObfuscationChecksTests
{
    [Fact]
    public void A_whirlpool_mix_is_a_check_with_an_area_around_the_transaction_and_its_payers()
    {
        using var harness = new Harness();
        var whirlpool = Patterns.Whirlpool();

        var check = Assert.Single(harness.Run([whirlpool]).Checks, c => c.Analysis.Name == CoinJoinCheck.Name);

        Assert.Equal(CheckKind.Check, check.Finding.Kind);
        Assert.Equal(AreaSort.Obfuscation, check.Finding.Area);
        Assert.Equal("CoinJoin, Whirlpool", check.Finding.Title);
        Assert.Equal(whirlpool.Txid.Value, check.Finding.Key);
        Assert.Equal([whirlpool.Txid], check.Finding.Transactions);
        Assert.Equal(5, check.Finding.Addresses.Count);
        Assert.Equal(900_000, Assert.Single(check.Finding.Basis).Block!.Height);
        Assert.Contains("coinjoin v1", check.Finding.Why, StringComparison.Ordinal);
        Assert.Contains("Spåret tar slut här", check.Finding.Why, StringComparison.Ordinal);
        Assert.Equal(0, check.Waiting);
    }

    [Fact]
    public void A_possible_coinjoin_says_so_and_that_the_trail_goes_on()
    {
        using var harness = new Harness();

        var check = Assert.Single(
            harness.Run([Patterns.PossibleCoinJoin()]).Checks, c => c.Analysis.Name == CoinJoinCheck.Name);

        Assert.Equal("Möjlig CoinJoin", check.Finding.Title);
        Assert.Contains("Spåret går vidare", check.Finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_peel_chain_is_one_check_over_all_its_hops()
    {
        using var harness = new Harness();
        var chain = Patterns.PeelChain(4);

        var check = Assert.Single(harness.Run(chain).Checks, c => c.Analysis.Name == PeelChainCheck.Name);

        Assert.Equal("Peel chain, 4 hopp", check.Finding.Title);
        Assert.Equal(chain[0].Txid.Value, check.Finding.Key);
        Assert.Equal(chain.Select(hop => hop.Txid), check.Finding.Basis.Select(basis => basis.Txid));
        Assert.Contains(Wpkh("peel-holder-0"), check.Finding.Addresses);
        Assert.Contains(Wpkh("peel-holder-4"), check.Finding.Addresses);
        Assert.Contains("inget slås ihop", check.Finding.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_hops_are_no_peel_chain()
    {
        using var harness = new Harness();

        Assert.DoesNotContain(
            harness.Run(Patterns.PeelChain(2)).Checks, c => c.Analysis.Name == PeelChainCheck.Name);
    }

    [Fact]
    public void A_proposal_the_clustering_did_not_merge_is_a_wallet_area()
    {
        using var harness = new Harness();

        var wallet = Assert.Single(
            harness.Run([Patterns.PossibleCoinJoin()]).Checks, c => c.Analysis.Name == WalletCheck.Name);

        Assert.Equal(AreaSort.Wallet, wallet.Finding.Area);
        Assert.Equal(CheckKind.Check, wallet.Finding.Kind);
        Assert.Contains(Wpkh("possible-1"), wallet.Finding.Addresses);
        Assert.Contains(Wpkh("possible-2"), wallet.Finding.Addresses);
    }

    [Fact]
    public void An_ordinary_payment_whose_inputs_are_merged_gives_no_area()
    {
        using var harness = new Harness();

        var run = harness.Run([Patterns.Consolidation()]);

        Assert.Empty(run.Checks);
        Assert.Equal(0, run.Sealed);
    }
}
