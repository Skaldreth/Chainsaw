using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>
/// What the graph throws in the trash besides its nodes (sketch SO1a): a comment and an own area, each under a key of
/// its own that never looks like a node's, and with a sort that the trash's view draws in its own form. The trash only
/// hides them; the comment and the area stay in the case as they were (principle 1).
/// </summary>
public static class TrashKeys
{
    /// <summary>The start of a comment's key in the trash, before its id.</summary>
    public const string CommentPrefix = "comment:";

    /// <summary>The sort a comment is thrown as, which the trash's view draws as a note.</summary>
    public const string CommentSort = "Comment";

    /// <summary>The sort an own area is thrown as, which the trash's view draws as an area.</summary>
    public const string AreaSort = "Area";

    /// <summary>What a comment without a line of text is called in the trash.</summary>
    public const string CommentName = "Kommentar";

    /// <summary>Gives a comment's key in the trash.</summary>
    /// <param name="id">The comment's id.</param>
    /// <returns>For example <c>comment:4a5e…</c>.</returns>
    public static string OfComment(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        return CommentPrefix + id;
    }

    /// <summary>Makes the member that throws a comment: its key, its first line as its name, and its sort.</summary>
    /// <param name="id">The comment's id.</param>
    /// <param name="text">The comment's text as stored.</param>
    /// <returns>The member.</returns>
    public static TrashMember Comment(string id, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var title = CommentRules.Title(text);
        return new TrashMember(OfComment(id), null, title.Length > 0 ? title : CommentName, CommentSort);
    }

    /// <summary>Makes the member that throws an own area: its key, its name and its sort.</summary>
    /// <param name="id">The area's id.</param>
    /// <param name="name">The area's name.</param>
    /// <returns>The member.</returns>
    public static TrashMember Area(string id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new TrashMember(GraphAreas.OwnKey(id), null, name, AreaSort);
    }

    /// <summary>Says whether a member in the trash is a comment.</summary>
    /// <param name="member">The member.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsComment(TrashMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return member.Key.StartsWith(CommentPrefix, StringComparison.Ordinal);
    }

    /// <summary>Says whether a member in the trash is an own area.</summary>
    /// <param name="member">The member.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsArea(TrashMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return member.Key.StartsWith(GraphAreas.OwnKeyPrefix, StringComparison.Ordinal);
    }
}
