using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using static Chainsaw.Modules.Clustering.Tests.Synthetic;

namespace Chainsaw.Modules.Clustering.Tests;

/// <summary>
/// Undoing a split (sketch AN1b): a new sealed record, nothing removed, the heuristic may merge again, a confirmation
/// says what merges first, and an undone split cannot be undone again.
/// </summary>
public sealed class UndoSplitTests : IDisposable
{
    private readonly CaseHarness _harness = new();

    public UndoSplitTests() => _harness.Analyse(Patterns.PaymentWithChange());

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void An_undone_split_lets_the_heuristic_merge_again_and_stays_sealed()
    {
        var split = Split();

        Assert.Equal(1, _harness.Service.UndoSplits(_harness.Case, [split.Sequence], "Fel av mig"));

        AssertMembers(_harness.ClusterOf(Wpkh("payer-1")), "payer-1", "payer-2", "payer-change");
        Assert.Empty(_harness.ClusterOf(Wpkh("payer-1")).Splits);
        Assert.Equal(1, _harness.Count("clustering_splits"));
        Assert.Equal(1, _harness.Count("clustering_split_undos"));
        var entry = Assert.Single(_harness.Service.GetSplitHistory(_harness.Case, [Wpkh("payer-change")]));
        Assert.Equal(split, entry.Split);
        Assert.True(entry.IsUndone);
        Assert.Equal("Fel av mig", entry.Undo!.Reason);
        var journal = _harness.Case.ReadJournal().Last();
        Assert.Equal(ClusteringJournalKinds.SplitUndone, journal.Kind);
        Assert.Equal(journal.Sequence, entry.Undo.JournalSequence);
        Assert.Equal(journal.Utc, entry.Undo.RecordedUtc);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void The_reason_is_optional(string? reason)
    {
        var split = Split();

        Assert.Equal(1, _harness.Service.UndoSplits(_harness.Case, [split.Sequence], reason));

        Assert.Null(Assert.Single(_harness.Service.GetSplitHistory(_harness.Case, [Wpkh("payer-1")])).Undo!.Reason);
    }

    [Fact]
    public void A_reason_that_is_too_long_is_refused_and_nothing_is_stored()
    {
        var split = Split();

        Assert.Throws<ArgumentException>(
            () => _harness.Service.UndoSplits(_harness.Case, [split.Sequence], new string('x', 1_001)));

        Assert.Equal(0, _harness.Count("clustering_split_undos"));
    }

    [Fact]
    public void An_undone_split_cannot_be_undone_again_but_the_addresses_can_be_split_anew()
    {
        var split = Split();
        _harness.Service.UndoSplits(_harness.Case, [split.Sequence], null);
        var before = _harness.Case.ReadJournal().Count;

        Assert.Equal(0, _harness.Service.UndoSplits(_harness.Case, [split.Sequence], null));
        Assert.Equal(0, _harness.Service.UndoSplits(_harness.Case, [999], null));
        Assert.Equal(before, _harness.Case.ReadJournal().Count);

        var anew = Split();
        AssertMembers(_harness.ClusterOf(Wpkh("payer-1")), "payer-1", "payer-2");
        var history = _harness.Service.GetSplitHistory(_harness.Case, [Wpkh("payer-1")]);
        Assert.Equal([split.Sequence, anew.Sequence], history.Select(entry => entry.Split.Sequence));
        Assert.Equal([true, false], history.Select(entry => entry.IsUndone));
    }

    [Fact]
    public void The_undoing_holds_after_the_case_is_closed_and_opened()
    {
        var split = Split();
        _harness.Service.UndoSplits(_harness.Case, [split.Sequence], "Fel");

        _harness.Reopen();

        AssertMembers(_harness.ClusterOf(Wpkh("payer-1")), "payer-1", "payer-2", "payer-change");
        Assert.True(Assert.Single(_harness.Service.GetSplitHistory(_harness.Case, [Wpkh("payer-1")])).IsUndone);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void The_confirmation_says_which_addresses_merge_again_and_by_which_heuristic_without_storing()
    {
        var split = Split();
        var before = _harness.Case.ReadJournal().Count;

        var preview = _harness.Service.PreviewUndoSplits(_harness.Case, [split.Sequence], Wpkh("payer-2"));

        Assert.NotNull(preview);
        Assert.True(preview.IsInForce);
        var member = Assert.Single(preview.Rejoined);
        Assert.Equal(Wpkh("payer-change"), member.Address);
        Assert.Equal(HeuristicNames.ChangeAddress, member.JoinedBy!.Evidence.Heuristic.Name);
        Assert.Equal(HeuristicNames.ChangeAddress, Assert.Single(preview.Heuristics).Name);
        Assert.Equal(before, _harness.Case.ReadJournal().Count);
        Assert.Equal(0, _harness.Count("clustering_split_undos"));
    }

    [Fact]
    public void The_confirmation_seen_from_the_other_side_names_the_cluster_that_joins_it()
    {
        var split = Split();

        var preview = _harness.Service.PreviewUndoSplits(_harness.Case, [split.Sequence], Wpkh("payer-change"));

        Assert.Equal(
            [Wpkh("payer-1").Value, Wpkh("payer-2").Value],
            preview!.Rejoined.Select(member => member.Address.Value).Order());
    }

    [Fact]
    public void The_confirmation_says_when_another_split_still_holds_them_apart()
    {
        var change = Split();
        _harness.Service.Split(_harness.Case, new SplitRequest(
            Wpkh("payer-2"), Wpkh("payer-change"), HeuristicNames.ChangeAddress, "Också den."));

        var preview = _harness.Service.PreviewUndoSplits(_harness.Case, [change.Sequence], Wpkh("payer-1"));

        Assert.True(preview!.IsInForce);
        Assert.Empty(preview.Rejoined);
        Assert.Empty(preview.Heuristics);
    }

    [Fact]
    public void The_splits_of_one_breaking_out_are_undone_together_under_one_journal_line()
    {
        var first = Split();
        var second = _harness.Service.Split(_harness.Case, new SplitRequest(
            Wpkh("payer-2"), Wpkh("payer-change"), HeuristicNames.ChangeAddress, "Också den."));
        var before = _harness.Case.ReadJournal().Count;

        var preview = _harness.Service.PreviewUndoSplits(
            _harness.Case, [first.Sequence, second.Sequence], Wpkh("payer-1"));
        Assert.Equal(Wpkh("payer-change"), Assert.Single(preview!.Rejoined).Address);

        Assert.Equal(2, _harness.Service.UndoSplits(_harness.Case, [first.Sequence, second.Sequence], null));

        Assert.Equal(before + 1, _harness.Case.ReadJournal().Count);
        AssertMembers(_harness.ClusterOf(Wpkh("payer-1")), "payer-1", "payer-2", "payer-change");
    }

    [Fact]
    public void An_undone_split_has_nothing_to_confirm_and_an_unknown_one_has_no_confirmation()
    {
        var split = Split();
        _harness.Service.UndoSplits(_harness.Case, [split.Sequence], null);

        Assert.False(_harness.Service.PreviewUndoSplits(_harness.Case, [split.Sequence], null)!.IsInForce);
        Assert.Null(_harness.Service.PreviewUndoSplits(_harness.Case, [999], null));
    }

    [Fact]
    public void Undoing_without_an_operator_profile_is_refused()
    {
        var split = Split();
        _harness.Profiles.Profile = null;

        var error = Assert.Throws<CaseException>(
            () => _harness.Service.UndoSplits(_harness.Case, [split.Sequence], null));

        Assert.Equal(CaseError.NoOperatorProfile, error.Error);
        Assert.Equal(0, _harness.Count("clustering_split_undos"));
    }

    [Fact]
    public void The_history_has_only_the_splits_of_the_asked_addresses()
    {
        Split();

        Assert.Single(_harness.Service.GetSplitHistory(_harness.Case, [Wpkh("payer-change"), Wpkh("nobody")]));
        Assert.Empty(_harness.Service.GetSplitHistory(_harness.Case, [Wpkh("payer-2")]));
        Assert.Empty(_harness.Service.GetSplitHistory(_harness.Case, []));
    }

    private ClusterSplit Split() => _harness.Service.Split(_harness.Case, new SplitRequest(
        Wpkh("payer-1"), Wpkh("payer-change"), HeuristicNames.ChangeAddress, "Växeln är en annan persons."));

    private static void AssertMembers(Cluster cluster, params string[] labels) =>
        Assert.Equal(
            labels.Select(label => Wpkh(label).Value).Order(),
            cluster.Addresses.Select(address => address.Value).Order());
}
