using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>
/// The surfaces of round four: drawing a version back (KV1a), a conclusion held back (HB1a), breaking up a pair
/// (BU1a), dust and poisoning (DM1a), many hops (HP1a) and a likely service as a label (TJ1b).
/// </summary>
public sealed class RoundFourSurfacesTests
{
    /// <summary>The second address of the sample cluster's longer way.</summary>
    private static readonly BitcoinAddress K3 = GraphSamples.Address(31);

    /// <summary>The rows under Varför? are the heuristics that merge, with a version drawn back struck through.</summary>
    [Fact]
    public void TheRowsAreTheHeuristicsThatMergeAndTheVersionsDrawnBack()
    {
        var cluster = Chain();
        var withdrawn = new VersionWithdrawal(
            1, new HeuristicId(HeuristicNames.ChangeAddress, 3), null, 4, DateTimeOffset.UnixEpoch);

        var rows = ClusterWhyViewModel.RowsOf(cluster, [withdrawn]);

        Assert.Equal(["common-input-ownership v1", "change-address v3", "change-address v2"], rows.Select(r => r.Name));
        Assert.True(rows[1].IsWithdrawn);
        Assert.False(rows[1].HasMenu);
        Assert.Equal(ClusterWhyViewModel.WithdrawnTag, rows[1].Tag);
        Assert.False(rows[0].CanWithdraw);
        Assert.True(rows[2].CanWithdraw);
        Assert.True(rows[2].CountsAgain);
        Assert.Equal(ClusterWhyViewModel.AgainTag, rows[2].Tag);
        Assert.StartsWith("2 adresser", rows[0].Detail, StringComparison.Ordinal);
    }

    /// <summary>The confirmation's bars are scaled to the widest cluster, and a cluster that grows is green.</summary>
    [Fact]
    public void TheBarsAreScaledToTheWidestCluster()
    {
        var preview = new WithdrawPreview(
            new HeuristicId(HeuristicNames.ChangeAddress, 2),
            new HeuristicId(HeuristicNames.ChangeAddress, 1),
            [
                new ClusterChange(GraphSamples.K1, [GraphSamples.K1, GraphSamples.K2, K3, GraphSamples.U], [GraphSamples.K1, GraphSamples.K2]),
                new ClusterChange(GraphSamples.E, [GraphSamples.E], [GraphSamples.E, GraphSamples.W]),
            ]);

        var bars = ClusterWhyViewModel.BarsOf(preview, address => address == GraphSamples.K1 ? "K1" : "K5");

        Assert.Equal("4 → 2", bars[0].Text);
        Assert.Equal(1.0, bars[0].BeforeShare);
        Assert.Equal(0.5, bars[0].KeptShare);
        Assert.False(bars[0].Grows);
        Assert.True(bars[1].Grows);
        Assert.Equal(0.25, bars[1].ChangedShare);
        Assert.Equal([K3, GraphSamples.U], preview.Changes[0].Leaving);
    }

    /// <summary>
    /// Drawing a version back reads the preview, stores it through the editor with the reason, and the toast's Ångra
    /// takes it back.
    /// </summary>
    [Fact]
    public async Task DrawingAVersionBackIsConfirmedStoredAndCanBeUndone()
    {
        var rig = new Rig();
        rig.Editor.Cluster = Chain();
        rig.Editor.WithdrawPreview = new WithdrawPreview(
            new HeuristicId(HeuristicNames.ChangeAddress, 2),
            new HeuristicId(HeuristicNames.ChangeAddress, 1),
            [new ClusterChange(GraphSamples.K1, [GraphSamples.K1, GraphSamples.K2, K3], [GraphSamples.K1, GraphSamples.K2])]);
        ToastRequest? toast = null;
        rig.Graph.ToastWanted += (_, request) => toast = request;
        rig.Graph.Choose(GraphSamples.KKey, null);
        var why = rig.Graph.ClusterWhy;
        Assert.True(why.HasRows);
        var row = why.Rows.Single(r => r.Name == "change-address v2");

        await why.AskWithdrawAsync(row);

        Assert.True(why.IsConfirmOpen);
        Assert.Equal("Dra tillbaka change-address v2?", why.ConfirmTitle);
        Assert.Single(why.Bars);
        Assert.NotNull(why.ChangeOf(rig.Graph.Scene!.Node(GraphSamples.KKey)!));
        Assert.True(rig.Graph.IsDimmedByWithdrawal(rig.Graph.Scene.Node(GraphSamples.UKey)!));
        why.Reason = "  Slog ihop fel.  ";
        await why.SubmitAsync();

        var stored = Assert.Single(rig.Editor.Withdrawals);
        Assert.Equal("Slog ihop fel.", stored.Reason);
        Assert.False(why.IsConfirmOpen);
        Assert.Null(why.Preview);
        Assert.Equal(ClusterWhyViewModel.UndoText, toast!.ActionText);
        toast.Action!();
        Assert.Equal([stored.Sequence], rig.Editor.TakenBack);
    }

