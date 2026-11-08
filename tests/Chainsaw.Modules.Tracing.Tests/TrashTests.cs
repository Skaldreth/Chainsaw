using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// The graph's trash (sketch SO1a): a throw and a putting back are sealed records with a line in the journal, what is
/// hidden stays hidden when the case is opened again, and nothing is ever removed.
/// </summary>
public sealed class TrashTests : IDisposable
{
    private readonly CaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    /// <summary>A throw of x with two that follow, o3 under x and o4 under o3.</summary>
    private static IReadOnlyList<TrashMember> Branch() =>
    [
        new("cluster:bc1qx", null, "bc1q…x0wl", "Unknown") { Keys = ["address:bc1qx", "address:bc1qy"] },
        new("address:bc1qo3", "cluster:bc1qx", "bc1q…k3d0", "Unknown"),
        new("address:bc1qo4", "address:bc1qo3", "bc1q…7m2c", "Unknown"),
    ];

    [Fact]
    public void A_throw_is_sealed_with_a_journal_line_and_survives_reopening()
    {
        var thrown = _harness.Trash.Throw(_harness.Case, Branch());

        var journal = _harness.Case.ReadJournal().Last();
        Assert.Equal(TracingJournalKinds.Trashed, journal.Kind);
        Assert.Equal("Slängde i soptunnan: bc1q…x0wl och 2 till", journal.Summary);
        Assert.Equal(journal.Sequence, thrown.JournalSequence);
        Assert.Equal("Testa Testsson", thrown.By.Name);

        _harness.Reopen();

        var read = Assert.Single(_harness.Trash.GetThrows(_harness.Case));
        Assert.Equal(thrown.Sequence, read.Sequence);
        Assert.Equal(Branch().Select(member => member.Key), read.Members.Select(member => member.Key));
        Assert.Equal(["address:bc1qx", "address:bc1qy"], read.Members[0].FoundBy);
        Assert.Equal(2, read.DepthOf("address:bc1qo4"));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_branch_is_put_back_with_what_hangs_under_it_and_what_it_hangs_under_and_the_rest_stays()
    {
        var members = Branch().Append(new TrashMember("address:bc1qo5", "cluster:bc1qx", "bc1q…o5", "Unknown"))
            .ToList();
        var thrown = _harness.Trash.Throw(_harness.Case, members);

        var back = _harness.Trash.Restore(_harness.Case, thrown.Sequence, "address:bc1qo3");

        Assert.Equal(["cluster:bc1qx", "address:bc1qo3", "address:bc1qo4"], back.Select(member => member.Key));
        Assert.Equal(TracingJournalKinds.Restored, _harness.Case.ReadJournal().Last().Kind);
        _harness.Reopen();
        var left = Assert.Single(_harness.Trash.GetThrows(_harness.Case));
        Assert.Equal("address:bc1qo5", Assert.Single(left.Members).Key);
        Assert.Equal(0, left.DepthOf("address:bc1qo5"));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void The_whole_throw_is_put_back_and_leaves_the_trash()
    {
        var thrown = _harness.Trash.Throw(_harness.Case, Branch());

        Assert.Equal(3, _harness.Trash.Restore(_harness.Case, thrown.Sequence, null).Count);

        Assert.Empty(_harness.Trash.GetThrows(_harness.Case));
        var before = _harness.Case.ReadJournal().Count;
        Assert.Empty(_harness.Trash.Restore(_harness.Case, thrown.Sequence, null));
        Assert.Equal(before, _harness.Case.ReadJournal().Count);
    }

    [Fact]
    public void A_node_already_in_the_trash_is_not_thrown_twice_whichever_key_finds_it()
    {
        _harness.Trash.Throw(_harness.Case, Branch());

        Assert.Throws<InvalidOperationException>(() => _harness.Trash.Throw(
            _harness.Case, [new("cluster:bc1qy", null, "Kluster", "Cluster") { Keys = ["address:bc1qy"] }]));
        Assert.Throws<InvalidOperationException>(() => _harness.Trash.Throw(
            _harness.Case, [new("address:bc1qo3", null, "O3", "Unknown")]));
    }

    [Fact]
    public void A_cluster_that_grew_into_a_thrown_address_can_still_be_thrown()
    {
        _harness.Trash.Throw(_harness.Case, Branch());

        var grown = _harness.Trash.Throw(_harness.Case,
            [new("cluster:bc1qz", null, "Kluster", "Cluster") { Keys = ["address:bc1qy", "address:bc1qz"] }]);

        Assert.Equal(2, _harness.Trash.GetThrows(_harness.Case).Count);
        Assert.Equal("cluster:bc1qz", Assert.Single(grown.Members).Key);
    }

    [Theory]
    [InlineData("k", true)]
    [InlineData("other", false)]
    public void A_node_is_hidden_by_its_key_or_all_its_addresses_never_one_of_them(string key, bool byKey)
    {
        var lying = new HashSet<string>(StringComparer.Ordinal) { "k", "address:a", "address:b" };

        Assert.Equal(byKey, TrashMember.IsHidden(key, ["address:a", "address:c"], lying));
        Assert.True(TrashMember.IsHidden(key, ["address:a", "address:b"], lying));
        Assert.Equal(byKey, TrashMember.IsHidden(key, [], lying));
    }

    [Fact]
    public void A_put_back_node_can_be_thrown_again()
    {
        var thrown = _harness.Trash.Throw(_harness.Case, Branch());
        _harness.Trash.Restore(_harness.Case, thrown.Sequence, null);

        var again = _harness.Trash.Throw(_harness.Case, Branch());

        Assert.Equal(again.Sequence, Assert.Single(_harness.Trash.GetThrows(_harness.Case)).Sequence);
    }

    public static TheoryData<TrashMember[]> NotTrees => new()
    {
        Array.Empty<TrashMember>(),
        new TrashMember[] { new("a", "b", "A", "Unknown") },
        new TrashMember[] { new("a", null, "A", "Unknown"), new("b", "c", "B", "Unknown") },
        new TrashMember[] { new("a", null, "A", "Unknown"), new("a", "a", "A", "Unknown") },
        new TrashMember[] { new("a", null, " ", "Unknown") },
        new TrashMember[] { new("a", null, "A", "") },
    };

    [Theory]
    [MemberData(nameof(NotTrees))]
    public void Members_that_do_not_make_a_tree_are_refused_and_nothing_is_written(TrashMember[] members)
    {
        var before = _harness.Case.ReadJournal().Count;

        Assert.Throws<ArgumentException>(() => _harness.Trash.Throw(_harness.Case, members));

        Assert.Equal(before, _harness.Case.ReadJournal().Count);
    }

    [Fact]
    public void Without_an_operator_profile_nothing_is_thrown()
    {
        _harness.Profiles.Profile = null;

        var error = Assert.Throws<CaseException>(() => _harness.Trash.Throw(_harness.Case, Branch()));

        Assert.Equal(CaseError.NoOperatorProfile, error.Error);
        Assert.Empty(_harness.Trash.GetThrows(_harness.Case));
    }
}

/// <summary>The rule of what follows with a throw (sketch SO1a): what is reached only through what is thrown.</summary>
public sealed class TrashTreeTests
{
    /// <summary>The sketch's graph: s → x → {o1, o2, o3}, o1 → e2, o3 → {o4, o6}, o4 → o5, and s → d1 (dust).</summary>
    private static readonly (string From, string To)[] Lines =
    [
        ("s", "x"), ("s", "d1"), ("x", "o1"), ("x", "o2"), ("x", "o3"), ("o1", "e2"), ("o3", "o4"), ("o3", "o6"),
        ("o4", "o5"),
    ];

    [Fact]
    public void Everything_reached_only_through_the_thrown_hangs_under_it_depth_first()
    {
        var tree = TrashTree.Follow(["x"], Lines, ["s"]);

        Assert.Equal(
            [
                new TrashBranch("x", null), new("o1", "x"), new("e2", "o1"), new("o2", "x"), new("o3", "x"),
                new("o4", "o3"), new("o5", "o4"), new("o6", "o3"),
            ],
            tree);
    }

    [Fact]
    public void An_end_follows_alone()
    {
        Assert.Equal([new TrashBranch("d1", null)], TrashTree.Follow(["d1"], Lines, ["s"]));
    }

    [Fact]
    public void A_node_still_reached_another_way_stays()
    {
        var lines = Lines.Append(("s", "o4")).ToList();

        var tree = TrashTree.Follow(["o3"], lines, ["s"]);

        Assert.Equal([new TrashBranch("o3", null), new("o6", "o3")], tree);
    }

    [Fact]
    public void A_payer_seen_only_at_the_thrown_follows_it()
    {
        var lines = Lines.Append(("p", "o3")).ToList();

        var tree = TrashTree.Follow(["o3"], lines, ["s"]);

        Assert.Contains(new TrashBranch("p", "o3"), tree);
    }

    [Fact]
    public void Without_another_start_point_the_nodes_no_line_reaches_stand_in()
    {
        var tree = TrashTree.Follow(["x"], Lines, ["x"]);

        Assert.DoesNotContain(tree, branch => branch.Key is "s" or "d1");
        Assert.Contains(new TrashBranch("o5", "o4"), tree);
    }

    [Fact]
    public void Without_start_points_in_a_graph_of_only_cycles_everything_joined_to_the_thrown_follows()
    {
        // a → b → c → a and c → d → e → c: every node is reached by a line, so none can stand in for the start point,
        // and nothing stays reached; the cycle f ⇄ g is not joined to the thrown and is left alone.
        (string From, string To)[] lines =
            [("a", "b"), ("b", "c"), ("c", "a"), ("c", "d"), ("d", "e"), ("e", "c"), ("f", "g"), ("g", "f")];

        var tree = TrashTree.Follow(["a"], lines, ["a"]);

        Assert.Equal(
            [new TrashBranch("a", null), new("b", "a"), new("c", "a"), new("d", "c"), new("e", "c")],
            tree);
    }

    [Fact]
    public void Without_any_start_point_a_single_cycle_follows_whole()
    {
        var tree = TrashTree.Follow(["b"], [("a", "b"), ("b", "c"), ("c", "a")], []);

        Assert.Equal([new TrashBranch("b", null), new("a", "b"), new("c", "b")], tree);
    }

    [Fact]
    public void Several_thrown_each_head_their_own_branch_and_never_hang_under_each_other()
    {
        var tree = TrashTree.Follow(["o3", "o4"], Lines, ["s"]);

        Assert.Equal(
            [new TrashBranch("o3", null), new("o6", "o3"), new("o4", null), new("o5", "o4")],
            tree);
    }

    [Fact]
    public void A_long_peel_chain_is_walked_without_running_out_of_stack()
    {
        var lines = Enumerable.Range(0, 100_000)
            .Select(index => (From: "p" + index, To: "p" + (index + 1)))
            .Prepend(("s", "p0"))
            .ToList();

        var tree = TrashTree.Follow(["p0"], lines, ["s"]);

        Assert.Equal(100_001, tree.Count);
        Assert.Equal(new TrashBranch("p100000", "p99999"), tree[^1]);
    }

    [Fact]
    public void Nothing_thrown_is_refused()
    {
        Assert.Throws<ArgumentException>(() => TrashTree.Follow([], Lines, ["s"]));
    }
}
