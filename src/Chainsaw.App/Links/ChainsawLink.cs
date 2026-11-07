using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.App.Links;

/// <summary>
/// The link <c>chainsaw:&lt;chain&gt;/&lt;address or tx&gt;</c> that other programs open Chainsaw with, also given as
/// the argument to <c>Chainsaw.exe</c> (CLAUDE.md, *Öppna med en adress*). A link only names a start point: it never
/// carries a command, a path or anything else, and anything that is not exactly a link is refused.
/// </summary>
public static class ChainsawLink
{
    /// <summary>The link's scheme, with its colon.</summary>
    public const string Scheme = "chainsaw:";

    /// <summary>The longest link that is read at all; a start point on any known chain is far shorter.</summary>
    public const int MaximumLength = 200;

    /// <summary>
    /// Reads a link: <c>chainsaw:bitcoin/bc1q…</c>, also with <c>//</c> after the colon and a slash at the end, as
    /// some programs write it. The value must be an address or a tx on the named chain.
    /// </summary>
    /// <param name="text">The link.</param>
    /// <param name="startPoint">The start point it names, when it is a link.</param>
    /// <returns><see langword="true"/> when the text is a link to a recognised start point.</returns>
    public static bool TryParse(string? text, out ChainStartPoint startPoint)
    {
        startPoint = ChainStartPoint.Recognise(string.Empty);
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length > MaximumLength || !trimmed.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = trimmed[Scheme.Length..];
        rest = rest.StartsWith("//", StringComparison.Ordinal) ? rest[2..] : rest;
        rest = rest.EndsWith('/') ? rest[..^1] : rest;
        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || !StartPointText.TryChain(rest[..slash], out var chain))
        {
            return false;
        }

        var value = rest[(slash + 1)..];
        if (value.Length == 0 || !value.All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        startPoint = ChainStartPoint.Recognise(chain, value);
        return startPoint.IsRecognised;
    }
}