    /// <summary>A version that cannot be drawn back says so, and nothing can be stored.</summary>
    [Fact]
    public async Task AVersionThatCannotBeDrawnBackSaysSo()
    {
        var rig = new Rig();
        rig.Editor.Cluster = Chain();
        rig.Graph.Choose(GraphSamples.KKey, null);
        var why = rig.Graph.ClusterWhy;

        await why.AskWithdrawAsync(why.Rows.Single(r => r.Name == "change-address v2"));

        Assert.Equal(ClusterWhyViewModel.CannotText, why.Notice);
        Assert.False(why.CanSubmit);
        why.Cancel();
        Assert.False(why.IsConfirmOpen);
    }

    /// <summary>The history shows a withdrawal with Ta tillbaka, and a held back conclusion with its two choices.</summary>
    [Fact]
    public void TheHistoryShowsWithdrawalsAndHeldBackConclusions()
    {
        var scene = GraphSamples.Scene();
        var node = scene.Node(GraphSamples.KKey)!;
        var held = Held();
        var released = new HeldBackDecision(
            7, GraphSamples.Tx[1], [GraphSamples.Tx[0]], [GraphSamples.K1], ConclusionChoice.Release, "Ersatt", 9,
            DateTimeOffset.UnixEpoch);
        var data = new NodeHistoryData([], [], [], null, new Dictionary<long, string> { [4] = "M. Persson" })
        {
            Withdrawals = [new VersionWithdrawal(2, new HeuristicId(HeuristicNames.ChangeAddress, 2), null, 4, DateTimeOffset.UnixEpoch)],
            HeldBack = [held],
            HeldBackDecisions = [released],
        };

        var rows = NodeHistory.Build(scene, node, data);

        var withdrawal = rows.Single(row => row.Kind == HistoryKind.VersionWithdrawn);
        Assert.Equal(HistoryUndo.TakeBackWithdrawal, withdrawal.Undo);
        Assert.Equal(2, withdrawal.WithdrawalSequence);
        Assert.Contains("M. Persson", withdrawal.Sub, StringComparison.Ordinal);
        var waiting = rows.Single(row => row.Undo == HistoryUndo.DecideHeldBack);
        Assert.Equal(NodeHistory.ReleaseText, waiting.UndoText);
        Assert.True(waiting.IsDashed);
        Assert.Equal(GraphSamples.Tx[2], waiting.HeldTxid);
        Assert.Contains(rows, row => row.Kind == HistoryKind.HeldBack && row.IsWithdrawn);
        var letGo = rows.Single(row => row.Undo == HistoryUndo.ReopenHeldBack);
        Assert.Equal("Slutsatsen släppt, också dess fynd", letGo.Text);
        Assert.Equal(7, letGo.DecisionSequence);
    }

    /// <summary>Låt stå and Släpp slutsatsen store their decision through the editor.</summary>
    [Fact]
    public async Task LettingAHeldBackConclusionStandOrGoIsStored()
    {
        var rig = new Rig();
        rig.Editor.History = new NodeHistoryData([], [], [], null, new Dictionary<long, string>())
        {
            HeldBack = [Held()],
        };
        rig.Graph.Choose(GraphSamples.KKey, null);
        var history = rig.Graph.History;
        history.ShowHistory();
        var row = history.Rows.Single(r => r.Event.Kind == HistoryKind.HeldBack);

        history.Toggle(row);
        Assert.True(history.IsHeldOpen);
        await history.KeepAsync();
        history.Toggle(history.Rows.Single(r => r.Event.Kind == HistoryKind.HeldBack));
        await history.SubmitAsync();

        Assert.Equal(
            [ConclusionChoice.Keep, ConclusionChoice.Release],
            rig.Editor.HeldDecisions.Select(decision => decision.Choice));
    }

