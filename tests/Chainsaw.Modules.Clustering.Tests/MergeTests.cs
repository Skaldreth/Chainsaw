using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using static Chainsaw.Modules.Clustering.Tests.Synthetic;

namespace Chainsaw.Modules.Clustering.Tests;

/// <summary>
/// The user's own merges (<c>user v1</c>): sealed with who, when and an optional reason, respected by the clusters,
/// broken out by a split and undone like the heuristics' merges (sketch AN1b), and refused across a split in force.
/// </summary>
public sealed class MergeTests : IDisposable
{
    /// <summary>A transaction whose inputs are only proposed to have the same owner (a possible CoinJoin).</summary>
    private static readonly BitcoinTransaction Possible = Patterns.PossibleCoinJoin();

    private readonly CaseHarness _harness = new();

    public MergeTests() => _harness.Analyse(Possible, Patterns.PaymentWithChange());

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void The_inputs_of_a_possible_coinjoin_are_only_proposed_before_the_user_merges_them()
    {
        var cluster = _harness.ClusterOf(Wpkh("possible-1"));

        Assert.Single(cluster.Members);
        Assert.Contains(cluster.Suggestions, link => link.Evidence.Addresses.Contains(Wpkh("possible-2")));
    }

    [Fact]
    public void A_merge_joins_the_addresses_and_explains_itself_as_the_users_judgement()
    {
        var merge = Merge("Samma mönster som förra veckan");

        var cluster = _harness.ClusterOf(Wpkh("possible-1"));
        AssertMembers(cluster, "possible-1", "possible-2");
        var link = Assert.Single(cluster.Links);
        Assert.Equal(UserMerges.Id, link.Evidence.Heuristic);
        Assert.Equal(LinkState.Merged, link.State);
        Assert.Equal(Possible.Txid, link.Evidence.Txid);
        Assert.Equal(merge.Sequence, link.Evidence.Sequence);
        Assert.Equal(merge.JournalSequence, link.Evidence.JournalSequence);
        Assert.StartsWith("Användaren godkände förslaget från common-input-ownership v1", link.Evidence.Reason,
            StringComparison.Ordinal);
        Assert.EndsWith("Skäl: Samma mönster som förra veckan", link.Evidence.Reason, StringComparison.Ordinal);
        var path = _harness.Service.ExplainConnection(_harness.Case, Wpkh("possible-1"), Wpkh("possible-2"));
        Assert.Equal(UserMerges.Id, Assert.Single(path!).Evidence.Heuristic);
    }

