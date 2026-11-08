using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// The graph's comments (sketch KN1a): what a comment says, its first place, its thread and its removal sealed with who
/// and when, each change a new record with a line in the journal; its later places and size ordinary data.
/// </summary>
public sealed class CommentTests : IDisposable
{
    private const string Anchor = "address:bc1qexampleexampleexampleexample";

    private readonly CaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private IGraphCommentService Comments => _harness.Comments;

    private GraphComment Add(string text = "## Uttag via Kraken?\nBeloppet stämmer.", string? anchor = Anchor) =>
        Comments.AddComment(_harness.Case, new GraphCommentDraft(text, anchor, new NodePosition(760, 60)));

    [Fact]
    public void A_comment_is_sealed_with_who_and_when_and_a_line_in_the_journal()
    {
        var comment = Add("  ## Uttag via Kraken?\r\nBeloppet stämmer.\r\n\r\n");

        Assert.Equal("## Uttag via Kraken?\nBeloppet stämmer.", comment.Text);
        Assert.Equal(Anchor, comment.Anchor);
        Assert.Equal("Testa Testsson", comment.CreatedBy.Name);
        Assert.Equal(comment.CreatedBy, comment.ChangedBy);
        var journal = _harness.Case.ReadJournal().Last();
        Assert.Equal(TracingJournalKinds.CommentAdded, journal.Kind);
        Assert.Equal("Kommentar skriven: Uttag via Kraken?", journal.Summary);
        Assert.Equal(journal.Utc, comment.CreatedUtc);
        Assert.Equal(comment, Assert.Single(Comments.GetComments(_harness.Case)));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_comment_is_kept_when_the_case_is_opened_again()
    {
        var comment = Add();

        _harness.Reopen();

        var read = Assert.Single(Comments.GetComments(_harness.Case));
        Assert.Equal(comment, read);
        Assert.Equal(new NodePosition(760, 60), read.Place);
        Assert.Null(read.Size);
    }

    [Fact]
    public void An_edit_is_a_new_record_and_the_old_text_stays_in_the_history()
    {
        var comment = Add("Först");
        _harness.Profiles.Profile = new("Kollega Kollegasson", "Analytiker", "Enheten");

        var edited = Comments.EditComment(_harness.Case, comment.Id, "Sedan");

        Assert.NotNull(edited);
        Assert.Equal("Sedan", edited.Text);
        Assert.Equal("Testa Testsson", edited.CreatedBy.Name);
        Assert.Equal("Kollega Kollegasson", edited.ChangedBy.Name);
        var history = Comments.GetCommentHistory(_harness.Case, comment.Id);
        Assert.Equal([CommentAction.Added, CommentAction.Edited], history.Select(record => record.Action));
        Assert.Equal(["Först", "Sedan"], history.Select(record => record.Text));
        Assert.Equal(TracingJournalKinds.CommentEdited, _harness.Case.ReadJournal().Last().Kind);
        Assert.Equal(_harness.Case.ReadJournal().Last().Sequence, history[^1].JournalSequence);
        Assert.Equal("Sedan", Assert.Single(Comments.GetComments(_harness.Case)).Text);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void The_same_text_writes_nothing()
    {
        var comment = Add("Samma");
        var before = _harness.Case.ReadJournal().Count;

        var edited = Comments.EditComment(_harness.Case, comment.Id, "Samma\n");

        Assert.Equal(comment, edited);
        Assert.Equal(before, _harness.Case.ReadJournal().Count);
        Assert.Single(Comments.GetCommentHistory(_harness.Case, comment.Id));
    }

    [Fact]
    public void A_thread_is_tied_anew_and_untied_as_new_records()
    {
        var comment = Add();

        var moved = Comments.AnchorComment(_harness.Case, comment.Id, "tx:" + new string('a', 64));
        var untied = Comments.AnchorComment(_harness.Case, comment.Id, null);

        Assert.Equal("tx:" + new string('a', 64), moved!.Anchor);
        Assert.Null(untied!.Anchor);
        Assert.Null(Assert.Single(Comments.GetComments(_harness.Case)).Anchor);
        var history = Comments.GetCommentHistory(_harness.Case, comment.Id);
        Assert.Equal(
            [CommentAction.Added, CommentAction.Anchored, CommentAction.Anchored],
            history.Select(record => record.Action));
        Assert.Equal(TracingJournalKinds.CommentAnchored, _harness.Case.ReadJournal().Last().Kind);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_removed_comment_is_gone_but_its_history_stays()
    {
        var comment = Add("Bort");

        Assert.True(Comments.RemoveComment(_harness.Case, comment.Id));
        Assert.False(Comments.RemoveComment(_harness.Case, comment.Id));

        Assert.Empty(Comments.GetComments(_harness.Case));
        var history = Comments.GetCommentHistory(_harness.Case, comment.Id);
        Assert.Equal([CommentAction.Added, CommentAction.Removed], history.Select(record => record.Action));
        Assert.Equal("Bort", history[^1].Text);
        Assert.Null(Comments.EditComment(_harness.Case, comment.Id, "Igen"));
        Assert.Null(Comments.AnchorComment(_harness.Case, comment.Id, null));
        Assert.False(Comments.SaveCommentPlace(_harness.Case, comment.Id, new NodePosition(1, 2), null));
        Assert.Equal(TracingJournalKinds.CommentRemoved, _harness.Case.ReadJournal().Last().Kind);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_place_and_a_size_are_ordinary_data_without_a_journal()
    {
        var comment = Add();
        var before = _harness.Case.ReadJournal().Count;

        Assert.True(Comments.SaveCommentPlace(
            _harness.Case, comment.Id, new NodePosition(100, 200), new CommentSize(9_000, 50)));
        _harness.Reopen();

        var read = Assert.Single(Comments.GetComments(_harness.Case));
        Assert.Equal(new NodePosition(100, 200), read.Place);
        Assert.Equal(new CommentSize(CommentRules.MaximumWidth, CommentRules.MinimumHeight), read.Size);
        Assert.Equal(before, _harness.Case.ReadJournal().Count);
        Assert.Single(Comments.GetCommentHistory(_harness.Case, comment.Id));

        Assert.True(Comments.SaveCommentPlace(_harness.Case, comment.Id, new NodePosition(5, 6), null));
        Assert.Null(Assert.Single(Comments.GetComments(_harness.Case)).Size);
    }

    [Fact]
    public void The_sealed_records_cannot_be_changed_afterwards()
    {
        Add();

        Assert.ThrowsAny<Exception>(() => _harness.Case.Write(transaction =>
        {
            using var command = transaction.CreateCommand("UPDATE tracing_comments SET body = 'Ändrad'");
            return command.ExecuteNonQuery();
        }));
        Assert.ThrowsAny<Exception>(() => _harness.Case.Write(transaction =>
        {
            using var command = transaction.CreateCommand("DELETE FROM tracing_comments");
            return command.ExecuteNonQuery();
        }));
        Assert.Single(Comments.GetComments(_harness.Case));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n\t ")]
    public void An_empty_comment_is_refused(string text)
    {
        Assert.Throws<ArgumentException>(() => Add(text));
        var comment = Add();
        Assert.Throws<ArgumentException>(() => Comments.EditComment(_harness.Case, comment.Id, text));
        Assert.Single(Comments.GetCommentHistory(_harness.Case, comment.Id));
    }

    [Fact]
    public void A_text_that_is_too_long_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Add(new string('x', CommentRules.MaximumTextLength + 1)));
        Assert.Empty(Comments.GetComments(_harness.Case));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("address:\nbc1q")]
    public void A_thread_that_is_not_a_node_key_is_refused(string anchor)
    {
        Assert.Throws<ArgumentException>(() => Add(anchor: anchor));
        var comment = Add();
        Assert.Throws<ArgumentException>(() => Comments.AnchorComment(_harness.Case, comment.Id, anchor));
    }

    [Fact]
    public void A_place_that_is_not_finite_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Comments.AddComment(
            _harness.Case, new GraphCommentDraft("Text", null, new NodePosition(double.NaN, 0))));
        var comment = Add();
        Assert.Throws<ArgumentException>(() => Comments.SaveCommentPlace(
            _harness.Case, comment.Id, new NodePosition(0, double.PositiveInfinity), null));
        Assert.Throws<ArgumentException>(() => Comments.SaveCommentPlace(
            _harness.Case, comment.Id, new NodePosition(0, 0), new CommentSize(double.NaN, 1)));
    }

    [Fact]
    public void Comments_are_read_oldest_first_and_an_unknown_one_has_no_history()
    {
        var first = Add("Ett");
        var second = Add("Två", null);

        Assert.Equal([first.Id, second.Id], Comments.GetComments(_harness.Case).Select(comment => comment.Id));
        Assert.Empty(Comments.GetCommentHistory(_harness.Case, "okänd"));
    }

    [Theory]
    [InlineData("## Uttag via Kraken?\nmer", "Uttag via Kraken?")]
    [InlineData("\n\n- punkt", "punkt")]
    [InlineData("", "")]
    public void The_title_is_the_first_line_without_its_marks(string text, string title)
    {
        Assert.Equal(title, CommentRules.Title(text));
        Assert.Equal(10, CommentRules.Title(new string('a', 40), 10).Length);
        Assert.EndsWith("…", CommentRules.Title(new string('a', 40), 10), StringComparison.Ordinal);
    }
}