    /// <summary>The held back ring marks the node with the conclusion's addresses, and hovering tells why.</summary>
    [Fact]
    public async Task TheHeldBackRingMarksTheClusterThatWaits()
    {
        var rig = new Rig();
        rig.Editor.HeldBack.Add(Held());
        await rig.Graph.RefreshAsync();
        await Task.Yield();

        Assert.NotNull(rig.Graph.HeldBackOf(rig.Graph.Scene!.Node(GraphSamples.KKey)!));
        Assert.Null(rig.Graph.HeldBackOf(rig.Graph.Scene.Node(GraphSamples.UKey)!));
        var lines = GraphViewModel.HeldBackLines(Held());
        Assert.Equal(GraphViewModel.HeldBackTitle, lines[0]);
        Assert.Contains(GraphScene.Shorten(GraphSamples.Tx[0].Value), lines[2], StringComparison.Ordinal);
        rig.Graph.OpenHeldBack(GraphSamples.KKey);
        Assert.Equal(GraphSamples.KKey, rig.Graph.SelectedNode);
        Assert.True(rig.Graph.History.IsShown);
    }

    /// <summary>
    /// A gone transaction's own conclusions, which the expansions now hold back on the transaction itself, name it as
    /// such in the hover and in Historik; other missing transactions are named as before.
    /// </summary>
    [Fact]
    public void AGoneTransactionsOwnConclusionNamesItselfAsWhatItWasBuiltOn()
    {
        var own = Held() with { Missing = [GraphSamples.Tx[2], GraphSamples.Tx[0]] };

        var lines = GraphViewModel.HeldBackLines(own);

        var itself = $"{HeldBackMatch.ItselfText} ({GraphScene.Shorten(GraphSamples.Tx[2].Value)})";
        Assert.Equal(
            $"Byggde på {itself}, {GraphScene.Shorten(GraphSamples.Tx[0].Value)}",
            lines[2]);
        Assert.Equal(
            GraphScene.Shorten(GraphSamples.Tx[0].Value),
            HeldBackMatch.Basis(GraphSamples.Tx[2], [GraphSamples.Tx[0]]));
    }

    /// <summary>The way between a pair is the steps with their addresses, and the scissors go on the weakest.</summary>
    [Fact]
    public void ThePairsWayHasTheScissorsOnTheWeakestLink()
    {
        var cluster = Chain();
        var steps = PairPath.Steps(GraphSamples.K1, K3, cluster.Links);

        Assert.Equal([GraphSamples.K1, GraphSamples.K2], steps.Select(step => step.From));
        Assert.Equal([GraphSamples.K2, K3], steps.Select(step => step.To));
        Assert.Equal(1, PairPath.Weakest(steps));
        Assert.Equal([K3], PairPath.Cut(cluster, GraphSamples.K1, K3, steps[1].Link).Leaving);
        Assert.Equal([GraphSamples.K2, K3], PairPath.Cut(cluster, GraphSamples.K1, K3, steps[0].Link).Leaving);
    }

    /// <summary>
    /// Ctrl+click on a second address shows the way in the form Bryt upp; Bryt upp cuts the chosen link only.
    /// </summary>
    [Fact]
    public async Task BreakingUpAPairCutsTheChosenLink()
    {
        var rig = new Rig();
        var cluster = Chain();
        rig.Editor.Cluster = cluster;
        rig.Editor.Path = cluster.Links;
        rig.Graph.Choose(GraphSamples.KKey, null);
        rig.Forms.Open(InspectorForm.Split);
        await Task.Yield();
        rig.Forms.ToggleMember(rig.Forms.Members.Single(member => member.Address == GraphSamples.K1));

        await rig.Forms.PickPairAsync(rig.Forms.Members.Single(member => member.Address == K3));

        Assert.True(rig.Forms.IsPairMode);
        Assert.False(rig.Forms.IsApart);
        Assert.Equal(2, rig.Forms.PathLinks.Count);
        Assert.True(rig.Forms.PathLinks[1].IsCut);
        Assert.True(rig.Forms.PathLinks[1].IsWeakest);
        Assert.Equal("Bryt upp", rig.Forms.SubmitText);
        Assert.Contains("blir en egen nod", rig.Forms.PairResult, StringComparison.Ordinal);
        rig.Forms.ChooseCut(rig.Forms.PathLinks[0]);
        Assert.Contains("egen plånbok", rig.Forms.PairResult, StringComparison.Ordinal);
        rig.Forms.ChooseCut(rig.Forms.PathLinks[1]);
        await rig.Forms.SubmitAsync();

        var split = Assert.Single(rig.Editor.Splits);
        Assert.Equal((K3, GraphSamples.K2, HeuristicNames.ChangeAddress), (split.First, split.Second, split.Heuristic));
    }