    [Fact]
    public void A_merge_is_sealed_with_who_when_the_grounds_and_the_origin()
    {
        var origin = new MergeOrigin("checks.decisions", 7);

        var merge = Merge("Skäl", origin);

        var journal = _harness.Case.ReadJournal().Last();
        Assert.Equal(ClusteringJournalKinds.Merged, journal.Kind);
        Assert.Equal(journal.Sequence, merge.JournalSequence);
        Assert.Equal(journal.Utc, merge.RecordedUtc);
        Assert.Contains("Testa Testsson", journal.Operator.Name, StringComparison.Ordinal);
        Assert.Contains("user v1", journal.Summary, StringComparison.Ordinal);
        Assert.Equal(1, _harness.Count("clustering_merges"));
        var stored = Assert.Single(History()).Merge;
        Assert.Equal(merge, stored with { Addresses = merge.Addresses });
        Assert.Equal(merge.Addresses, stored.Addresses);
        Assert.Equal(origin, stored.Origin);
        Assert.Equal("Skäl", stored.Reason);
        Assert.Equal(new HeuristicId(HeuristicNames.CommonInputOwnership, 1), stored.Proposal);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void The_reason_is_optional(string? reason)
    {
        var merge = Merge(reason);

        Assert.Null(merge.Reason);
        Assert.DoesNotContain("Skäl", Assert.Single(_harness.ClusterOf(Wpkh("possible-1")).Links).Evidence.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_merge_that_is_not_valid_is_refused_and_nothing_is_stored()
    {
        var before = _harness.Case.ReadJournal().Count;
        var one = new MergeRequest([Wpkh("possible-1")], Possible.Txid, Cio, "Grund");
        var twice = new MergeRequest([Wpkh("possible-1"), Wpkh("possible-1")], Possible.Txid, Cio, "Grund");
        var bare = new MergeRequest([Wpkh("possible-1"), Wpkh("possible-2")], Possible.Txid, Cio, " ");
        var good = Request();

        Assert.Throws<ArgumentException>(() => Write(t => _harness.Service.Merge(t, [], null, null)));
        Assert.Throws<ArgumentException>(() => Write(t => _harness.Service.Merge(t, [one], null, null)));
        Assert.Throws<ArgumentException>(() => Write(t => _harness.Service.Merge(t, [twice], null, null)));
        Assert.Throws<ArgumentException>(() => Write(t => _harness.Service.Merge(t, [bare], null, null)));
        Assert.Throws<ArgumentException>(
            () => Write(t => _harness.Service.Merge(t, [good], new string('x', 1_001), null)));
        Assert.Throws<ArgumentException>(
            () => Write(t => _harness.Service.Merge(t, [good], null, new MergeOrigin(" ", 1))));

        Assert.Equal(0, _harness.Count("clustering_merges"));
        Assert.Equal(before, _harness.Case.ReadJournal().Count);
    }

    [Fact]
    public void A_merge_holds_when_the_transactions_are_analysed_again_and_the_case_is_opened_again()
    {
        Merge(null);

        _harness.Analyse(Possible, Patterns.PaymentWithChange());
        _harness.Reopen();

        AssertMembers(_harness.ClusterOf(Wpkh("possible-2")), "possible-2", "possible-1");
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_split_that_names_the_users_merges_breaks_it_out_and_undoing_the_split_merges_again()
    {
        Merge(null);

        var split = _harness.Service.Split(
            _harness.Case, new SplitRequest(Wpkh("possible-2"), Wpkh("possible-1"), UserMerges.Name, string.Empty));

        var cluster = _harness.ClusterOf(Wpkh("possible-1"));
        Assert.Single(cluster.Members);
        var refused = Assert.Single(cluster.Refused, link => link.Evidence.Heuristic == UserMerges.Id);
        Assert.Equal(LinkState.RefusedBySplit, refused.State);
        Assert.Equal(split, refused.Split);

        var preview = _harness.Service.PreviewUndoSplits(_harness.Case, [split.Sequence], Wpkh("possible-1"));
        Assert.Equal(UserMerges.Id, Assert.Single(preview!.Heuristics));

        _harness.Service.UndoSplits(_harness.Case, [split.Sequence], null);
        AssertMembers(_harness.ClusterOf(Wpkh("possible-1")), "possible-1", "possible-2");
    }

    [Fact]
    public void A_merge_across_a_split_in_force_is_refused_and_nothing_is_stored()
    {
        // The change was broken out of the payer's cluster; merging the two back by hand contradicts the split.
        _harness.Service.Split(
            _harness.Case,
            new SplitRequest(Wpkh("payer-change"), Wpkh("payer-1"), HeuristicNames.ChangeAddress, string.Empty));
        var before = _harness.Case.ReadJournal().Count;
        var across = new MergeRequest(
            [Wpkh("payer-1"), Wpkh("payer-change")], Payment.Txid, new(HeuristicNames.ChangeAddress, 1), "Växel");

        var refused = Assert.Throws<MergeRefusedException>(
            () => Write(t => _harness.Service.Merge(t, [across], null, null)));

        Assert.Equal(MergeRefusal.AcrossSplit, refused.Refusal);
        Assert.Equal(0, _harness.Count("clustering_merges"));
        Assert.Equal(before, _harness.Case.ReadJournal().Count);
        AssertMembers(_harness.ClusterOf(Wpkh("payer-1")), "payer-1", "payer-2");
    }

    [Fact]
    public void A_merge_that_would_break_up_a_cluster_the_user_never_broke_up_is_refused()
    {
        // The user splits the change from the first payer, and then would say the change has the same owner as the
        // second payer. Common-input-ownership holds the two payers together now; since the user's merges count
        // first, the merge would push the first payer out of the cluster without the user breaking it out.
        _harness.Service.Split(
            _harness.Case,
            new SplitRequest(Wpkh("payer-change"), Wpkh("payer-1"), HeuristicNames.ChangeAddress, string.Empty));
        var before = _harness.Case.ReadJournal().Count;
        var breaking = new MergeRequest([Wpkh("payer-2"), Wpkh("payer-change")], Payment.Txid, Cio, "Egen bedömning");

        var refused = Assert.Throws<MergeRefusedException>(
            () => Write(t => _harness.Service.Merge(t, [breaking], null, null)));

        Assert.Equal(MergeRefusal.BreaksMerge, refused.Refusal);
        Assert.Equal(0, _harness.Count("clustering_merges"));
        Assert.Equal(before, _harness.Case.ReadJournal().Count);
        AssertMembers(_harness.ClusterOf(Wpkh("payer-1")), "payer-1", "payer-2");
    }

    [Fact]
    public void Undoing_a_merge_says_first_what_a_heuristic_merges_again_without_storing()
    {
        // In a case where only the possible CoinJoin is analysed, the user splits the change from the first payer
        // and merges it with the second payer. The payment is analysed afterwards: its common-input-ownership link
        // would bring the split pair together, so it only suggests while the user's merge holds.
        using var harness = new CaseHarness();
        harness.Analyse(Possible);
        harness.Service.Split(
            harness.Case,
            new SplitRequest(Wpkh("payer-change"), Wpkh("payer-1"), HeuristicNames.ChangeAddress, string.Empty));
        var merge = Assert.Single(harness.Case.Write(t => harness.Service.Merge(
            t,
            [new MergeRequest([Wpkh("payer-2"), Wpkh("payer-change")], Payment.Txid, Cio, "Egen bedömning")],
            null,
            null)));
        harness.Analyse(Possible, Payment);
        var journal = harness.Case.ReadJournal().Count;

        var preview = harness.Service.PreviewUndoMerges(harness.Case, [merge.Sequence]);

        Assert.NotNull(preview);
        Assert.True(preview.IsInForce);
        Assert.Equal(merge, Assert.Single(preview.Merges) with { Addresses = merge.Addresses });
        Assert.Equal(
            [Wpkh("payer-1").Value, Wpkh("payer-2").Value],
            preview.Rejoined.Select(member => member.Address.Value).Order());
        Assert.Equal(HeuristicNames.CommonInputOwnership, Assert.Single(preview.Heuristics).Name);
        Assert.Equal(journal, harness.Case.ReadJournal().Count);
        Assert.Equal(0, harness.Count("clustering_merge_undos"));
    }

    [Fact]
    public void Undoing_a_merge_that_held_nothing_back_merges_nothing_again()
    {
        var merge = Merge(null);

        var preview = _harness.Service.PreviewUndoMerges(_harness.Case, [merge.Sequence]);

        Assert.True(preview!.IsInForce);
        Assert.Empty(preview.Rejoined);
        Assert.Empty(preview.Heuristics);
        Assert.Null(_harness.Service.PreviewUndoMerges(_harness.Case, [999]));
        Write(t => _harness.Service.UndoMerges(t, [merge.Sequence], null));
        Assert.False(_harness.Service.PreviewUndoMerges(_harness.Case, [merge.Sequence])!.IsInForce);
    }

    [Fact]
    public void An_undone_merge_no_longer_merges_and_stays_sealed_with_its_undoing()
    {
        var merge = Merge(null);

        var undone = Write(t => _harness.Service.UndoMerges(t, [merge.Sequence], "Fel plånbok"));

        Assert.Equal(1, undone);
        Assert.Single(_harness.ClusterOf(Wpkh("possible-1")).Members);
        var entry = Assert.Single(History());
        Assert.True(entry.IsUndone);
        Assert.Equal("Fel plånbok", entry.Undo!.Reason);
        var journal = _harness.Case.ReadJournal().Last();
        Assert.Equal(ClusteringJournalKinds.MergeUndone, journal.Kind);
        Assert.Equal(journal.Sequence, entry.Undo.JournalSequence);
        Assert.Equal(1, _harness.Count("clustering_merges"));
        Assert.Equal(1, _harness.Count("clustering_merge_undos"));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void An_undone_merge_cannot_be_undone_again_and_nothing_is_journalled()
    {
        var merge = Merge(null);
        Write(t => _harness.Service.UndoMerges(t, [merge.Sequence], null));
        var before = _harness.Case.ReadJournal().Count;

        Assert.Equal(0, Write(t => _harness.Service.UndoMerges(t, [merge.Sequence], null)));
        Assert.Equal(0, Write(t => _harness.Service.UndoMerges(t, [999], null)));

        Assert.Equal(before, _harness.Case.ReadJournal().Count);
    }

    [Fact]
    public void Undoing_with_a_reason_that_is_too_long_is_refused()
    {
        var merge = Merge(null);

        Assert.Throws<ArgumentException>(
            () => Write(t => _harness.Service.UndoMerges(t, [merge.Sequence], new string('x', 1_001))));

        Assert.Equal(0, _harness.Count("clustering_merge_undos"));
    }

    [Fact]
    public void The_conclusions_read_in_a_transaction_are_the_same_as_read_from_the_case()
    {
        var fromCase = _harness.Service.GetConclusions(_harness.Case, Possible.Txid);

        var fromReader = _harness.Case.Read(reader => _harness.Service.GetConclusions(reader, Possible.Txid));

        Assert.Equal(fromCase.Evidence.Select(e => e.Sequence), fromReader.Evidence.Select(e => e.Sequence));
        Assert.Equal(fromCase.Findings.Count, fromReader.Findings.Count);
    }

    /// <summary>An ordinary payment with change, whose inputs and change the heuristics merge.</summary>
    private static BitcoinTransaction Payment => Patterns.PaymentWithChange();

    /// <summary>The proposal the merges approve: common-input-ownership, version 1.</summary>
    private static HeuristicId Cio => new(HeuristicNames.CommonInputOwnership, 1);

    /// <summary>Gives the merge of the possible CoinJoin's two inputs.</summary>
    /// <returns>The request.</returns>
    private static MergeRequest Request() =>
        new([Wpkh("possible-1"), Wpkh("possible-2")], Possible.Txid, Cio, "Alla inputs i en tx har samma ägare.");

    /// <summary>Merges the possible CoinJoin's two inputs in a transaction of its own.</summary>
    /// <param name="reason">The user's reason.</param>
    /// <param name="origin">Where it came from.</param>
    /// <returns>The stored merge.</returns>
    private ClusterMerge Merge(string? reason, MergeOrigin? origin = null) =>
        Assert.Single(Write(t => _harness.Service.Merge(t, [Request()], reason, origin)));

    /// <summary>Reads the merge history.</summary>
    /// <returns>The history.</returns>
    private IReadOnlyList<MergeHistoryEntry> History() => _harness.Case.Read(_harness.Service.GetMergeHistory);

    /// <summary>Asserts a cluster's addresses, in any order.</summary>
    /// <param name="cluster">The cluster.</param>
    /// <param name="labels">The addresses' labels.</param>
    private static void AssertMembers(Cluster cluster, params string[] labels) =>
        Assert.Equal(
            labels.Select(label => Wpkh(label).Value).Order(),
            cluster.Addresses.Select(address => address.Value).Order());

    /// <summary>Runs work in a write transaction of the case.</summary>
    /// <typeparam name="T">What it gives.</typeparam>
    /// <param name="work">The work.</param>
    /// <returns>What it gave.</returns>
    private T Write<T>(Func<ICaseTransaction, T> work) => _harness.Case.Write(work);
}
