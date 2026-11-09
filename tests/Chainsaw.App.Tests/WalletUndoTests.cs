using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Security.Contracts;
using static Chainsaw.App.Tests.CheckSamples;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>
/// Taking back an approved wallet, confirmed in the graph (sketch PL1b): Ctrl+Z, the toast's or the log's
/// <em>Ångra</em> opens the cluster as a card in the graph, and only Enter there seals the take-back.
/// </summary>
public sealed class WalletUndoTests
{
    /// <summary>A case open, the checks with a wallet to approve and the graph.</summary>
    private sealed class Rig
    {
        public Rig()
        {
            Session.Open(Case);
            Source.Checks.Clear();
            Source.Checks.Add(Wallet());
            Checks = new ChecksViewModel(Session, Source) { TimeZone = TimeZoneInfo.Utc };
            Checks.ToastWanted += (_, toast) => Toasts.Add(toast);
        }

        public CaseSession Session { get; } = new();

        public TestCase Case { get; } = new("2026-0031");

        public TestCheckSource Source { get; } = new();

        public ChecksViewModel Checks { get; }

        public List<ToastRequest> Toasts { get; } = [];

        /// <summary>Runs the checks on the graph, opens the panel and approves the first suggestion.</summary>
        public async Task<Rig> ApprovedAsync()
        {
            await Checks.FollowAsync(Scene());
            Checks.Toggle();
            Checks.StartReview(null);
            await Checks.ApproveAsync();
            return this;
        }
    }

    [Fact]
    public async Task Ctrl_Z_on_an_approved_wallet_asks_in_the_graph_and_seals_nothing_yet()
    {
        var rig = await new Rig().ApprovedAsync();
        WalletUndo? wanted = null;
        rig.Checks.WalletUndoWanted += (_, undo) => wanted = undo;

        await rig.Checks.UndoAsync();

        Assert.Single(rig.Source.Decisions);
        Assert.True(rig.Checks.IsConfirmingInGraph);
        Assert.Same(rig.Checks.PendingWalletUndo, wanted);
        Assert.Equal([W.Value, V.Value], wanted!.Addresses.Select(address => address.Full));
        Assert.Equal("2 adresser", wanted.CountText);
        Assert.Equal([WKey, VKey], wanted.Nodes);
    }

    [Fact]
    public async Task Enter_in_the_graph_takes_the_approval_back_sealed_with_the_optional_reason()
    {
        var rig = await new Rig().ApprovedAsync();
        var changed = 0;
        rig.Checks.ClustersChanged += (_, _) => changed++;
        await rig.Checks.UndoAsync();
        rig.Checks.ToggleUndoReason();
        rig.Checks.UndoReason = "  Fel inloggning  ";

        await rig.Checks.ConfirmWalletUndoAsync();

        var undo = rig.Source.Decisions[1];
        Assert.Equal(CheckVerdict.Withdrawn, undo.Verdict);
        Assert.Equal(1, undo.Undoes);
        Assert.Equal("Fel inloggning", undo.Reason);
        Assert.Equal(1, changed);
        Assert.False(rig.Checks.IsConfirmingInGraph);
        Assert.False(rig.Checks.IsUndoReasonOpen);
        Assert.Equal(ChecksViewModel.UndoneText, rig.Toasts[^1].Title);
        Assert.Equal(1, rig.Checks.WaitingCount);
    }

    [Fact]
    public async Task Without_a_reason_the_take_back_has_none()
    {
        var rig = await new Rig().ApprovedAsync();
        await rig.Checks.UndoAsync();

        await rig.Checks.ConfirmWalletUndoAsync();

        Assert.Null(rig.Source.Decisions[1].Reason);
    }

    [Fact]
    public async Task Esc_leaves_the_wallet_approved_and_writes_nothing()
    {
        var rig = await new Rig().ApprovedAsync();
        await rig.Checks.UndoAsync();

        rig.Checks.CancelWalletUndo();
        await rig.Checks.ConfirmWalletUndoAsync();

        Assert.Single(rig.Source.Decisions);
        Assert.False(rig.Checks.IsConfirmingInGraph);
        Assert.True(rig.Checks.CanUndoLast);
    }

    [Fact]
    public async Task The_toasts_undo_and_the_logs_undo_also_ask_in_the_graph()
    {
        var rig = await new Rig().ApprovedAsync();

        rig.Toasts[0].Action!();
        await Task.Yield();
        Assert.True(rig.Checks.IsConfirmingInGraph);
        rig.Checks.CancelWalletUndo();
        await rig.Checks.UndoRowAsync(rig.Checks.Decided.Single());

        Assert.True(rig.Checks.IsConfirmingInGraph);
        Assert.Single(rig.Source.Decisions);
    }

    [Fact]
    public async Task Enter_and_X_in_the_panel_wait_while_the_graph_asks()
    {
        var rig = new Rig();
        rig.Source.Checks.Add(Mixer());
        await rig.ApprovedAsync();
        await rig.Checks.UndoAsync();

        await rig.Checks.ApproveAsync();
        await rig.Checks.DismissAsync();

        Assert.Single(rig.Source.Decisions);
        Assert.True(rig.Checks.IsConfirmingInGraph);
    }

    [Fact]
    public async Task Another_case_forgets_the_take_back_that_waited()
    {
        var rig = await new Rig().ApprovedAsync();
        await rig.Checks.UndoAsync();

        rig.Session.Open(new TestCase("2026-0032"));
        await rig.Checks.ConfirmWalletUndoAsync();

        Assert.False(rig.Checks.IsConfirmingInGraph);
        Assert.Single(rig.Source.Decisions);
    }

    [Fact]
    public async Task Another_suggestions_undo_is_taken_back_at_once()
    {
        var rig = new Rig();
        rig.Source.Checks.Clear();
        rig.Source.Checks.Add(Mixer());
        await rig.ApprovedAsync();

        await rig.Checks.UndoAsync();

        Assert.False(rig.Checks.IsConfirmingInGraph);
        Assert.Equal(CheckVerdict.Withdrawn, rig.Source.Decisions[^1].Verdict);
    }

    [Fact]
    public async Task The_graph_chooses_the_wallet_and_choosing_elsewhere_leaves_it_approved()
    {
        var session = new CaseSession();
        var source = new TestCheckSource();
        source.Checks.Clear();
        source.Checks.Add(Wallet());
        var workflow = new CaseWorkflow(
            new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), session);
        var graph = new GraphViewModel(
            session, workflow, new TestGraphSource(), TestWays.For(new TestExitConnection()), new InlineUi(),
            new TestGraphEditor(), new TestClock(), checks: source);
        IReadOnlyList<string>? shown = null;
        graph.ShowNodesWanted += (_, nodes) => shown = nodes;
        session.Open(new TestCase("2026-0033"));
        await graph.ReloadAsync(fit: false);
        graph.Checks.Toggle();
        graph.Checks.StartReview(null);
        await graph.Checks.ApproveAsync();

        await graph.Checks.UndoAsync();

        Assert.True(graph.Checks.IsConfirmingInGraph);
        Assert.Equal(WKey, graph.SelectedNode);
        Assert.Equal([WKey, VKey], shown);

        graph.Choose(VKey, null);
        Assert.True(graph.Checks.IsConfirmingInGraph);

        graph.Choose(UKey, null);
        Assert.False(graph.Checks.IsConfirmingInGraph);
        Assert.Single(source.Decisions);
    }
}