    /// <summary>Two addresses in different clusters give the warning with only Stäng.</summary>
    [Fact]
    public async Task APairInTwoClustersIsAWarningWithOnlyClose()
    {
        var rig = new Rig();
        rig.Graph.Choose(GraphSamples.KKey, null);
        rig.Forms.Open(InspectorForm.Split);

        await rig.Forms.ShowPairAsync(GraphSamples.K1, GraphSamples.U);

        Assert.True(rig.Forms.IsApart);
        Assert.False(rig.Forms.CanSubmit);
        Assert.Equal("Stäng", rig.Forms.CancelText);
        rig.Forms.LeavePair();
        Assert.False(rig.Forms.IsPairMode);
        Assert.Equal("Avbryt", rig.Forms.CancelText);
    }

    /// <summary>Dust and a look-alike are toned down; the look-alike is compared character by character.</summary>
    [Fact]
    public void DustAndPoisoningAreTonedDownAndCompared()
    {
        var scene = GraphSamples.Scene();
        var checks = new[]
        {
            Check("dust", "dust:" + GraphSamples.Tx[0].Value, [GraphSamples.W]),
            Check("dust", "poison:" + GraphSamples.U.Value, [GraphSamples.U, GraphSamples.E]),
        };

        var dusted = ObfuscationMarks.Dusted(scene, checks);
        var poisonings = ObfuscationMarks.Poisonings(scene, checks);

        Assert.Equal([GraphSamples.UKey, GraphSamples.WKey], dusted.Order(StringComparer.Ordinal));
        var poison = Assert.Single(poisonings).Value;
        Assert.Equal((GraphSamples.U, GraphSamples.E), (poison.Suspect, poison.Genuine));
        var pairs = ObfuscationMarks.Compare("TQ5nAAmK8e", "TQ5nX7mK8e");
        Assert.Equal([false, false, false, false, true, true, false, false, false, false], pairs.Select(p => p.Differs));
    }

    /// <summary>Copying a look-alike asks first: nothing is copied until the genuine one or the look-alike is chosen.</summary>
    [Fact]
    public async Task CopyingALookAlikeAsksFirst()
    {
        var rig = new Rig(Check("dust", "poison:" + GraphSamples.U.Value, [GraphSamples.U, GraphSamples.E]));
        await rig.Graph.Checks.FollowAsync(rig.Graph.Scene);
        var copied = new List<string>();
        rig.Graph.CopyWanted += (_, text) => copied.Add(text);
        rig.Graph.Choose(GraphSamples.UKey, null);

        rig.Graph.CopyAddress();

        Assert.Empty(copied);
        Assert.True(rig.Graph.IsPoisonAsked);
        Assert.Equal("Kopiera " + GraphScene.Shorten(GraphSamples.E.Value), rig.Graph.PoisonCopyText);
        rig.Graph.CopyGenuine();
        Assert.Equal([GraphSamples.E.Value], copied);
        Assert.False(rig.Graph.IsPoisonAsked);
    }

