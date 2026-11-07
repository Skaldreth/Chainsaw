using System.Globalization;
using System.Numerics;

namespace Chainsaw.SharedKernel;

/// <summary>
/// An amount of one asset in its smallest unit (satoshi, wei, sun or a token's base unit), as a whole number of any
/// size: wei needs more than 64 bits. The graph and the tracing count in this, whatever the chain; amounts of
/// different assets are never added, compared or mixed up.
/// </summary>
public readonly record struct AssetAmount : IComparable<AssetAmount>
{
    /// <summary>Creates an amount.</summary>
    /// <param name="asset">The asset.</param>
    /// <param name="units">How many of its smallest unit; may be negative only as a difference.</param>
    /// <exception cref="ArgumentNullException"><paramref name="asset"/> is null.</exception>
    public AssetAmount(Asset asset, BigInteger units)
    {
        Asset = asset ?? throw new ArgumentNullException(nameof(asset));
        Units = units;
    }

    /// <summary>Gets the asset.</summary>
    public Asset Asset { get; }

    /// <summary>Gets how many of the asset's smallest unit.</summary>
    public BigInteger Units { get; }

    /// <summary>Gets whether the amount is nothing.</summary>
    public bool IsZero => Units.IsZero;

    /// <summary>Gives nothing of an asset.</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>Zero units of it.</returns>
    public static AssetAmount Zero(Asset asset) => new(asset, BigInteger.Zero);

    /// <summary>Gives an amount of bitcoin.</summary>
    /// <param name="amount">The amount in satoshi.</param>
    /// <returns>The same amount, counted in <see cref="Asset.Bitcoin"/>.</returns>
    public static AssetAmount Of(Satoshi amount) => new(Asset.Bitcoin, amount.Value);

    /// <summary>Parses an amount written with the asset's decimals, for example <c>1.5</c> ether.</summary>
    /// <param name="asset">The asset.</param>
    /// <param name="text">The amount in whole units, with <c>.</c> before the decimals and invariant digits.</param>
    /// <returns>The amount.</returns>
    /// <exception cref="FormatException">The text is not a number, or has more decimals than the asset.</exception>
    public static AssetAmount Parse(Asset asset, string text)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        var negative = trimmed.StartsWith('-');
        var digits = negative ? trimmed[1..] : trimmed;
        var parts = digits.Split('.');
        var whole = parts[0];
        var fraction = parts.Length == 2 ? parts[1] : string.Empty;
        if (parts.Length > 2 || whole.Length == 0 || fraction.Length > asset.Decimals
            || !whole.All(char.IsAsciiDigit) || !fraction.All(char.IsAsciiDigit) || (parts.Length == 2 && fraction.Length == 0))
        {
            throw new FormatException($"Not an amount of {asset.Symbol} with at most {asset.Decimals} decimals.");
        }

        var units = BigInteger.Parse(
            whole + fraction.PadRight(asset.Decimals, '0'), NumberStyles.None, CultureInfo.InvariantCulture);
        return new AssetAmount(asset, negative ? -units : units);
    }

    /// <summary>Gives the amount as satoshi; only for bitcoin.</summary>
    /// <returns>The amount in satoshi.</returns>
    /// <exception cref="InvalidOperationException">The amount is not bitcoin.</exception>
    /// <exception cref="OverflowException">The amount does not fit in 64 bits.</exception>
    public Satoshi ToSatoshi() => Asset == Asset.Bitcoin
        ? new Satoshi((long)Units)
        : throw new InvalidOperationException($"{Asset} is not counted in satoshi.");

    /// <summary>Writes the amount with all the asset's decimals and invariant digits, without the symbol.</summary>
    /// <returns>For example <c>0.51200000</c> for bitcoin or <c>1.000000000000000000</c> for ether.</returns>
    public string ToDecimalString()
    {
        var digits = BigInteger.Abs(Units).ToString(CultureInfo.InvariantCulture)
            .PadLeft(Asset.Decimals + 1, '0');
        var whole = digits[..^Asset.Decimals];
        var text = Asset.Decimals == 0 ? whole : whole + "." + digits[^Asset.Decimals..];
        return Units.Sign < 0 ? "-" + text : text;
    }

    /// <summary>Adds two amounts of the same asset.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>The sum.</returns>
    /// <exception cref="InvalidOperationException">The assets differ.</exception>
    public static AssetAmount operator +(AssetAmount left, AssetAmount right) =>
        new(Same(left, right), left.Units + right.Units);

    /// <summary>Subtracts one amount from another of the same asset.</summary>
    /// <param name="left">The amount to subtract from.</param>
    /// <param name="right">The amount to subtract.</param>
    /// <returns>The difference.</returns>
    /// <exception cref="InvalidOperationException">The assets differ.</exception>
    public static AssetAmount operator -(AssetAmount left, AssetAmount right) =>
        new(Same(left, right), left.Units - right.Units);

    /// <summary>Says whether one amount is less than another of the same asset.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>Whether it is.</returns>
    /// <exception cref="InvalidOperationException">The assets differ.</exception>
    public static bool operator <(AssetAmount left, AssetAmount right) => left.CompareTo(right) < 0;

    /// <summary>Says whether one amount is more than another of the same asset.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>Whether it is.</returns>
    /// <exception cref="InvalidOperationException">The assets differ.</exception>
    public static bool operator >(AssetAmount left, AssetAmount right) => left.CompareTo(right) > 0;

    /// <summary>Says whether one amount is at most another of the same asset.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>Whether it is.</returns>
    /// <exception cref="InvalidOperationException">The assets differ.</exception>
    public static bool operator <=(AssetAmount left, AssetAmount right) => left.CompareTo(right) <= 0;

    /// <summary>Says whether one amount is at least another of the same asset.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>Whether it is.</returns>
    /// <exception cref="InvalidOperationException">The assets differ.</exception>
    public static bool operator >=(AssetAmount left, AssetAmount right) => left.CompareTo(right) >= 0;

    /// <summary>Compares two amounts of the same asset.</summary>
    /// <param name="other">The amount to compare with.</param>
    /// <returns>Less than zero, zero or more than zero.</returns>
    /// <exception cref="InvalidOperationException">The assets differ.</exception>
    public int CompareTo(AssetAmount other)
    {
        Same(this, other);
        return Units.CompareTo(other.Units);
    }

    /// <summary>Writes the amount and the asset's symbol, for example <c>0.51200000 BTC</c>.</summary>
    /// <returns>The amount with all its decimals, a space and the symbol.</returns>
    public override string ToString() => Asset is null ? "0" : ToDecimalString() + " " + Asset.Symbol;

    /// <summary>Checks that two amounts are of the same asset.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>The asset.</returns>
    /// <exception cref="InvalidOperationException">They are not.</exception>
    private static Asset Same(AssetAmount left, AssetAmount right) =>
        left.Asset is not null && left.Asset == right.Asset
            ? left.Asset
            : throw new InvalidOperationException($"Amounts of {left.Asset} and {right.Asset} cannot be counted together.");
}
