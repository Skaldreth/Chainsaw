using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using static Chainsaw.Modules.Checks.Tests.Synthetic;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>
/// Approving a wallet (<c>wallet-proposals v1</c>): the decision and the user's merge (<c>user v1</c>) are sealed
/// together or not at all, taking the approval back undoes the merge, and undoing the merge from the history takes the
/// approval back, so that the panel and the clusters always say the same.
/// </summary>
public sealed class WalletApprovalTests : IDisposable
{
    /// <summary>A transaction whose inputs the clustering only proposes have the same owner.</summary>
    private static readonly IReadOnlyList<BitcoinTransaction> Possible = [Patterns.PossibleCoinJoin()];

    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void A_wallet_is_a_suggestion_that_waits_for_one_decision_without_candidates()
    {
        var wallet = Wallet();

        Assert.Equal(CheckKind.Suggestion, wallet.Finding.Kind);
        Assert.Empty(wallet.Finding.Candidates);
        Assert.Equal(1, wallet.Waiting);
        Assert.Contains("user v1", wallet.Finding.Why, StringComparison.Ordinal);
        Assert.Single(_harness.Clustering.GetCluster(_harness.Case, Wpkh("possible-1")).Members);
    }

    [Fact]
    public void Approving_merges_the_addresses_as_the_users_judgement_sealed_with_the_decision()
    {
        var wallet = Wallet();

        var decision = Approve(wallet, "  Samma inloggning. ");

        var cluster = _harness.Clustering.GetCluster(_harness.Case, Wpkh("possible-1"));
        Assert.Equal([Wpkh("possible-1"), Wpkh("possible-2")], cluster.Addresses.OrderBy(a => a.Value));
        Assert.Equal(UserMerges.Id, Assert.Single(cluster.Links).Evidence.Heuristic);
        var merge = Assert.Single(Merges()).Merge;
        Assert.Equal(new MergeOrigin("checks.decisions", decision.Sequence), merge.Origin);
        Assert.Equal("Samma inloggning.", merge.Reason);
        Assert.Equal(Possible[0].Txid, merge.Txid);
        Assert.Equal(new HeuristicId("common-input-ownership", 1), merge.Proposal);
        Assert.Single(_harness.Journal(CheckJournalKinds.Decided));
        Assert.Single(_harness.Journal(ClusteringJournalKinds.Merged));
        Assert.True(Wallet().IsSettled);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void Taking_the_approval_back_undoes_the_merge_and_the_wallet_waits_again()
    {
        var approved = Approve(Wallet(), null);

        _harness.Service.Decide(_harness.Case, DecisionRequest.TakeBack(approved) with { Reason = "Fel" });

        Assert.Single(_harness.Clustering.GetCluster(_harness.Case, Wpkh("possible-1")).Members);
        var entry = Assert.Single(Merges());
        Assert.True(entry.IsUndone);
        Assert.Equal("Fel", entry.Undo!.Reason);
        Assert.Single(_harness.Journal(ClusteringJournalKinds.MergeUndone));
        var after = Wallet();
        Assert.False(after.IsSettled);
        Assert.Equal(1, after.Waiting);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void Approving_again_after_a_take_back_merges_anew_and_the_old_merge_stays_sealed()
    {
        var first = Approve(Wallet(), null);
        _harness.Service.Decide(_harness.Case, DecisionRequest.TakeBack(first));

        var second = Approve(Wallet(), null);

        Assert.Equal(2, _harness.Clustering.GetCluster(_harness.Case, Wpkh("possible-2")).Members.Count);
        var merges = Merges();
        Assert.Equal([true, false], merges.Select(entry => entry.IsUndone));
        Assert.Equal(second.Sequence, merges[1].Merge.Origin!.Sequence);
    }

    [Fact]
    public void Dismissing_a_wallet_merges_nothing()
    {
        var wallet = Wallet();

        _harness.Service.Decide(_harness.Case, new DecisionRequest(
            wallet.Analysis, wallet.Finding.Key, null, CheckVerdict.Dismissed, "Olika ägare"));

        Assert.Empty(Merges());
        Assert.Single(_harness.Clustering.GetCluster(_harness.Case, Wpkh("possible-1")).Members);
        Assert.Empty(_harness.Journal(ClusteringJournalKinds.Merged));
        Assert.True(Wallet().IsSettled);
    }

    [Fact]
    public void Undoing_the_merge_from_the_history_takes_the_approval_back()
    {
        var approved = Approve(Wallet(), null);
        var merge = Assert.Single(Merges()).Merge;

        var undone = _harness.Service.TakeBackMerges(_harness.Case, [merge.Sequence], "Ångrad i historiken");

        Assert.Equal(1, undone);
        Assert.Single(_harness.Clustering.GetCluster(_harness.Case, Wpkh("possible-1")).Members);
        Assert.Equal("Ångrad i historiken", Assert.Single(Merges()).Undo!.Reason);
        var after = Wallet();
        Assert.False(after.IsSettled);
        var withdrawal = after.Decisions[^1];
        Assert.Equal(CheckVerdict.Withdrawn, withdrawal.Verdict);
        Assert.True(withdrawal.Sequence > approved.Sequence);
        Assert.Equal("Ångrad i historiken", withdrawal.Reason);
        Assert.Equal(2, _harness.Journal(CheckJournalKinds.Decided).Count);
        Assert.Single(_harness.Journal(ClusteringJournalKinds.MergeUndone));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void Undoing_a_merge_that_is_undone_or_unknown_writes_nothing()
    {
        var approved = Approve(Wallet(), null);
        var merge = Assert.Single(Merges()).Merge;
        _harness.Service.Decide(_harness.Case, DecisionRequest.TakeBack(approved));
        var before = _harness.Case.ReadJournal().Count;

        Assert.Equal(0, _harness.Service.TakeBackMerges(_harness.Case, [merge.Sequence], null));
        Assert.Equal(0, _harness.Service.TakeBackMerges(_harness.Case, [999], null));

        Assert.Equal(before, _harness.Case.ReadJournal().Count);
        Assert.Throws<ArgumentException>(
            () => _harness.Service.TakeBackMerges(_harness.Case, [merge.Sequence], new string('x', 1_001)));
    }

    [Fact]
    public void A_wallet_the_user_has_split_is_not_approved_and_nothing_is_sealed()
    {
        var wallet = Wallet();
        _harness.Clustering.Split(
            _harness.Case,
            new SplitRequest(Wpkh("possible-1"), Wpkh("possible-2"), UserMerges.Name, "Olika ägare enligt beslag"));
        var before = _harness.Case.ReadJournal().Count;

        Assert.Throws<ArgumentException>(() => Approve(wallet, null));

        Assert.Equal(0, _harness.Count("checks_decisions"));
        Assert.Equal(0, _harness.Count("clustering_merges"));
        Assert.Equal(before, _harness.Case.ReadJournal().Count);
        Assert.Equal(1, Wallet().Waiting);
    }

    [Fact]
    public void An_approved_wallet_stays_approved_and_merged_after_the_case_is_opened_again()
    {
        Approve(Wallet(), null);

        _harness.Reopen();

        Assert.True(Wallet().IsSettled);
        Assert.Equal(2, _harness.Clustering.GetCluster(_harness.Case, Wpkh("possible-1")).Members.Count);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void Another_suggestions_decisions_never_touch_the_clusters()
    {
        var check = Assert.Single(
            _harness.Run(Mixing.All(), Mixing.Mixer, Mixing.MixerOther).Checks,
            c => c.Analysis.Name == MixerOutputsCheck.Name && c.Finding.Key == $"{Txid("deposit").Value}:0");
        var approved = _harness.Service.Decide(_harness.Case, new DecisionRequest(
            check.Analysis, check.Finding.Key, check.Finding.Candidates[0].Key, CheckVerdict.Approved));

        _harness.Service.Decide(_harness.Case, DecisionRequest.TakeBack(approved));

        Assert.Empty(Merges());
        Assert.Empty(_harness.Journal(ClusteringJournalKinds.Merged));
    }

    /// <summary>Runs the checks over the possible CoinJoin and gives the wallet.</summary>
    /// <returns>The wallet.</returns>
    private StoredCheck Wallet() =>
        Assert.Single(_harness.Run(Possible).Checks, check => check.Analysis.Name == CheckAnalysisNames.Wallets);

    /// <summary>Approves a wallet.</summary>
    /// <param name="wallet">The wallet.</param>
    /// <param name="reason">The reason.</param>
    /// <returns>The sealed decision.</returns>
    private CheckDecision Approve(StoredCheck wallet, string? reason) =>
        _harness.Service.Decide(_harness.Case, new DecisionRequest(
            wallet.Analysis, wallet.Finding.Key, null, CheckVerdict.Approved, reason));

    /// <summary>Reads the user's merges in the clustering.</summary>
    /// <returns>The merges, oldest first.</returns>
    private IReadOnlyList<MergeHistoryEntry> Merges() => _harness.Case.Read(_harness.Clustering.GetMergeHistory);
}
