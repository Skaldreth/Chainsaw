namespace Chainsaw.App.Links;

/// <summary>
/// What a link in a comment (sketch KN1a) may do: only a <c>chainsaw:</c> link to a start point is followed, and a link
/// whose text names another address than it goes to says where it really goes. Without WPF, so that it is tested.
/// </summary>
public static class NoteLinks
{
    /// <summary>Reads whether an address is followed: only a <c>chainsaw:</c> link to a start point.</summary>
    /// <param name="target">The address.</param>
    /// <returns><see langword="true"/> when it is followed.</returns>
    public static bool IsFollowed(string target) => ChainsawLink.TryParse(target, out _);

    /// <summary>
    /// Reads whether a link's text names an address or a link of its own that is not where the link goes, such as
    /// <c>[chainsaw:bitcoin/A](chainsaw:bitcoin/B)</c>.
    /// </summary>
    /// <param name="text">The link's text.</param>
    /// <param name="target">Where it goes.</param>
    /// <returns><see langword="true"/> when the text could mislead.</returns>
    public static bool Misleads(string text, string target)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        if (string.Equals(trimmed, target, StringComparison.Ordinal))
        {
            return false;
        }

        return ChainsawLink.TryParse(trimmed, out _) || StartPointText.Parse(trimmed).IsRecognised;
    }
}