    /// <summary>A row of many hops folds the nodes between into one line, with a bead per hop after time.</summary>
    [Fact]
    public void ManyHopsFoldIntoOneLineWithBeadsAfterTime()
    {
        var scene = GraphSamples.Scene();
        var start = DateTimeOffset.UnixEpoch;
        var check = Check(
            "pass-through",
            GraphSamples.Tx[0].Value,
            [GraphSamples.K1, GraphSamples.E, GraphSamples.U],
            [
                new CheckBasis(GraphSamples.Tx[0], new BlockReference(1, GraphSamples.Block(1).Hash, start)),
                new CheckBasis(GraphSamples.Tx[1], new BlockReference(2, GraphSamples.Block(2).Hash, start.AddDays(3))),
                new CheckBasis(GraphSamples.Tx[2], new BlockReference(3, GraphSamples.Block(3).Hash, start.AddDays(3).AddHours(4))),
            ]);

        var row = Assert.Single(ObfuscationMarks.HopRows(scene, [check]));

        Assert.Equal((GraphSamples.KKey, GraphSamples.UKey), (row.First, row.Last));
        Assert.Equal([GraphSamples.EKey], row.Between);
        Assert.Equal("3 hopp · 3 d 4 h", row.Text);
        Assert.True(row.Beads[1] - row.Beads[0] > row.Beads[2] - row.Beads[1]);
        Assert.Contains("väntade 3 d 0 h", row.Lines[1], StringComparison.Ordinal);
        Assert.Equal("5 h 12 min", ObfuscationMarks.Span(TimeSpan.FromMinutes(312)));
    }

    /// <summary>
    /// A likely service opens the label form at the node with the check as its source; the node shows the label
    /// while it is chosen, and the label is stored as a judgement from the check.
    /// </summary>
    [Fact]
    public async Task ALikelyServiceBecomesALabelFromTheCheck()
    {
        var rig = new Rig();
        var check = Check("service-behaviour", "sweep:" + GraphSamples.U.Value, [GraphSamples.U, GraphSamples.E]);
        Assert.Same(check, GraphViewModel.ServiceCheckOf([check], GraphSamples.Scene().Node(GraphSamples.UKey)));
        Assert.Null(GraphViewModel.ServiceCheckOf([check], GraphSamples.Scene().Node(GraphSamples.WKey)));
        rig.Graph.Choose(GraphSamples.UKey, null);

        rig.Forms.OpenLabelFromCheck(check);

        Assert.True(rig.Forms.IsFromCheck);
        Assert.Equal("Bedömning · service-behaviour v1", rig.Forms.CheckSourceText);
        Assert.Equal(LabelKind.Exchange, rig.Forms.Kind);
        rig.Forms.LabelName = "Sydbörsen";
        var shown = rig.Forms.Previewed(rig.Graph.Scene!.Node(GraphSamples.UKey)!);
        Assert.Equal("Sydbörsen", shown.Name);
        Assert.True(shown.Look.IsDashed);
        Assert.Same(rig.Graph.Scene.Node(GraphSamples.WKey), rig.Forms.Previewed(rig.Graph.Scene.Node(GraphSamples.WKey)!));
        await rig.Forms.SubmitAsync();

        var label = Assert.Single(rig.Editor.Labels);
        Assert.Equal((LabelSource.Behaviour, "service-behaviour", "v1"), (label.Source, label.SourceName, label.SourceVersion));
        Assert.Equal("Sydbörsen", label.Name);
        Assert.False(rig.Forms.IsFromCheck);
    }

    /// <summary>
    /// A cut refuses the whole proposal, as the clustering will: common inputs a, b, c with a sign of change c – d,
    /// cut at the common inputs, leaves b on its own and c with d, not a with b.
    /// </summary>
    [Fact]
    public void ACutRefusesTheWholeProposalAsTheClusteringWill()
    {
        var (a, b, c, d) = (GraphSamples.K1, GraphSamples.K2, K3, GraphSamples.Address(32));
        var common = Link(1, new HeuristicId(HeuristicNames.CommonInputOwnership, 1), a, b, c);
        var change = Link(2, new HeuristicId(HeuristicNames.ChangeAddress, 2), c, d);
        var cluster = new Cluster(
            a,
            [
                new ClusterMember(a, null, null), new ClusterMember(b, common, a), new ClusterMember(c, common, a),
                new ClusterMember(d, change, c),
            ],
            [common, change],
            [],
            [],
            []);

        var cut = PairPath.Cut(cluster, a, d, common);

        Assert.Equal([b, c, d], cut.Leaving);
        Assert.Equal(2, cut.Groups.Count);
        Assert.Equal([b], cut.Groups[0]);
        Assert.Equal([c, d], cut.Groups[1]);
        Assert.Empty(PairPath.Cut(cluster with { Links = [common, change, Link(3, common.Evidence.Heuristic, a, d)] },
            a, d, common).Leaving);
    }

