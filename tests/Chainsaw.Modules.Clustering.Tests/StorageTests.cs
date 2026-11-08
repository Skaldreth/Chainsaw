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
    public void A_narrower_run_never_takes_back_what_a_wider_one_concluded()
    {
        var chain = Patterns.PeelChain(3);
        _harness.Analyse([.. chain]);
        var before = _harness.Count("clustering_evidence");

        var run = _harness.Analyse(chain[1]);

        Assert.Equal([chain[1].Txid], run.HeldBack);
        Assert.Null(run.Journal);
        Assert.Equal(before, _harness.Count("clustering_evidence"));
        Assert.Contains(
            _harness.Service.GetConclusions(_harness.Case, chain[1].Txid).Findings,
            found => found.Finding is PeelChainFinding);
    }

    [Fact]
    public void A_narrower_run_never_takes_back_a_disagreement_and_restores_a_merge()
    {
        var seen = Tx("seen-earlier", 800_000).In(Wpkh("elsewhere"), 9_000_000).Out(Wpkh("bulk"), 8_000_000).Build();
        var payment = Tx("disagree", 900_000)
            .In(Wpkh("payer-x"), 10_000_000)
            .Out(Wpkh("payer-x"), 1_234_567)
            .Out(Wpkh("bulk"), 8_700_000)
            .Build();
        _harness.Analyse(seen, payment);
        var wide = _harness.Service.GetConclusions(_harness.Case, Txid("disagree"));
        Assert.Contains(Txid("seen-earlier").Value,
            wide.Findings.Select(found => found.Finding).OfType<ChangeFinding>().Single().SeenIn);

        var run = _harness.Analyse(payment);

        Assert.Equal([Txid("disagree")], run.HeldBack);
        var after = _harness.Service.GetConclusions(_harness.Case, Txid("disagree"));
        Assert.Equal(wide.Findings.Select(found => found.Sequence), after.Findings.Select(found => found.Sequence));
        Assert.Equal(wide.Evidence.Select(found => found.Sequence), after.Evidence.Select(found => found.Sequence));
    }

    [Fact]
    public void A_result_that_concludes_nothing_is_not_stored_the_first_time()
    {
        var run = _harness.Analyse(Patterns.Consolidation());

        Assert.Equal(1, run.NewResults);
        Assert.Equal(1, _harness.Count("clustering_evidence"));
    }

    [Fact]
    public void A_newer_version_replaces_an_older_versions_results_which_stay_sealed()
    {
        using var harness = new CaseHarness(services =>
            services.AddSingleton<IClusteringHeuristic, SilentCoinJoinNext>());
        harness.Analyse(Patterns.Whirlpool());
        var conclusions = harness.Service.GetConclusions(harness.Case, Txid("whirlpool"));

        Assert.Null(conclusions.TrailEnd);
        var coinJoin = Assert.Single(conclusions.Findings, found => found.Heuristic.Name == HeuristicNames.CoinJoin);
        Assert.Equal(3, coinJoin.Heuristic.Version);
        Assert.Equal(1L, harness.Case.Read(reader =>
        {
            using var command = reader.CreateCommand(
                "SELECT COUNT(*) FROM clustering_evidence WHERE heuristic = 'coinjoin' AND heuristic_version = 2");
            return (long)command.ExecuteScalar()!;
        }));
    }

    [Fact]
    public void A_newer_version_that_concludes_nothing_takes_back_an_older_versions_merge()
    {
        _harness.Analyse(Patterns.Consolidation());
        Assert.Equal(6, _harness.ClusterOf(Wpkh("cons-1")).Members.Count);

        var upgraded = new ClusteringService([new SilentCommonInputV2()]);
        var run = upgraded.Analyse(_harness.Case, [Patterns.Consolidation()]);

        Assert.Equal(1, run.NewResults);
        Assert.Single(_harness.ClusterOf(Wpkh("cons-1")).Members);
        Assert.Null(upgraded.Analyse(_harness.Case, [Patterns.Consolidation()]).Journal);
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
        Assert.Contains(Txid("whirlpool").Value, run.Journal!.Details!,
            StringComparison.Ordinal);
        Assert.Equal(0, harness.Count("clustering_evidence"));
        Assert.Single(harness.ClusterOf(Wpkh("whirl-in-1")).Members);
    }

    [Fact]
    public void A_malformed_proposal_is_never_sealed_and_the_clusters_still_work()
    {
        using var harness = new CaseHarness(services =>
            services.AddSingleton<IClusteringHeuristic, MalformedHeuristic>());

        var run = harness.Analyse(Patterns.PaymentWithChange());

        Assert.Equal([Txid("payment")], run.Failed);
        Assert.Equal(0, harness.Count("clustering_evidence"));
        Assert.Single(harness.ClusterOf(Wpkh("payer-1")).Members);
    }

    [Fact]
    public void A_new_kind_of_finding_fails_its_transaction_and_not_the_run()
    {
        using var harness = new CaseHarness(services =>
            services.AddSingleton<IClusteringHeuristic, UndeclaredFindingHeuristic>());

        var run = harness.Analyse(Patterns.PaymentWithChange(), Patterns.Consolidation());

        Assert.Equal(2, run.Failed.Count);
        Assert.Equal(0, harness.Count("clustering_evidence"));
    }

    [Fact]
    public void Findings_are_sealed_with_their_enums_by_name()
    {
        _harness.Analyse(Patterns.Whirlpool());

        var finding = _harness.Case.Read(reader =>
        {
            using var command = reader.CreateCommand(
                "SELECT finding FROM clustering_evidence WHERE record_kind = 'finding'");
            return (string)command.ExecuteScalar()!;
        });

        Assert.Contains("\"Whirlpool\"", finding, StringComparison.Ordinal);
        Assert.Contains("\"Recognised\"", finding, StringComparison.Ordinal);
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
