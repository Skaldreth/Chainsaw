namespace Chainsaw.App.Search;

/// <summary>How well a text matches what is written, best first.</summary>
public enum MatchQuality
{
    /// <summary>It does not match.</summary>
    None = 0,

    /// <summary>What is written stands somewhere inside it.</summary>
    Inside = 1,

    /// <summary>A word in it starts with what is written.</summary>
    WordStart = 2,

    /// <summary>It starts with what is written.</summary>
    Start = 3,

    /// <summary>It is what is written.</summary>
    Whole = 4,
}

/// <summary>Where and how well what is written was found in a text.</summary>
/// <param name="Quality">How well.</param>
/// <param name="Index">Where it starts in the text.</param>
/// <param name="Length">How long it is.</param>
public readonly record struct SearchMatch(MatchQuality Quality, int Index, int Length)
{
    /// <summary>No match.</summary>
    public static SearchMatch None { get; } = new(MatchQuality.None, 0, 0);

    /// <summary>Gets whether it matched.</summary>
    public bool IsMatch => Quality != MatchQuality.None;

    /// <summary>
    /// Finds what is written in a text, ignoring case: the whole text, its start, the start of a word, or anywhere.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="term">What is written, not empty.</param>
    /// <returns>The match, or <see cref="None"/>.</returns>
    public static SearchMatch Find(string text, string term)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(term);
        var index = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return None;
        }

        if (index == 0)
        {
            return new(text.Length == term.Length ? MatchQuality.Whole : MatchQuality.Start, 0, term.Length);
        }

        var wordStart = WordStartOf(text, term);
        return wordStart >= 0
            ? new(MatchQuality.WordStart, wordStart, term.Length)
            : new(MatchQuality.Inside, index, term.Length);
    }

    /// <summary>Gives where a word that starts with what is written begins in a text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="term">What is written.</param>
    /// <returns>The index, or -1 when no word starts with it.</returns>
    private static int WordStartOf(string text, string term)
    {
        var from = 0;
        while (from < text.Length)
        {
            var index = text.IndexOf(term, from, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return -1;
            }

            if (index == 0 || !char.IsLetterOrDigit(text[index - 1]))
            {
                return index;
            }

            from = index + 1;
        }

        return -1;
    }
}

/// <summary>
/// A hit's text split around what was written, so that it can stand in bold (sketch KR1a); a long hash or address is
/// cut in the middle, around the match.
/// </summary>
/// <param name="Before">The text before the match, perhaps cut with <c>…</c>.</param>
/// <param name="Match">The match, as written in the text.</param>
/// <param name="After">The text after the match, perhaps cut with <c>…</c>.</param>
public sealed record SearchText(string Before, string Match, string After)
{
    /// <summary>The cut.</summary>
    public const string Ellipsis = "…";

    /// <summary>How many characters stay at an end that is cut.</summary>
    private const int KeptEnd = 4;

    /// <summary>Gets the whole text as shown.</summary>
    public string Shown => Before + Match + After;

    /// <summary>Splits a text around a match, cutting it to a length when it is longer.</summary>
    /// <param name="text">The text.</param>
    /// <param name="match">Where what was written is in it; a non-match gives the text unbolded.</param>
    /// <param name="room">How many characters may be shown; a shorter text is shown whole.</param>
    /// <returns>The split text.</returns>
    public static SearchText Of(string text, SearchMatch match, int room)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!match.IsMatch || match.Index + match.Length > text.Length)
        {
            return Plain(text, room);
        }

        var before = text[..match.Index];
        var found = text.Substring(match.Index, match.Length);
        var after = text[(match.Index + match.Length)..];
        if (text.Length <= room)
        {
            return new(before, found, after);
        }

        var left = Math.Max(KeptEnd, (room - found.Length) / 2);
        var right = Math.Max(KeptEnd, room - found.Length - Math.Min(left, before.Length));
        return new(CutStart(before, left), found, CutEnd(after, right));
    }

    /// <summary>Gives a text with nothing bold, cut in the middle when it is longer than the room.</summary>
    /// <param name="text">The text.</param>
    /// <param name="room">How many characters may be shown.</param>
    /// <returns>The text.</returns>
    public static SearchText Plain(string text, int room)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length <= room)
        {
            return new(text, string.Empty, string.Empty);
        }

        var head = Math.Max(KeptEnd, (room - 1) / 2);
        var tail = Math.Max(KeptEnd, room - 1 - head);
        var cut = string.Concat(text.AsSpan(0, head), Ellipsis, text.AsSpan(text.Length - tail));
        return new(cut, string.Empty, string.Empty);
    }

    /// <summary>Keeps the end of what comes before the match, with its start cut away.</summary>
    /// <param name="text">The text before the match.</param>
    /// <param name="keep">How many characters stay.</param>
    /// <returns>The text, cut when it is longer.</returns>
    private static string CutStart(string text, int keep) => text.Length <= keep
        ? text
        : string.Concat(text.AsSpan(0, KeptEnd), Ellipsis, text.AsSpan(text.Length - Math.Max(0, keep - KeptEnd - 1)));

    /// <summary>Keeps the start of what comes after the match, with its end cut away.</summary>
    /// <param name="text">The text after the match.</param>
    /// <param name="keep">How many characters stay.</param>
    /// <returns>The text, cut when it is longer.</returns>
    private static string CutEnd(string text, int keep) => text.Length <= keep
        ? text
        : string.Concat(text.AsSpan(0, Math.Max(0, keep - KeptEnd - 1)), Ellipsis, text.AsSpan(text.Length - KeptEnd));
}