    /// <summary>A label from an analysis the user approved is the user's own: it can be taken back in Historik.</summary>
    [Fact]
    public void AnApprovedBehaviourLabelCanBeTakenBackInTheHistory()
    {
        var scene = GraphSamples.Scene();
        var label = GraphSamples.Label(GraphSamples.U, LabelKind.Exchange, "Sydbörsen", LabelSource.Behaviour);
        var data = new NodeHistoryData(
            [new LabelHistoryEntry(label.Entries[0], null)], [], [], null, new Dictionary<long, string>());

        var row = Assert.Single(
            NodeHistory.Build(scene, scene.Node(GraphSamples.UKey)!, data), r => r.Kind == HistoryKind.Label);

        Assert.Equal(HistoryUndo.RetractLabel, row.Undo);
        Assert.True(row.CanUndo);
    }

    /// <summary>The look-alike cannot be copied from the inspector's ids either, and its ids are not shown.</summary>
    [Fact]
    public async Task TheLookAlikeCannotBeCopiedFromTheIds()
    {
        var rig = new Rig(Check("dust", "poison:" + GraphSamples.U.Value, [GraphSamples.U, GraphSamples.E]));
        await rig.Graph.Checks.FollowAsync(rig.Graph.Scene);
        var copied = new List<string>();
        rig.Graph.CopyWanted += (_, text) => copied.Add(text);
        rig.Graph.Choose(GraphSamples.UKey, null);

        rig.Graph.CopyTextCommand.Execute(GraphSamples.U.Value);

        Assert.Empty(copied);
        Assert.True(rig.Graph.IsPoisonAsked);
        Assert.False(rig.Graph.ShowsIds);
        rig.Graph.CopyTextCommand.Execute(GraphSamples.E.Value);
        Assert.Equal([GraphSamples.E.Value], copied);
    }

    /// <summary>The confirmation says what the preview cannot know, and a withdrawal only drawn back by one stays.</summary>
    [Fact]
    public async Task TheConfirmationSaysWhatThePreviewCannotKnow()
    {
        var rig = new Rig();
        rig.Editor.Cluster = Chain();
        rig.Editor.WithdrawPreview = new WithdrawPreview(
            new HeuristicId(HeuristicNames.ChangeAddress, 2), new HeuristicId(HeuristicNames.ChangeAddress, 1), []);
        rig.Graph.Choose(GraphSamples.KKey, null);
        var why = rig.Graph.ClusterWhy;

        await why.AskWithdrawAsync(why.Rows.Single(r => r.Name == "change-address v2"));

        Assert.True(why.HasPreview);
        Assert.Contains("change-address v1", why.UnknownNote, StringComparison.Ordinal);
        var withdrawnFirst = new VersionWithdrawal(
            1, new HeuristicId(HeuristicNames.ChangeAddress, 1), null, 4, DateTimeOffset.UnixEpoch);
        Assert.False(ClusterWhyViewModel.RowsOf(Chain(), [withdrawnFirst])
            .Single(r => r.Name == "change-address v2").CanWithdraw);
    }

    /// <summary>A release is taken back in Historik with Ångra, as a new sealed decision.</summary>
    [Fact]
    public async Task AReleaseIsTakenBackInTheHistory()
    {
        var rig = new Rig();
        var released = new HeldBackDecision(
            7, GraphSamples.Tx[1], [GraphSamples.Tx[0]], [GraphSamples.K1], ConclusionChoice.Release, null, 9,
            DateTimeOffset.UnixEpoch);
        rig.Editor.History = new NodeHistoryData([], [], [], null, new Dictionary<long, string>())
        {
            HeldBackDecisions = [released],
        };
        rig.Graph.Choose(GraphSamples.KKey, null);
        var history = rig.Graph.History;
        history.ShowHistory();
        var row = history.Rows.Single(r => r.Event.Undo == HistoryUndo.ReopenHeldBack);

        Assert.Equal(NodeHistory.UndoText, row.Event.UndoText);
        history.Toggle(row);
        await history.SubmitAsync();

        Assert.Equal([7L], rig.Editor.Reopened);
    }

