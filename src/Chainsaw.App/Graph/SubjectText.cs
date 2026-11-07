namespace Chainsaw.App.Graph;

/// <summary>
/// Shortens an address, txid or id only as much as the room asks (CLAUDE.md, <em>Nodernas namn</em>; sketch GR5): the
/// start and the last four characters around an ellipsis, so that both ends can still be compared with the chain.
/// </summary>
public static class SubjectText
{
    /// <summary>How many characters are always kept at the end.</summary>
    public const int Tail = 4;

    /// <summary>How many characters are always kept at the start.</summary>
    public const int MinimumHead = 4;

    /// <summary>The fewest characters a shortened text has: the start, the ellipsis and the end.</summary>
    public const int Shortest = MinimumHead + 1 + Tail;

    /// <summary>Fits a text into a number of characters, shortening its middle only when it does not fit.</summary>
    /// <param name="text">The address, txid or id.</param>
    /// <param name="room">How many characters fit; never fewer than <see cref="Shortest"/> are written.</param>
    /// <returns>The whole text when it fits, otherwise its start, <c>…</c> and its last four characters.</returns>
    public static string Fit(string text, int room)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length <= Math.Max(room, Shortest))
        {
            return text;
        }

        var head = Math.Max(MinimumHead, room - Tail - 1);
        return string.Concat(text.AsSpan(0, head), "…", text.AsSpan(text.Length - Tail));
    }
}
