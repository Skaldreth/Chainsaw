using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using static Chainsaw.Modules.Clustering.Tests.Synthetic;

namespace Chainsaw.Modules.Clustering.Tests;

/// <summary>The user's splits: they are sealed, they hold, and other heuristics may only propose.</summary>
public sealed class SplitTests : IDisposable
{
    private readonly CaseHarness _harness = new();

    public SplitTests() => _harness.Analyse(Patterns.PaymentWithChange());

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void A_split_takes_the_change_out_of_the_cluster()
    {
        var split = Split();

        var cluster = _harness.ClusterOf(Wpkh("payer-1"));

        AssertMembers(cluster, "payer-1", "payer-2");
        var refused = Assert.Single(cluster.Refused);
        Assert.Equal(LinkState.RefusedBySplit, refused.State);
        Assert.Equal(split, refused.Split);
        Assert.Equal(split, Assert.Single(cluster.Splits));
        Assert.Single(_harness.ClusterOf(Wpkh("payer-change")).Members);
    }

    [Fact]
    public void A_split_is_journalled_and_sealed()
    {
        var split = Split();

        var entry = _harness.Case.ReadJournal().Last();
        Assert.Equal(ClusteringJournalKinds.Split, entry.Kind);
        Assert.Equal(entry.Sequence, split.JournalSequence);
        Assert.Equal(entry.Utc, split.RecordedUtc);
        Assert.Contains("Testa Testsson", entry.Operator.Name, StringComparison.Ordinal);
        Assert.Equal(1, _harness.Count("clustering_splits"));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_split_holds_when_the_same_transactions_are_analysed_again()
    {
        Split();

        var run = _harness.Analyse(Patterns.PaymentWithChange());

        Assert.Null(run.Journal);
        AssertMembers(_harness.ClusterOf(Wpkh("payer-1")), "payer-1", "payer-2");
    }

    [Fact]
    public void The_same_heuristic_never_merges_them_again_from_another_transaction()
    {
        Split();

        // Earlier than the payment, so that the change address is fresh in both and nothing points elsewhere.
        var again = Tx("again", 899_999)
            .In(Wpkh("payer-1"), 4_000_000)
            .In(Wpkh("payer-2"), 3_500_000)
            .Out(Pkh("shop-2"), 5_000_000)
            .Out(Wpkh("payer-change"), 990_000)
            .Build();

        _harness.Analyse(Patterns.PaymentWithChange(), again);

        var cluster = _harness.ClusterOf(Wpkh("payer-1"));
        AssertMembers(cluster, "payer-1", "payer-2");
        var refused = Assert.Single(cluster.Refused, link => link.Evidence.Txid == Txid("again"));
        Assert.Equal(LinkState.RefusedBySplit, refused.State);
        Assert.Equal(EvidenceStrength.Merge, refused.Evidence.Strength);
        Assert.Equal(HeuristicNames.ChangeAddress, refused.Evidence.Heuristic.Name);
    }

    [Fact]
    public void Another_heuristic_may_only_propose_and_it_shows()
    {
        Split();
        var together = Tx("together", 900_002)
            .In(Wpkh("payer-2"), 1_000_000)
            .In(Wpkh("payer-change"), 487_650)
            .Out(Pkh("shop-3"), 1_400_000)
            .Build();

        _harness.Analyse(Patterns.PaymentWithChange(), together);

        var cluster = _harness.ClusterOf(Wpkh("payer-1"));
        AssertMembers(cluster, "payer-1", "payer-2");
        var suggestion = Assert.Single(cluster.Suggestions);
        Assert.Equal(LinkState.SuggestedAfterSplit, suggestion.State);
        Assert.Equal(HeuristicNames.CommonInputOwnership, suggestion.Evidence.Heuristic.Name);
        Assert.NotNull(suggestion.Split);
    }

    [Fact]
    public void Analysing_again_with_new_results_never_changes_which_link_gives_way_to_a_split()
    {
        _harness.Service.Split(_harness.Case, new SplitRequest(
            Wpkh("payer-2"), Wpkh("payer-change"), HeuristicNames.CommonInputOwnership, "Två personer."));
        var before = _harness.ClusterOf(Wpkh("payer-1")).Addresses.Select(address => address.Value).Order().ToList();

        // A known earlier use of the shop's address adds a sign, so the change result is sealed again, later.
        var earlier = Tx("shop-earlier", 899_000).In(Wpkh("someone"), 9_000_000).Out(Pkh("shop"), 8_000_000).Build();
        var run = _harness.Analyse(earlier, Patterns.PaymentWithChange());

        Assert.True(run.NewResults > 0);
        Assert.Equal(before, _harness.ClusterOf(Wpkh("payer-1")).Addresses.Select(address => address.Value).Order());
    }

    [Fact]
    public void A_split_holds_after_the_case_is_closed_and_opened()
    {
        Split();

        _harness.Reopen();

        AssertMembers(_harness.ClusterOf(Wpkh("payer-1")), "payer-1", "payer-2");
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_split_made_before_the_analysis_holds_as_well()
    {
        using var fresh = new CaseHarness();
        fresh.Service.Split(fresh.Case, new SplitRequest(
            Wpkh("payer-1"), Wpkh("payer-2"), HeuristicNames.CommonInputOwnership, "Kända som två personer."));

        fresh.Analyse(Patterns.PaymentWithChange());

        var cluster = fresh.ClusterOf(Wpkh("payer-1"));
        AssertMembers(cluster, "payer-1", "payer-change");
        Assert.Equal(LinkState.RefusedBySplit, Assert.Single(cluster.Refused).State);
    }

    [Fact]
    public void A_split_needs_two_addresses_a_heuristic_and_a_reason()
    {
        var service = _harness.Service;
        var @case = _harness.Case;

        Assert.Throws<ArgumentException>(() => service.Split(@case, new SplitRequest(
            Wpkh("payer-1"), Wpkh("payer-1"), HeuristicNames.ChangeAddress, "Skäl")));
        Assert.Throws<ArgumentException>(() => service.Split(@case, new SplitRequest(
            Wpkh("payer-1"), Wpkh("payer-change"), "Not A Name", "Skäl")));
        Assert.Throws<ArgumentException>(() => service.Split(@case, new SplitRequest(
            Wpkh("payer-1"), Wpkh("payer-change"), "change-adress", "Skäl")));
        Assert.Throws<ArgumentException>(() => service.Split(@case, new SplitRequest(
            Wpkh("payer-1"), Wpkh("payer-change"), HeuristicNames.ChangeAddress, " ")));
        Assert.Throws<ArgumentException>(() => service.Split(@case, new SplitRequest(
            Wpkh("payer-1"), Wpkh("payer-change"), HeuristicNames.ChangeAddress, new string('x', 1_001))));
        Assert.Equal(0, _harness.Count("clustering_splits"));
    }

    [Fact]
    public void A_split_without_an_operator_profile_is_refused()
    {
        _harness.Profiles.Profile = null;

        var error = Assert.Throws<CaseException>(() => Split());

        Assert.Equal(CaseError.NoOperatorProfile, error.Error);
        Assert.Equal(0, _harness.Count("clustering_splits"));
    }

    private ClusterSplit Split() => _harness.Service.Split(_harness.Case, new SplitRequest(
        Wpkh("payer-1"), Wpkh("payer-change"), HeuristicNames.ChangeAddress, "Växeln är en annan persons."));

    private static void AssertMembers(Cluster cluster, params string[] labels) =>
        Assert.Equal(
            labels.Select(label => Wpkh(label).Value).Order(),
            cluster.Addresses.Select(address => address.Value).Order());
}
