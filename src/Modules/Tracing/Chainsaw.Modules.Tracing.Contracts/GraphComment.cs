using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// A comment on the graph (sketch KN1a): a note on the surface written in Markdown, optionally tied by a thin dotted
/// thread to the node it is about. What it says, where it was first put, its thread and its removal are sealed in the
/// case with who and when, each change as a new record (principle 1); where it was moved to and the size it was dragged
/// to are ordinary data, like the nodes' places.
/// </summary>
/// <param name="Id">The comment's stable id.</param>
/// <param name="Text">Its Markdown, as last written.</param>
/// <param name="Anchor">
/// A key the node the thread goes to is known by (its <see cref="GraphNode.Key"/>, or for an address or a cluster
/// <c>address:</c> and one of its addresses, so that the thread finds the node again when the cluster changes), or
/// <see langword="null"/> for a comment that stands on its own.
/// </param>
/// <param name="Place">Its top left corner on the free surface.</param>
/// <param name="Size">
/// The size the user dragged it to, or <see langword="null"/> while it grows with its text. A note is never drawn
/// shorter than its text needs, so that nothing written is hidden.
/// </param>
/// <param name="CreatedBy">Who wrote it first.</param>
/// <param name="CreatedUtc">When.</param>
/// <param name="ChangedBy">Who wrote its text last.</param>
/// <param name="ChangedUtc">When.</param>
/// <param name="Sequence">The record that wrote it first in the sealed chain <c>tracing.comments</c>.</param>
public sealed record GraphComment(
    string Id,
    string Text,
    string? Anchor,
    NodePosition Place,
    CommentSize? Size,
    OperatorProfile CreatedBy,
    DateTimeOffset CreatedUtc,
    OperatorProfile ChangedBy,
    DateTimeOffset ChangedUtc,
    long Sequence);

/// <summary>The size a comment was dragged to, in the graph's own units.</summary>
/// <param name="Width">Its width.</param>
/// <param name="Height">Its least height.</param>
public readonly record struct CommentSize(double Width, double Height);

/// <summary>A comment to write.</summary>
/// <param name="Text">The Markdown; not empty.</param>
/// <param name="Anchor">The node it is about, or <see langword="null"/>.</param>
/// <param name="Place">Where it stands, its top left corner on the free surface.</param>
public sealed record GraphCommentDraft(string Text, string? Anchor, NodePosition Place);

/// <summary>What a sealed record about a comment did.</summary>
public enum CommentAction
{
    /// <summary>Wrote the comment, with its text, thread and first place.</summary>
    Added,

    /// <summary>Changed its text.</summary>
    Edited,

    /// <summary>Tied its thread to another node, or untied it.</summary>
    Anchored,

    /// <summary>Removed it; the record keeps what it said.</summary>
    Removed,
}

/// <summary>One sealed record in a comment's history.</summary>
/// <param name="Sequence">The record's place in the sealed chain <c>tracing.comments</c>.</param>
/// <param name="Action">What it did.</param>
/// <param name="Text">The text after it (for a removal, the text that was removed).</param>
/// <param name="Anchor">The thread after it (for a removal, the thread it had).</param>
/// <param name="By">Who.</param>
/// <param name="Utc">When.</param>
/// <param name="JournalSequence">The journal entry that records it.</param>
public sealed record GraphCommentRecord(
    long Sequence,
    CommentAction Action,
    string Text,
    string? Anchor,
    OperatorProfile By,
    DateTimeOffset Utc,
    long JournalSequence);

/// <summary>The rules for comments: their lengths and sizes.</summary>
public static class CommentRules
{
    /// <summary>The longest text a comment holds.</summary>
    public const int MaximumTextLength = 20_000;

    /// <summary>The longest node key a thread holds.</summary>
    public const int MaximumAnchorLength = 256;

    /// <summary>The narrowest a comment can be dragged to (sketch KN1a).</summary>
    public const double MinimumWidth = 200;

    /// <summary>The widest a comment can be dragged to (sketch KN1a).</summary>
    public const double MaximumWidth = 620;

    /// <summary>The lowest a comment can be dragged to (sketch KN1a).</summary>
    public const double MinimumHeight = 110;

    /// <summary>The highest a comment can be dragged to (sketch KN1a).</summary>
    public const double MaximumHeight = 420;

