using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Microsoft.Extensions.DependencyInjection;
using static Chainsaw.Modules.Clustering.Tests.Synthetic;

namespace Chainsaw.Modules.Clustering.Tests;

/// <summary>How the conclusions are stored: sealed, journalled, once, and never changed.</summary>
public sealed class StorageTests : IDisposable
{
    private readonly CaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void A_run_is_one_journal_entry_and_sealed_records_that_point_to_it()
    {
        var run = _harness.Analyse(Patterns.PaymentWithChange(), Patterns.Whirlpool());

        var entry = Assert.IsType<JournalEntry>(run.Journal);
        Assert.Equal(ClusteringJournalKinds.Analysed, entry.Kind);
        Assert.Equal(entry, _harness.Case.ReadJournal().Last());
        Assert.Contains("2 tx", entry.Summary, StringComparison.Ordinal);
        Assert.Equal((2, 2, 1), (run.TransactionsAnalysed, run.Merges, run.CoinJoins.Count));
        var conclusions = _harness.Service.GetConclusions(_harness.Case, Txid("payment"));
        Assert.All(conclusions.Evidence, evidence => Assert.Equal(entry.Sequence, evidence.JournalSequence));
        Assert.All(conclusions.Evidence, evidence => Assert.Equal(entry.Utc, evidence.RecordedUtc));
        Assert.True(_harness.Verify().IsIntact);
        Assert.Contains(_harness.Verify().Chains, chain => chain.Chain == "clustering.evidence");
    }

    [Fact]
    public void An_unchanged_result_is_not_stored_again()
    {
        _harness.Analyse(Patterns.PaymentWithChange());
        var records = _harness.Count("clustering_evidence");
        var entries = _harness.Case.ReadJournal().Count;

        var run = _harness.Analyse(Patterns.PaymentWithChange());

        Assert.Null(run.Journal);
        Assert.Equal(0, run.NewResults);
        Assert.Equal(records, _harness.Count("clustering_evidence"));
        Assert.Equal(entries, _harness.Case.ReadJournal().Count);
    }

    [Fact]
    public void A_newer_result_replaces_the_older_only_by_being_newer()
    {
        var chain = Patterns.PeelChain(3);
        _harness.Analyse(chain[0]);
        var before = _harness.Count("clustering_evidence");

        _harness.Analyse([.. chain]);

        Assert.True(_harness.Count("clustering_evidence") > before);
        var peel = _harness.Service.GetConclusions(_harness.Case, chain[0].Txid).Findings
            .Single(found => found.Heuristic.Name == HeuristicNames.PeelChain);
        Assert.Equal(3, Assert.IsType<PeelChainFinding>(peel.Finding).ChainLength);
    }

    [Fact]
    public void A_result_that_finds_nothing_any_more_replaces_one_that_did()
    {
        var chain = Patterns.PeelChain(3);
        _harness.Analyse([.. chain]);

        _harness.Analyse(chain[0]);

        Assert.DoesNotContain(
            _harness.Service.GetConclusions(_harness.Case, chain[0].Txid).Findings,
            found => found.Finding is PeelChainFinding);
    }

    [Theory]
    [InlineData("UPDATE clustering_evidence SET reason = 'ändrad'")]
    [InlineData("DELETE FROM clustering_evidence")]
    [InlineData("UPDATE clustering_splits SET heuristic = 'peel-chain'")]
    public void Sealed_records_cannot_be_changed(string sql)
    {
        _harness.Analyse(Patterns.PaymentWithChange());
        _harness.Service.Split(_harness.Case, new SplitRequest(
            Wpkh("payer-1"), Wpkh("payer-change"), HeuristicNames.ChangeAddress, "Skäl"));

        Assert.ThrowsAny<Exception>(() => _harness.Case.Write(transaction =>
        {
            using var command = transaction.CreateCommand(sql);
            return command.ExecuteNonQuery();
        }));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void Nothing_new_is_stored_without_an_operator_profile()
    {
        _harness.Profiles.Profile = null;

        var error = Assert.Throws<CaseException>(() => _harness.Analyse(Patterns.PaymentWithChange()));

        Assert.Equal(CaseError.NoOperatorProfile, error.Error);
        Assert.Equal(0, _harness.Count("clustering_evidence"));
    }

    [Fact]
    public void A_failing_analysis_stores_nothing_about_the_transaction()
    {
        using var harness = new CaseHarness(services =>
            services.AddSingleton<IClusteringHeuristic, FailingHeuristic>());

        var run = harness.Analyse(Patterns.Whirlpool());

        Assert.Equal([Txid("whirlpool")], run.Failed);
        Assert.Null(run.Journal);
        Assert.Equal(0, harness.Count("clustering_evidence"));
        Assert.Single(harness.ClusterOf(Wpkh("whirl-in-1")).Members);
    }

    [Fact]
    public void An_unknown_address_is_a_cluster_of_its_own()
    {
        var cluster = _harness.ClusterOf(Wpkh("never-seen"));

        var member = Assert.Single(cluster.Members);
        Assert.Equal(Wpkh("never-seen"), member.Address);
        Assert.Null(member.JoinedBy);
        Assert.Empty(cluster.Links);
    }

    [Fact]
    public void A_transaction_that_was_never_analysed_has_no_conclusions()
    {
        var conclusions = _harness.Service.GetConclusions(_harness.Case, Txid("unknown"));

        Assert.Empty(conclusions.Evidence);
        Assert.Empty(conclusions.Findings);
        Assert.Null(conclusions.TrailEnd);
    }
}
