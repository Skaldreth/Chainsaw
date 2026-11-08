using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>A comment (sketch KN1a) on a node in the trash (sketch SO1a) is hidden with it and comes back with it.</summary>
public sealed class CommentTrashTests
{
    private static TrashThrow ThrowOf(params TrashMember[] members) =>
        new(1, members, new OperatorProfile("Testa", "Utredare", "Enheten"), DateTimeOffset.UnixEpoch, 1);

    [Fact]
    public void A_thread_to_a_thrown_node_is_known_by_its_key_or_its_address()
    {
        var scene = Scene().WithTrash(new TrashFilter([ThrowOf(
            new TrashMember(UKey, null, "U", "Unknown") { Keys = [GraphPile.MemberKey(U)] })]));

        Assert.True(CommentGeometry.IsThrown(scene, UKey));
        Assert.True(CommentGeometry.IsThrown(scene, GraphPile.MemberKey(U)));
        Assert.False(CommentGeometry.IsThrown(scene, VKey));
        Assert.False(CommentGeometry.IsThrown(scene, null));
        Assert.False(CommentGeometry.IsThrown(Scene(), UKey));
    }

    [Fact]
    public async Task A_note_on_a_thrown_node_is_hidden_and_shown_again_when_it_is_put_back()
    {
        var session = new CaseSession();
        var comments = new TestGraphComments();
        comments.Seed("Om U", GraphPile.MemberKey(U));
        var trash = new TestTrashSource();
        var workflow = new CaseWorkflow(new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), session);
        var graph = new GraphViewModel(
            session, workflow, new TestGraphSource(), TestWays.For(new TestExitConnection()), new InlineUi(),
            new TestGraphEditor(), new TestClock(), comments: comments, trash: trash);
        session.Open(new TestCase("2026-0042"));
        await graph.ReloadAsync(fit: false);
        await graph.Comments.LoadAsync();
        var note = Assert.Single(graph.Comments.Notes);
        Assert.False(note.IsInTrash);

        graph.AskThrow([UKey]);
        await Task.Yield();

        Assert.True(note.IsInTrash);
        Assert.Null(note.AnchorNode);

        await graph.UndoThrowAsync();

        Assert.False(note.IsInTrash);
        Assert.Equal(UKey, note.AnchorNode);
    }
}