    /// <summary>
    /// Gives a comment's text as it is stored: line breaks as <c>\n</c>, without empty lines or spaces at its ends.
    /// </summary>
    /// <param name="text">The text as written.</param>
    /// <returns>The text to store; empty when nothing was written.</returns>
    public static string Normalize(string? text) =>
        (text ?? string.Empty).ReplaceLineEndings("\n").Trim();

    /// <summary>Keeps a dragged size within the sketch's bounds.</summary>
    /// <param name="width">The wanted width.</param>
    /// <param name="height">The wanted height.</param>
    /// <returns>The size within the bounds.</returns>
    public static CommentSize Clamp(double width, double height) => new(
        Math.Clamp(width, MinimumWidth, MaximumWidth), Math.Clamp(height, MinimumHeight, MaximumHeight));

    /// <summary>Gives the first line of a comment's text, for the journal and short mentions.</summary>
    /// <param name="text">The text.</param>
    /// <param name="length">The most characters to give.</param>
    /// <returns>The first non-empty line without leading Markdown marks, cut with an ellipsis when longer.</returns>
    public static string Title(string text, int length = 60)
    {
        ArgumentNullException.ThrowIfNull(text);
        var line = text.ReplaceLineEndings("\n").Split('\n').Select(part => part.Trim())
            .FirstOrDefault(part => part.Length > 0) ?? string.Empty;
        line = line.TrimStart('#', '-', '*', '+', '>', ' ');
        return line.Length <= length ? line : line[..(length - 1)] + "…";
    }
}

/// <summary>
/// Writes and reads the graph's comments (sketch KN1a). Every change of a comment's text, its thread and its removal
/// is a new sealed record with a line in the journal; nothing is written over (principle 1). Where a comment was moved
/// to and its size are ordinary data. Every method touches the case and belongs off the UI thread (principle 6).
/// </summary>
public interface IGraphCommentService
{
    /// <summary>Reads the comments in force, oldest first.</summary>
    /// <param name="case">The open case.</param>
    /// <returns>The comments.</returns>
    IReadOnlyList<GraphComment> GetComments(ICase @case);

    /// <summary>Writes a new comment, sealed with a line in the journal.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="draft">The comment.</param>
    /// <returns>The sealed comment.</returns>
    /// <exception cref="ArgumentException">
    /// The text is empty or too long, the thread is not a node key, or the place is not finite.
    /// </exception>
    GraphComment AddComment(ICase @case, GraphCommentDraft draft);

    /// <summary>
    /// Changes a comment's text as a new sealed record; the old text stays in the record before it. The same text
    /// writes nothing.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The comment.</param>
    /// <param name="text">
    /// The new Markdown; not empty (an emptied comment is removed with <see cref="RemoveComment"/>).
    /// </param>
    /// <returns>The comment, or <see langword="null"/> when it is gone.</returns>
    /// <exception cref="ArgumentException">The text is empty or too long.</exception>
    GraphComment? EditComment(ICase @case, string id, string text);

    /// <summary>Ties a comment's thread to another node, or unties it, as a new sealed record.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The comment.</param>
    /// <param name="anchor">The node's key, or <see langword="null"/> to untie it.</param>
    /// <returns>The comment, or <see langword="null"/> when it is gone.</returns>
    /// <exception cref="ArgumentException">The key is empty or too long.</exception>
    GraphComment? AnchorComment(ICase @case, string id, string? anchor);

    /// <summary>
    /// Removes a comment as a new sealed record that keeps what it said; it is no longer drawn, but its history stays.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The comment.</param>
    /// <returns><see langword="false"/> when it was gone already.</returns>
    bool RemoveComment(ICase @case, string id);

    /// <summary>Remembers where a comment was moved to and the size it was dragged to (ordinary data).</summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The comment.</param>
    /// <param name="place">Its top left corner.</param>
    /// <param name="size">Its size, or <see langword="null"/> while it grows with its text.</param>
    /// <returns><see langword="false"/> when the comment is gone.</returns>
    /// <exception cref="ArgumentException">The place or the size is not finite.</exception>
    bool SaveCommentPlace(ICase @case, string id, NodePosition place, CommentSize? size);

    /// <summary>Reads every sealed record about a comment, also a removed one, oldest first.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The comment.</param>
    /// <returns>The records; none for an unknown comment.</returns>
    IReadOnlyList<GraphCommentRecord> GetCommentHistory(ICase @case, string id);
}
