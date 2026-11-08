using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// The graph's comments (sketch KN1a): every change of a comment's text, thread or existence is a new sealed record
/// with a line in the journal, in one transaction (principle 1); its later places and size are ordinary data.
/// </summary>
/// <param name="clock">The clock, for when a place was stored.</param>
internal sealed class GraphCommentService(IClock clock) : IGraphCommentService
{
    /// <inheritdoc/>
    public IReadOnlyList<GraphComment> GetComments(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(CommentStore.Read);
    }

    /// <inheritdoc/>
    public GraphComment AddComment(ICase @case, GraphCommentDraft draft)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(draft);
        var text = CheckedText(draft.Text, nameof(draft));
        var anchor = CheckedAnchor(draft.Anchor, nameof(draft));
        CheckPlace(draft.Place, nameof(draft));
        var id = Guid.NewGuid().ToString("N");
        return @case.Write(transaction =>
        {
            var journal = transaction.AppendJournal(new JournalDraft(
                TracingJournalKinds.CommentAdded,
                Summary("Kommentar skriven", text),
                Details(id, anchor, text)));
            var record = CommentStore.Seal(
                transaction, CommentStore.AddAction, id, text, anchor, draft.Place, journal);
            return new GraphComment(
                id, text, anchor, draft.Place, null, journal.Operator, journal.Utc, journal.Operator, journal.Utc,
                record.Sequence);
        });
    }

    /// <inheritdoc/>
    public GraphComment? EditComment(ICase @case, string id, string text)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var checkedText = CheckedText(text, nameof(text));
        return @case.Write(transaction =>
        {
            if (Find(transaction, id) is not { } comment)
            {
                return null;
            }

            if (comment.Text == checkedText)
            {
                return comment;
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                TracingJournalKinds.CommentEdited,
                Summary("Kommentaren ändrades", checkedText),
                Details(id, comment.Anchor, checkedText)));
            CommentStore.Seal(transaction, CommentStore.EditAction, id, checkedText, comment.Anchor, null, journal);
            return comment with { Text = checkedText, ChangedBy = journal.Operator, ChangedUtc = journal.Utc };
        });
    }

    /// <inheritdoc/>
    public GraphComment? AnchorComment(ICase @case, string id, string? anchor)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var checkedAnchor = CheckedAnchor(anchor, nameof(anchor));
        return @case.Write(transaction =>
        {
            if (Find(transaction, id) is not { } comment)
            {
                return null;
            }

            if (comment.Anchor == checkedAnchor)
            {
                return comment;
            }

            var summary = checkedAnchor is null
                ? Summary("Kommentarens tråd togs bort", comment.Text)
                : Summary("Kommentarens tråd knöts till en annan nod", comment.Text);
            var journal = transaction.AppendJournal(new JournalDraft(
                TracingJournalKinds.CommentAnchored, summary, Details(id, checkedAnchor, null)));
            CommentStore.Seal(
                transaction, CommentStore.AnchorAction, id, comment.Text, checkedAnchor, null, journal);
            return comment with { Anchor = checkedAnchor };
        });
    }

    /// <inheritdoc/>
    public bool RemoveComment(ICase @case, string id)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Write(transaction =>
        {
            if (Find(transaction, id) is not { } comment)
            {
                return false;
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                TracingJournalKinds.CommentRemoved,
                Summary("Kommentaren togs bort", comment.Text),
                Details(id, comment.Anchor, null)));
            CommentStore.Seal(
                transaction, CommentStore.RemoveAction, id, comment.Text, comment.Anchor, null, journal);
            return true;
        });
    }

    /// <inheritdoc/>
    public bool SaveCommentPlace(ICase @case, string id, NodePosition place, CommentSize? size)
    {
        ArgumentNullException.ThrowIfNull(@case);
        CheckPlace(place, nameof(place));
        if (size is { } wanted && (!double.IsFinite(wanted.Width) || !double.IsFinite(wanted.Height)))
        {
            throw new ArgumentException("A comment's size is finite.", nameof(size));
        }

        var clamped = size is { } given ? CommentRules.Clamp(given.Width, given.Height) : (CommentSize?)null;
        var utc = clock.UtcNow;
        return @case.Write(transaction =>
        {
            if (Find(transaction, id) is null)
            {
                return false;
            }

            CommentStore.SavePlace(transaction, id, place, clamped, utc);
            return true;
        });
    }

    /// <inheritdoc/>
    public IReadOnlyList<GraphCommentRecord> GetCommentHistory(ICase @case, string id)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(reader =>
            (IReadOnlyList<GraphCommentRecord>)
            [.. CommentStore.ReadRecords(reader).Where(stored => stored.Id == id).Select(stored => stored.Record)]);
    }

    /// <summary>Finds a comment in force.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="id">The comment.</param>
    /// <returns>The comment, or <see langword="null"/> when it is unknown or removed.</returns>
    private static GraphComment? Find(ICaseReader reader, string id) =>
        CommentStore.Read(reader).FirstOrDefault(comment => comment.Id == id);

    /// <summary>Checks a comment's text and gives it as it is stored.</summary>
    /// <param name="text">The text as written.</param>
    /// <param name="parameter">The parameter's name, for the exception.</param>
    /// <returns>The text to store.</returns>
    /// <exception cref="ArgumentException">The text is empty or too long.</exception>
    private static string CheckedText(string? text, string parameter)
    {
        var normalized = CommentRules.Normalize(text);
        if (normalized.Length == 0)
        {
            throw new ArgumentException("A comment says something.", parameter);
        }

        if (normalized.Length > CommentRules.MaximumTextLength)
        {
            throw new ArgumentException("The comment is too long.", parameter);
        }

        return normalized;
    }

    /// <summary>Checks a thread's node key.</summary>
    /// <param name="anchor">The key, or <see langword="null"/>.</param>
    /// <param name="parameter">The parameter's name, for the exception.</param>
    /// <returns>The key, or <see langword="null"/> for no thread.</returns>
    /// <exception cref="ArgumentException">The key is blank, too long or holds control characters.</exception>
    private static string? CheckedAnchor(string? anchor, string parameter)
    {
        if (anchor is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(anchor) || anchor.Length > CommentRules.MaximumAnchorLength
            || anchor.Any(char.IsControl))
        {
            throw new ArgumentException("A comment's thread goes to a node's key.", parameter);
        }

        return anchor;
    }

    /// <summary>Checks that a place is finite.</summary>
    /// <param name="place">The place.</param>
    /// <param name="parameter">The parameter's name, for the exception.</param>
    /// <exception cref="ArgumentException">The place is not finite.</exception>
    private static void CheckPlace(NodePosition place, string parameter)
    {
        if (!double.IsFinite(place.X) || !double.IsFinite(place.Y))
        {
            throw new ArgumentException("A comment's place is finite.", parameter);
        }
    }

    /// <summary>Gives a journal line: what happened, and the comment's first line.</summary>
    /// <param name="what">What happened.</param>
    /// <param name="text">The comment's text.</param>
    /// <returns>For example <c>Kommentar skriven: Uttag via Kraken?</c>.</returns>
    private static string Summary(string what, string text) =>
        string.Format(CultureInfo.InvariantCulture, "{0}: {1}", what, CommentRules.Title(text));

    /// <summary>Gives a journal entry's details.</summary>
    /// <param name="id">The comment.</param>
    /// <param name="anchor">Its thread.</param>
    /// <param name="text">Its new text, or <see langword="null"/> when the text did not change.</param>
    /// <returns>The details as JSON.</returns>
    private static string Details(string id, string? anchor, string? text) =>
        JsonSerializer.Serialize(new { comment = id, anchor, text });
}
