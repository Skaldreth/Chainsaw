using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Links;

/// <summary>
/// A start point as text in the window: an address or a tx alone, or with its chain in front, as <c>tron:…</c>. The
/// chain in front is needed where the value alone would be read as another chain's: a TRON tx has the same form as a
/// Bitcoin txid, so a TRON tx from a link is written <c>tron:&lt;hex&gt;</c> and is never taken for Bitcoin.
/// </summary>
public static class StartPointText
{
    /// <summary>The chain names that may stand in front of a value (and in a link), and the chain each means.</summary>
    private static readonly Dictionary<string, Chain> ChainNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bitcoin"] = Chain.Bitcoin,
        ["btc"] = Chain.Bitcoin,
        ["ethereum"] = Chain.Ethereum,
        ["eth"] = Chain.Ethereum,
        ["tron"] = Chain.Tron,
        ["trx"] = Chain.Tron,
    };

    /// <summary>Finds the chain a name means.</summary>
    /// <param name="name">The name, for example <c>tron</c>.</param>
    /// <param name="chain">The chain, when the name is known.</param>
    /// <returns><see langword="true"/> when the name is known.</returns>
    public static bool TryChain(string name, out Chain chain) => ChainNames.TryGetValue(name, out chain);

    /// <summary>Recognises a text, with its chain in front when one is written.</summary>
    /// <param name="text">The text, for example <c>bc1q…</c> or <c>tron:4f2a…</c>.</param>
    /// <returns>What it is; unknown when a chain is written and the value is not on it.</returns>
    public static ChainStartPoint Parse(string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 && TryChain(trimmed[..colon], out var chain)
            ? ChainStartPoint.Recognise(chain, trimmed[(colon + 1)..])
            : ChainStartPoint.Recognise(trimmed);
    }

    /// <summary>
    /// Writes a start point so that <see cref="Parse"/> reads it back on the same chain: a Bitcoin value alone, any
    /// other with its chain in front.
    /// </summary>
    /// <param name="startPoint">The recognised start point.</param>
    /// <returns>The text.</returns>
    public static string Write(ChainStartPoint startPoint)
    {
        ArgumentNullException.ThrowIfNull(startPoint);
        return startPoint.Chain is { } chain && chain != Chain.Bitcoin
            ? $"{ChainNames.First(item => item.Value == chain).Key}:{startPoint.Value}"
            : startPoint.Value;
    }
}
