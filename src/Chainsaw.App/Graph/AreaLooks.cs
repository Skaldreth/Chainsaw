using Chainsaw.Modules.Checks.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>How an area or a check is drawn: its icon's resource key and its tint (sketch KF1b).</summary>
/// <param name="Icon">The icon's resource key.</param>
/// <param name="Tint">The tint.</param>
public sealed record AreaLook(string Icon, AreaTint Tint)
{
    /// <summary>Gets the tint's resource key, for the views.</summary>
    public string TintKey => AreaLooks.BrushKey(Tint);
}

/// <summary>
/// The areas' looks in one table (sketch KF1b): the sorts the menu <em>Områden i grafen</em> chooses between, and the
/// look of each analysis's area, so that the graph, the list and the menu read the same.
/// </summary>
public static class AreaLooks
{
    /// <summary>The icon of a peel chain.</summary>
    public const string PeelIcon = "Icon.Check.Peel";

    /// <summary>The icon of an own area.</summary>
    public const string AreaIcon = "Icon.Area";

    /// <summary>The icon of time.</summary>
    public const string ClockIcon = "Icon.Clock";

    /// <summary>The sorts in the menu's order, with their names.</summary>
    public static IReadOnlyList<(AreaSort Sort, string Name)> Sorts { get; } =
    [
        (AreaSort.Obfuscation, "Förvirring"),
        (AreaSort.Wallet, "Plånböcker"),
        (AreaSort.Service, "Tjänster"),
        (AreaSort.OtherChain, "Annan kedja"),
        (AreaSort.Time, "Tid"),
        (AreaSort.Own, "Egna"),
    ];

    /// <summary>Gives a sort's name in the menu.</summary>
    /// <param name="sort">The sort.</param>
    /// <returns>For example <c>Förvirring</c>.</returns>
    public static string Name(AreaSort sort) => Sorts.First(entry => entry.Sort == sort).Name;

    /// <summary>Gives a sort's look when nothing more particular says otherwise.</summary>
    /// <param name="sort">The sort.</param>
    /// <returns>The look.</returns>
    public static AreaLook Of(AreaSort sort) => sort switch
    {
        AreaSort.Obfuscation => new("Icon.Kind.Mixer", AreaTint.Sand),
        AreaSort.Wallet => new("Icon.Kind.Wallet", AreaTint.Grey),
        AreaSort.Service => new("Icon.Kind.Bank", AreaTint.Blue),
        AreaSort.OtherChain => new("Icon.Kind.Swap", AreaTint.Lila),
        AreaSort.Time => new(ClockIcon, AreaTint.Grey),
        _ => new(AreaIcon, AreaTint.Green),
    };

    /// <summary>
    /// Gives an analysis's look: red for mixers, CoinJoin, the shielded pools and mixed coins merged, sand for peel chains, splits, many
    /// hops and dust, else its sort's.
    /// </summary>
    /// <param name="analysis">The analysis's name.</param>
    /// <param name="sort">The area's sort.</param>
    /// <returns>The look.</returns>
    public static AreaLook OfAnalysis(string analysis, AreaSort sort) => analysis switch
    {
        "mixer-outputs" or "mixer-parts" or "mixer-chain" => new("Icon.Kind.Mixer", AreaTint.Red),
        "coinjoin-area" => new("Icon.Kind.CoinJoin", AreaTint.Red),
        "shielded-pool" => new("Icon.Kind.Mask", AreaTint.Red),
        "peel-chain-area" => new(PeelIcon, AreaTint.Sand),
        "coinjoin-merge" => new("Icon.Join", AreaTint.Red),
        "split-merge" => new("Icon.Split", AreaTint.Sand),
        "pass-through" => new("Icon.Layers", AreaTint.Sand),
        "dust" => new("Icon.Kind.Coin", AreaTint.Sand),
        _ => Of(sort),
    };

    /// <summary>The tints the form for an own area offers, in order.</summary>
    public static IReadOnlyList<AreaTint> OwnTints { get; } =
        [AreaTint.Green, AreaTint.Grey, AreaTint.Sand, AreaTint.Blue, AreaTint.Lila];

    /// <summary>Gives a tint's name, in Swedish.</summary>
    /// <param name="tint">The tint.</param>
    /// <returns>For example <c>Grön</c>.</returns>
    public static string TintName(AreaTint tint) => tint switch
    {
        AreaTint.Grey => "Grå",
        AreaTint.Sand => "Sand",
        AreaTint.Blue => "Blå",
        AreaTint.Lila => "Lila",
        AreaTint.Red => "Röd",
        _ => "Grön",
    };

    /// <summary>Gives a tint's resource key.</summary>
    /// <param name="tint">The tint.</param>
    /// <returns>For example <c>Brush.Area.Green</c>.</returns>
    public static string BrushKey(AreaTint tint) => "Brush.Area." + tint;
}
