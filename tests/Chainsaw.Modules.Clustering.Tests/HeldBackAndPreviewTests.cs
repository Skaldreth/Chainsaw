using Microsoft.Extensions.DependencyInjection;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using static Chainsaw.Modules.Clustering.Tests.Synthetic;

namespace Chainsaw.Modules.Clustering.Tests;

/// <summary>
/// The facit for the surfaces of round four: the preview of drawing a version back (sketch KV1a) and the conclusions a
/// run holds back with the user's decisions about them (sketch HB1a).
/// </summary>
public sealed class HeldBackAndPreviewTests : IDisposable
{
    /// <summary>The case under test.</summary>
    private readonly CaseHarness _harness = new();

    /// <inheritdoc/>
    public void Dispose() => _harness.Dispose();

    [Fact]
    public void The_preview_of_drawing_a_version_back_says_what_leaves_and_stores_nothing()
    {
        _harness.Analyse(Patterns.PaymentWithChange());
        var change = new HeuristicId(HeuristicNames.ChangeAddress, 2);
        var journal = _harness.Case.ReadJournal().Count;

        var preview = _harness.Service.PreviewWithdraw(_harness.Case, change);

        Assert.NotNull(preview);
        Assert.Equal(new HeuristicId(HeuristicNames.ChangeAddress, 1), preview.CountsInstead);
        var cluster = Assert.Single(preview.Changes);
        Assert.Contains(Wpkh("payer-change"), cluster.Leaving);
        Assert.Empty(cluster.Joining);
        Assert.Equal(cluster.Before.Count - cluster.Leaving.Count, cluster.After.Count);
        Assert.Equal(journal, _harness.Case.ReadJournal().Count);
        Assert.Equal(0, _harness.Count("clustering_withdrawals"));

        _harness.Service.Withdraw(_harness.Case, change, null);
        Assert.Equal(
            cluster.After,
            _harness.ClusterOf(cluster.Anchor).Addresses.OrderBy(address => address.Value, StringComparer.Ordinal));
    }

    [Fact]
    public void No_preview_for_a_version_that_cannot_be_drawn_back_or_already_is()
    {
        var change = new HeuristicId(HeuristicNames.ChangeAddress, 2);

        Assert.Null(_harness.Service.PreviewWithdraw(_harness.Case, new HeuristicId(UserMerges.Name, 1)));
        Assert.Null(_harness.Service.PreviewWithdraw(_harness.Case, new HeuristicId("no-such", 3)));
        Assert.Null(_harness.Service.PreviewWithdraw(
            _harness.Case, new HeuristicId(HeuristicNames.CommonInputOwnership, 1)));
        Assert.NotNull(_harness.Service.PreviewWithdraw(_harness.Case, change));
        _harness.Service.Withdraw(_harness.Case, change, null);
        Assert.Null(_harness.Service.PreviewWithdraw(_harness.Case, change));
    }

    [Fact]
    public void What_a_run_holds_back_is_kept_with_what_was_missing_until_a_run_knows_it_again()
    {
        var (seen, payment) = Disagreement();
        _harness.Analyse(seen, payment);
        Assert.Empty(_harness.Service.GetHeldBack(_harness.Case));

        _harness.Analyse(payment);

        var held = Assert.Single(_harness.Service.GetHeldBack(_harness.Case));
        Assert.Equal(Txid("disagree"), held.Txid);
        Assert.Equal([Txid("seen-earlier")], held.Missing);
        Assert.True(held.IsWaiting);
        Assert.NotEmpty(held.Evidence);

        _harness.Analyse(seen, payment);
        Assert.Empty(_harness.Service.GetHeldBack(_harness.Case));
    }

