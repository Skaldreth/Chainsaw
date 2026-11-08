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
    public void A_wallet_says_what_kept_the_clustering_from_merging_it()
    {
        var wallet = Wallet();

        // The common-input proposal was lowered because the transaction may be a CoinJoin; the user must see that
        // before approving, or strangers may be merged on a reason that reads as certain.
        Assert.Contains(
            "Det som talar emot: Tx:en kan vara en CoinJoin", wallet.Finding.Why, StringComparison.Ordinal);
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

        var refused = Assert.Throws<MergeRefusedException>(() => Approve(wallet, null));

        Assert.Equal(MergeRefusal.AcrossSplit, refused.Refusal);

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

    [Fact]
    public void An_approved_wallet_says_so_instead_of_suggesting_it_again()
    {
        var wallet = Wallet();
        Approve(wallet, null);

        var after = Wallet();

        Assert.Equal(wallet.Finding.Key, after.Finding.Key);
        Assert.Equal("Godkänd plånbok", after.Finding.Title);
        Assert.Equal("user v1", after.Finding.Summary);
        Assert.Contains("Du godkände", after.Finding.Why, StringComparison.Ordinal);
        Assert.DoesNotContain("slog inte ihop", after.Finding.Why, StringComparison.Ordinal);
        Assert.Equal(Possible[0].Txid, Assert.Single(after.Finding.Basis).Txid);
        Assert.True(after.IsSettled);
    }

    [Fact]
    public void An_approved_wallet_whose_merge_is_broken_up_says_so()
    {
        Approve(Wallet(), null);
        _harness.Clustering.Split(
            _harness.Case, new SplitRequest(Wpkh("possible-2"), Wpkh("possible-1"), UserMerges.Name, string.Empty));

        var after = Wallet();

        Assert.Equal("Godkänd plånbok, uppbruten", after.Finding.Title);
        Assert.Contains("uppbruten sedan godkännandet", after.Finding.Why, StringComparison.Ordinal);
        Assert.True(after.IsSettled);
    }

    [Fact]
    public void A_wallet_that_grows_after_the_approval_suggests_only_what_is_new()
    {
        var first = Wallet();
        Approve(first, null);

        var run = _harness.Run([.. Possible, Growing]).Checks
            .Where(check => check.Analysis.Name == CheckAnalysisNames.Wallets)
            .ToList();

        Assert.Equal(2, run.Count);
        var approved = Assert.Single(run, check => check.Finding.Key == first.Finding.Key);
        Assert.True(approved.IsSettled);
        Assert.DoesNotContain(Wpkh("possible-3"), approved.Finding.Addresses);
        var grown = Assert.Single(run, check => check.Finding.Key != first.Finding.Key);
        Assert.Equal(Wpkh("possible-3").Value, grown.Finding.Key);
        Assert.Equal(1, grown.Waiting);
        Assert.Equal(
            new[] { Wpkh("possible-2").Value, Wpkh("possible-3").Value }.Order(),
            grown.Finding.Addresses.Select(address => address.Value).Order());

        Approve(grown, null);

        var merges = Merges();
        Assert.Equal(2, merges.Count);
        Assert.Equal(
            new[] { Wpkh("possible-2").Value, Wpkh("possible-3").Value }.Order(),
            merges[1].Merge.Addresses.Select(address => address.Value).Order());
        Assert.Equal(Growing.Txid, merges[1].Merge.Txid);
        Assert.Equal(3, _harness.Clustering.GetCluster(_harness.Case, Wpkh("possible-1")).Members.Count);
    }

    [Fact]
    public void Undoing_part_of_an_approval_from_the_history_takes_the_whole_approval_back()
    {
        var wallet = Assert.Single(
            _harness.Run([.. Possible, Growing]).Checks, check => check.Analysis.Name == CheckAnalysisNames.Wallets);
        Approve(wallet, null);
        var merges = Merges();
        Assert.Equal(2, merges.Count);
        Assert.Equal(merges[0].Merge.JournalSequence, merges[1].Merge.JournalSequence);

        var undone = _harness.Service.TakeBackMerges(_harness.Case, [merges[0].Merge.Sequence], null);

        Assert.Equal(2, undone);
        Assert.All(Merges(), entry => Assert.True(entry.IsUndone));
        Assert.Single(_harness.Clustering.GetCluster(_harness.Case, Wpkh("possible-2")).Members);
        var after = Assert.Single(
            _harness.Run([.. Possible, Growing]).Checks, check => check.Analysis.Name == CheckAnalysisNames.Wallets);
        Assert.Equal(1, after.Waiting);
        Assert.Equal(CheckVerdict.Withdrawn, after.Decisions[^1].Verdict);
        Assert.True(_harness.Verify().IsIntact);
    }

    /// <summary>A second possible CoinJoin that shares an input's address with the first: the wallet grows.</summary>
    private static BitcoinTransaction Growing { get; } = Tx("possible-again")
        .In(Wpkh("possible-2"), 3_000_000)
        .In(Wpkh("possible-3"), 2_000_000)
        .Out(Wpkh("possible-out-3"), 2_400_000)
        .Out(Wpkh("possible-out-4"), 2_400_000)
        .Build();

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