    /// <summary>A node on the row with transactions of its own beside the row is not folded away.</summary>
    [Fact]
    public void ANodeWithOtherLinesIsNotFoldedAway()
    {
        var scene = GraphSamples.Scene();
        var check = Check("pass-through", GraphSamples.Tx[0].Value, [GraphSamples.W, GraphSamples.V, GraphSamples.K1]);

        Assert.Empty(ObfuscationMarks.HopRows(scene, [check]));
    }

    /// <summary>Gives a cluster K1 – K2 – K3: common inputs, then a sign of change.</summary>
    /// <returns>The cluster.</returns>
    private static Cluster Chain()
    {
        var common = Link(1, new HeuristicId(HeuristicNames.CommonInputOwnership, 1), GraphSamples.K1, GraphSamples.K2);
        var change = Link(2, new HeuristicId(HeuristicNames.ChangeAddress, 2), GraphSamples.K2, K3);
        return new Cluster(
            GraphSamples.K1,
            [
                new ClusterMember(GraphSamples.K1, null, null),
                new ClusterMember(GraphSamples.K2, common, GraphSamples.K1),
                new ClusterMember(K3, change, GraphSamples.K2),
            ],
            [common, change],
            [],
            [],
            []);
    }

    /// <summary>Gives a merged link.</summary>
    /// <param name="sequence">The evidence's number.</param>
    /// <param name="heuristic">The heuristic.</param>
    /// <param name="addresses">The addresses.</param>
    /// <returns>The link.</returns>
    private static ClusterLink Link(long sequence, HeuristicId heuristic, params BitcoinAddress[] addresses) => new(
        new ClusterEvidence(
            sequence, heuristic, GraphSamples.Tx[2], addresses, EvidenceStrength.Merge, "samma ägare", null, null,
            sequence, DateTimeOffset.UnixEpoch),
        LinkState.Merged);

    /// <summary>Gives a conclusion held back on the sample cluster's addresses.</summary>
    /// <returns>The conclusion.</returns>
    private static HeldBackConclusion Held() => new(
        GraphSamples.Tx[2],
        [Link(5, new HeuristicId(HeuristicNames.CommonInputOwnership, 1), GraphSamples.K1, GraphSamples.K2).Evidence],
        [GraphSamples.Tx[0]],
        null);

    /// <summary>Gives a finding of a check.</summary>
    /// <param name="analysis">The analysis's name.</param>
    /// <param name="key">The finding's key.</param>
    /// <param name="addresses">Its addresses.</param>
    /// <param name="basis">Its transactions, or none.</param>
    /// <returns>The stored check.</returns>
    private static StoredCheck Check(
        string analysis, string key, IReadOnlyList<BitcoinAddress> addresses, IReadOnlyList<CheckBasis>? basis = null) =>
        new(
            1,
            new HeuristicId(analysis, 1),
            analysis,
            new CheckFinding(key, CheckKind.Check, AreaSort.Obfuscation, "Titel", "Sammanfattning", "Varför", addresses, [],
                basis ?? [], []),
            1,
            DateTimeOffset.UnixEpoch,
            []);

    /// <summary>A graph with its forms, history and editor, on an open case.</summary>
    private sealed class Rig
    {
        /// <summary>Creates the rig with the sample graph read.</summary>
        /// <param name="checks">The checks a run gives; none by default.</param>
        public Rig(params StoredCheck[] checks)
        {
            Checks.Checks.Clear();
            Checks.Checks.AddRange(checks);
            Workflow = new CaseWorkflow(
                new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), Session);
            Graph = new GraphViewModel(
                Session, Workflow, Source, TestWays.For(new TestExitConnection()), new InlineUi(), Editor,
                new TestClock(), Checks);
            Session.Open(new TestCase("2026-0001"));
        }

        /// <summary>Gets the session.</summary>
        public CaseSession Session { get; } = new();

        /// <summary>Gets the workflow.</summary>
        public CaseWorkflow Workflow { get; }

        /// <summary>Gets the graph's source.</summary>
        public TestGraphSource Source { get; } = new();

        /// <summary>Gets the checks.</summary>
        public TestCheckSource Checks { get; } = new();

        /// <summary>Gets the editor.</summary>
        public TestGraphEditor Editor { get; } = new();

        /// <summary>Gets the graph.</summary>
        public GraphViewModel Graph { get; }

        /// <summary>Gets the forms.</summary>
        public InspectorFormsViewModel Forms => Graph.Forms;
    }
}