    [Fact]
    public void Letting_a_held_back_conclusion_stand_is_sealed_and_it_waits_no_more()
    {
        var (seen, payment) = Disagreement();
        _harness.Analyse(seen, payment);
        _harness.Analyse(payment);
        var before = _harness.ClusterOf(Wpkh("payer-x")).Members.Count;

        var decision = _harness.Service.DecideHeldBack(_harness.Case, Txid("disagree"), ConclusionChoice.Keep, null);

        Assert.NotNull(decision);
        Assert.Equal([Txid("seen-earlier")], decision.Missing);
        Assert.Contains(Wpkh("payer-x"), decision.Addresses);
        var held = Assert.Single(_harness.Service.GetHeldBack(_harness.Case));
        Assert.False(held.IsWaiting);
        Assert.Equal(decision.Sequence, held.Decision!.Sequence);
        Assert.Equal(before, _harness.ClusterOf(Wpkh("payer-x")).Members.Count);
        Assert.Single(_harness.Case.ReadJournal(), entry => entry.Kind == ClusteringJournalKinds.HeldBackDecided);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void Letting_a_held_back_conclusion_go_stops_it_counting_and_the_next_run_concludes_anew()
    {
        var (seen, payment) = Disagreement();
        _harness.Analyse(seen, payment);
        _harness.Analyse(payment);
        var stored = _harness.Count("clustering_evidence");
        var old = _harness.Service.GetConclusions(_harness.Case, Txid("disagree"));

        var decision = _harness.Service.DecideHeldBack(
            _harness.Case, Txid("disagree"), ConclusionChoice.Release, "Tx:en ersattes.");

        Assert.Equal("Tx:en ersattes.", decision!.Reason);
        Assert.Empty(_harness.Service.GetConclusions(_harness.Case, Txid("disagree")).Evidence);
        Assert.Empty(_harness.Service.GetHeldBack(_harness.Case));
        Assert.Equal(stored, _harness.Count("clustering_evidence"));
        Assert.Equal(decision.Sequence, Assert.Single(_harness.Service.GetHeldBackDecisions(_harness.Case)).Sequence);

        var run = _harness.Analyse(payment);

        Assert.Empty(run.HeldBack);
        var fresh = _harness.Service.GetConclusions(_harness.Case, Txid("disagree"));
        Assert.DoesNotContain(fresh.Findings, found => old.Findings.Any(earlier => earlier.Sequence == found.Sequence));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void Nothing_is_decided_about_a_transaction_that_is_not_held_back()
    {
        _harness.Analyse(Patterns.PaymentWithChange());

        Assert.Null(_harness.Service.DecideHeldBack(_harness.Case, Txid("payment"), ConclusionChoice.Release, null));
        Assert.Empty(_harness.Service.GetHeldBackDecisions(_harness.Case));
        Assert.Throws<ArgumentException>(() => _harness.Service.DecideHeldBack(
            _harness.Case, Txid("payment"), ConclusionChoice.Keep, new string('x', 1_001)));
    }

    [Fact]
    public void A_decision_can_be_taken_back_and_a_release_then_counts_again()
    {
        var (seen, payment) = Disagreement();
        _harness.Analyse(seen, payment);
        _harness.Analyse(payment);
        var release = _harness.Service.DecideHeldBack(_harness.Case, Txid("disagree"), ConclusionChoice.Release, null)!;
        Assert.Empty(_harness.Service.GetHeldBack(_harness.Case));

        var reopened = _harness.Service.ReopenHeldBack(_harness.Case, release.Sequence, "Fel");

        Assert.Equal((ConclusionChoice.Reopen, release.Sequence), (reopened!.Choice, reopened.Undoes));
        var held = Assert.Single(_harness.Service.GetHeldBack(_harness.Case));
        Assert.True(held.IsWaiting);
        Assert.NotEmpty(held.Findings);
        Assert.Null(_harness.Service.ReopenHeldBack(_harness.Case, release.Sequence, null));
        Assert.Null(_harness.Service.ReopenHeldBack(_harness.Case, reopened.Sequence, null));

        var kept = _harness.Service.DecideHeldBack(_harness.Case, Txid("disagree"), ConclusionChoice.Keep, null)!;
        Assert.False(Assert.Single(_harness.Service.GetHeldBack(_harness.Case)).IsWaiting);
        _harness.Service.ReopenHeldBack(_harness.Case, kept.Sequence, null);
        Assert.True(Assert.Single(_harness.Service.GetHeldBack(_harness.Case)).IsWaiting);
        Assert.Throws<ArgumentException>(() =>
            _harness.Service.DecideHeldBack(_harness.Case, Txid("disagree"), ConclusionChoice.Reopen, null));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void An_edited_working_row_is_checked_against_the_sealed_basis_before_anything_is_sealed()
    {
        var (seen, payment) = Disagreement();
        _harness.Analyse(seen, payment);
        _harness.Analyse(payment);
        _harness.Case.Write(transaction =>
        {
            using var edit = transaction.CreateCommand("UPDATE clustering_held_back SET missing = $missing");
            edit.AddParameter("$missing", "[\"" + Txid("made-up").Value + "\"]");
            return edit.ExecuteNonQuery();
        });

        Assert.Null(_harness.Service.DecideHeldBack(_harness.Case, Txid("disagree"), ConclusionChoice.Release, null));
        Assert.Empty(_harness.Service.GetHeldBackDecisions(_harness.Case));
    }

    [Fact]
    public void A_run_that_holds_back_the_same_in_another_order_writes_nothing()
    {
        var (seen, payment) = Disagreement();
        _harness.Analyse(seen, payment);
        _harness.Analyse(payment);
        var first = Assert.Single(_harness.Service.GetHeldBack(_harness.Case));

        _harness.Analyse(payment);

        Assert.Equal(first.Txid, Assert.Single(_harness.Service.GetHeldBack(_harness.Case)).Txid);
    }

    [Fact]
    public void A_gone_transaction_is_left_out_so_what_rests_on_it_is_held_back_and_nothing_is_stored()
    {
        var (seen, payment) = Disagreement();
        _harness.Analyse(seen, payment);
        var stored = _harness.Count("clustering_evidence");
        var journal = _harness.Case.ReadJournal().Count;
        var before = _harness.ClusterOf(Wpkh("payer-x")).Members.Count;

        var run = _harness.Service.Analyse(_harness.Case, [seen, payment], [Txid("seen-earlier")]);

        Assert.Equal(1, run.TransactionsAnalysed);
        Assert.Equal([Txid("disagree")], run.HeldBack);
        var held = Assert.Single(_harness.Service.GetHeldBack(_harness.Case));
        Assert.Equal((Txid("disagree"), Txid("seen-earlier")), (held.Txid, Assert.Single(held.Missing)));
        Assert.Equal(stored, _harness.Count("clustering_evidence"));
        Assert.Equal(journal, _harness.Case.ReadJournal().Count);
        Assert.Equal(before, _harness.ClusterOf(Wpkh("payer-x")).Members.Count);
    }

    [Fact]
    public void A_gone_transactions_own_conclusions_are_held_back_on_itself_and_can_be_let_stand()
    {
        var payment = Patterns.PaymentWithChange();
        _harness.Analyse(payment);
        var stored = _harness.Count("clustering_evidence");
        var before = _harness.ClusterOf(Wpkh("payer-change")).Members.Count;

        var run = _harness.Service.Analyse(_harness.Case, [payment], [payment.Txid]);

        Assert.Equal(0, run.TransactionsAnalysed);
        Assert.Equal([payment.Txid], run.HeldBack);
        var held = Assert.Single(_harness.Service.GetHeldBack(_harness.Case));
        Assert.Equal([payment.Txid], held.Missing);
        Assert.NotEmpty(held.Evidence);
        Assert.Equal(before, _harness.ClusterOf(Wpkh("payer-change")).Members.Count);

        Assert.Null(_harness.Service.DecideHeldBack(_harness.Case, payment.Txid, ConclusionChoice.Keep, null));
        var kept = _harness.Service.DecideHeldBack(
            _harness.Case, payment.Txid, ConclusionChoice.Keep, null, [payment.Txid]);

        Assert.Equal([payment.Txid], kept!.Missing);
        _harness.Service.Analyse(_harness.Case, [payment], [payment.Txid]);
        Assert.False(Assert.Single(_harness.Service.GetHeldBack(_harness.Case)).IsWaiting);
        Assert.Equal(stored, _harness.Count("clustering_evidence"));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_gone_transaction_that_comes_back_is_analysed_again_and_nothing_is_held_back()
    {
        var (seen, payment) = Disagreement();
        _harness.Analyse(seen, payment);
        _harness.Service.Analyse(_harness.Case, [seen, payment], [Txid("seen-earlier")]);
        var stored = _harness.Count("clustering_evidence");

        var run = _harness.Service.Analyse(_harness.Case, [seen, payment], []);

        Assert.Empty(run.HeldBack);
        Assert.Empty(_harness.Service.GetHeldBack(_harness.Case));
        Assert.Equal(stored, _harness.Count("clustering_evidence"));
    }

    [Fact]
    public void A_gone_transactions_own_conclusions_let_go_count_no_more_and_return_anew_when_it_comes_back()
    {
        var payment = Patterns.PaymentWithChange();
        _harness.Analyse(payment);
        _harness.Service.Analyse(_harness.Case, [payment], [payment.Txid]);

        _harness.Service.DecideHeldBack(
            _harness.Case, payment.Txid, ConclusionChoice.Release, "Ersatt.", [payment.Txid]);

        Assert.Empty(_harness.Service.GetConclusions(_harness.Case, payment.Txid).Evidence);
        Assert.Empty(_harness.Service.Analyse(_harness.Case, [payment], [payment.Txid]).HeldBack);
        Assert.Empty(_harness.Service.GetHeldBack(_harness.Case));

        _harness.Analyse(payment);

        Assert.NotEmpty(_harness.Service.GetConclusions(_harness.Case, payment.Txid).Evidence);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_gone_transaction_without_conclusions_is_not_held_back()
    {
        var (seen, payment) = Disagreement();
        _harness.Analyse(payment);

        var run = _harness.Service.Analyse(_harness.Case, [seen, payment], [Txid("seen-earlier")]);

        Assert.Empty(run.HeldBack);
        Assert.Empty(_harness.Service.GetHeldBack(_harness.Case));
    }

    [Fact]
    public void An_address_first_seen_in_a_gone_transaction_does_not_look_fresh_so_nothing_new_is_concluded()
    {
        var (gone, elsewhere, payment) = SeenOnlyInAGoneTransaction();
        using (var control = new CaseHarness())
        {
            // Without the gone transaction at all, the address looks fresh and the payment gets a change proposal.
            control.Analyse(elsewhere, payment);
            Assert.NotEmpty(control.Service.GetConclusions(control.Case, payment.Txid).Evidence);
        }

        _harness.Analyse(gone, elsewhere, payment);
        Assert.Empty(_harness.Service.GetConclusions(_harness.Case, payment.Txid).Evidence);
        var stored = _harness.Count("clustering_evidence");

        var run = _harness.Service.Analyse(_harness.Case, [gone, elsewhere, payment], [gone.Txid]);

        Assert.Equal(0, run.NewResults);
        Assert.Empty(_harness.Service.GetConclusions(_harness.Case, payment.Txid).Evidence);
        Assert.Equal(stored, _harness.Count("clustering_evidence"));
        Assert.DoesNotContain(
            Wpkh("fresh-payer"), _harness.ClusterOf(Wpkh("seen-in-gone")).Addresses);
    }

    [Fact]
    public void A_two_input_payment_to_an_address_seen_in_a_gone_transaction_gets_its_common_inputs_but_no_change()
    {
        var (seen, payment) = Disagreement();
        var other = Patterns.PaymentWithChange();

        var run = _harness.Service.Analyse(_harness.Case, [seen, payment, other], [Txid("seen-earlier")]);

        // Common inputs rest on nothing else and are sealed; the change rests on the gone transaction and is not, and
        // the common inputs are weighed as if the change analysis had no opinion.
        var conclusions = _harness.Service.GetConclusions(_harness.Case, Txid("disagree"));
        Assert.Contains(
            conclusions.Evidence, each => each.Heuristic.Name == HeuristicNames.CommonInputOwnership);
        Assert.DoesNotContain(conclusions.Evidence, each => each.Heuristic.Name == HeuristicNames.ChangeAddress);
        Assert.DoesNotContain(conclusions.Findings, found => found.Heuristic.Name == HeuristicNames.ChangeAddress);
        Assert.All(conclusions.Evidence, each => Assert.DoesNotContain(
            "växel", each.Adjustment ?? string.Empty, StringComparison.Ordinal));
        Assert.Equal([Txid("disagree")], run.NotDrawn);
        Assert.Contains(
            $"\"notDrawn\":[\"{Txid("disagree").Value}\"]", run.Journal!.Details!, StringComparison.Ordinal);

        _harness.Analyse(seen, payment);

        Assert.Contains(
            _harness.Service.GetConclusions(_harness.Case, Txid("disagree")).Evidence,
            each => each.Heuristic.Name == HeuristicNames.ChangeAddress);
    }

    [Fact]
    public void A_result_that_concludes_nothing_but_cites_a_gone_transaction_holds_nothing_back()
    {
        using var harness = new CaseHarness(services =>
            services.AddSingleton<IClusteringHeuristic, NoOpinionCiting>());
        var basis = Tx("probe-basis", 800_000).In(Wpkh("elsewhere"), 1_000).Out(Wpkh("bulk"), 900).Build();
        var consolidation = Patterns.Consolidation();

        var run = harness.Service.Analyse(harness.Case, [basis, consolidation], [basis.Txid]);

        Assert.Empty(run.NotDrawn);
        Assert.NotEmpty(harness.Service.GetConclusions(harness.Case, consolidation.Txid).Evidence);
    }

    [Fact]
    public void What_was_not_drawn_is_journalled_once_when_it_changes_also_when_nothing_else_is_stored()
    {
        var (seen, payment) = Disagreement();
        _harness.Service.Analyse(_harness.Case, [seen, payment], [Txid("seen-earlier")]);
        var journal = _harness.Case.ReadJournal().Count;

        var again = _harness.Service.Analyse(_harness.Case, [seen, payment], [Txid("seen-earlier")]);

        Assert.Null(again.Journal);
        Assert.Equal([Txid("disagree")], again.NotDrawn);
        Assert.Equal(journal, _harness.Case.ReadJournal().Count);

        using var fresh = new CaseHarness();
        fresh.Analyse(payment);
        var onlyNotDrawn = fresh.Service.Analyse(fresh.Case, [seen, payment], [Txid("seen-earlier")]);

        Assert.Equal(0, onlyNotDrawn.NewResults);
        Assert.Contains("notDrawn", onlyNotDrawn.Journal!.Details!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mixed_set_of_old_results_is_not_partly_redone_while_a_transaction_is_gone()
    {
        var (seen, payment) = Disagreement();
        _harness.Analyse(payment);
        var before = _harness.Service.GetConclusions(_harness.Case, Txid("disagree"));
        var stored = _harness.Count("clustering_evidence");

        var run = _harness.Service.Analyse(_harness.Case, [seen, payment], [Txid("seen-earlier")]);

        Assert.Equal([Txid("disagree")], run.NotDrawn);
        Assert.Equal(stored, _harness.Count("clustering_evidence"));
        Assert.Equal(
            before.Evidence.Select(evidence => evidence.Sequence),
            _harness.Service.GetConclusions(_harness.Case, Txid("disagree")).Evidence.Select(each => each.Sequence));
    }

    [Fact]
    public void A_withdrawn_versions_basis_stops_holding_back_once_the_transaction_is_weighed_again()
    {
        using var harness = new CaseHarness(services =>
        {
            services.AddSingleton<IClusteringHeuristic>(new BasisProbe(1));
            services.AddSingleton<IClusteringHeuristic>(new BasisProbe(2));
        });
        var basis = Tx("probe-basis", 800_000).In(Wpkh("elsewhere"), 1_000).Out(Wpkh("bulk"), 900).Build();
        var probed = Tx("probed", 900_000).In(Wpkh("probe-a"), 1_000).In(Wpkh("probe-b"), 2_000)
            .Out(Wpkh("probe-out"), 2_900).Build();
        harness.Analyse(basis, probed);
        harness.Service.Withdraw(harness.Case, new HeuristicId(BasisProbe.Name, 2), null);

        // Gone while still weighed against version 2: held back, as what would be re-weighed rests on it.
        Assert.Contains(probed.Txid, harness.Service.Analyse(harness.Case, [basis, probed], [basis.Txid]).HeldBack);

        // Back: re-weighed with version 1, which rests on nothing.
        Assert.Empty(harness.Service.Analyse(harness.Case, [basis, probed], []).HeldBack);

        // Gone again: version 2's basis no longer holds anything back.
        var run = harness.Service.Analyse(harness.Case, [basis, probed], [basis.Txid]);

        Assert.DoesNotContain(probed.Txid, run.HeldBack);
        Assert.DoesNotContain(harness.Service.GetHeldBack(harness.Case), held => held.Txid == probed.Txid);
    }

    [Fact]
    public void A_withdrawal_while_a_transaction_is_gone_does_not_redo_what_rested_on_it()
    {
        var (seen, payment) = Disagreement();
        _harness.Analyse(seen, payment);
        _harness.Service.Withdraw(_harness.Case, new HeuristicId(HeuristicNames.ChangeAddress, 2), null);
        var stored = _harness.Count("clustering_evidence");

        var run = _harness.Service.Analyse(_harness.Case, [seen, payment], [Txid("seen-earlier")]);

        Assert.Contains(Txid("disagree"), run.HeldBack);
        Assert.Equal(stored, _harness.Count("clustering_evidence"));
        var held = Assert.Single(_harness.Service.GetHeldBack(_harness.Case));
        Assert.Contains(Txid("seen-earlier"), held.Missing);
        Assert.NotNull(_harness.Service.DecideHeldBack(_harness.Case, Txid("disagree"), ConclusionChoice.Keep, null));
    }

    [Fact]
    public void Only_a_transaction_said_to_be_gone_can_be_sealed_as_missing_from_its_own_conclusions()
    {
        var payment = Patterns.PaymentWithChange();
        _harness.Analyse(payment);
        _harness.Service.Analyse(_harness.Case, [payment], [payment.Txid]);

        Assert.Null(_harness.Service.DecideHeldBack(
            _harness.Case, payment.Txid, ConclusionChoice.Release, null, [Txid("something-else")]));
        Assert.Empty(_harness.Service.GetHeldBackDecisions(_harness.Case));
    }

    [Fact]
    public void A_run_that_stores_while_holding_back_journals_what_it_held_back_and_left_out()
    {
        var (seen, payment) = Disagreement();
        _harness.Analyse(seen, payment);
        var other = Patterns.PaymentWithChange();

        _harness.Analyse(seen, payment, other);
        var further = Patterns.PaymentWithWeakSigns();

        var run = _harness.Service.Analyse(_harness.Case, [seen, payment, other, further], [Txid("seen-earlier")]);

        Assert.NotNull(run.Journal);
        Assert.Equal([Txid("seen-earlier")], run.LeftOut);
        var details = run.Journal.Details!;
        Assert.Contains($"\"heldBack\":[\"{Txid("disagree").Value}\"]", details, StringComparison.Ordinal);
        Assert.Contains($"\"leftOut\":[\"{Txid("seen-earlier").Value}\"]", details, StringComparison.Ordinal);
        Assert.DoesNotContain("leftOut", _harness.Case.ReadJournal()
            .First(entry => entry.Kind == ClusteringJournalKinds.Analysed).Details!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gives a payment whose change is told by a transaction seen earlier, so that a run without that transaction
    /// holds the payment's conclusions back.
    /// </summary>
    /// <returns>The earlier transaction and the payment.</returns>
    private static (BitcoinTransaction Seen, BitcoinTransaction Payment) Disagreement()
    {
        var seen = Tx("seen-earlier", 800_000).In(Wpkh("elsewhere"), 9_000_000).Out(Wpkh("bulk"), 8_000_000).Build();
        var payment = Tx("disagree", 900_000)
            .In(Wpkh("payer-x"), 10_000_000)
            .In(Wpkh("payer-y"), 1_000_000)
            .Out(Wpkh("payer-z"), 1_234_567)
            .Out(Wpkh("bulk"), 9_700_000)
            .Build();
        return (seen, payment);
    }

    /// <summary>
    /// Gives a payment with two outputs: one to an address the case first saw in a transaction that is now gone, and
    /// one to an address seen in another earlier transaction, so that neither is fresh while the gone one is known.
    /// </summary>
    /// <returns>The gone transaction, the other earlier one and the payment.</returns>
    private static (BitcoinTransaction Gone, BitcoinTransaction Elsewhere, BitcoinTransaction Payment)
        SeenOnlyInAGoneTransaction()
    {
        var gone = Tx("gone-first-sight", 800_000).In(Wpkh("someone"), 3_000_000).Out(Wpkh("seen-in-gone"), 2_990_000)
            .Build();
        var elsewhere = Tx("elsewhere-sight", 800_001).In(Wpkh("another"), 4_000_000).Out(Wpkh("seen-other"), 3_990_000)
            .Build();
        var payment = Tx("pays-both", 900_000)
            .In(Wpkh("fresh-payer"), 5_000_000)
            .Out(Wpkh("seen-in-gone"), 1_234_567)
            .Out(Wpkh("seen-other"), 3_745_678)
            .Build();
        return (gone, elsewhere, payment);
    }
}
