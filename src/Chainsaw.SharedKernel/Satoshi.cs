using System.Globalization;

namespace Chainsaw.SharedKernel;

/// <summary>
/// An amount of bitcoin in its smallest unit, the satoshi (one hundred millionth of a bitcoin). Amounts are kept as
/// whole satoshi, never as floating point, so that sums on the chain add up exactly.
/// </summary>
/// <param name="Value">The number of satoshi.</param>
public readonly record struct Satoshi(long Value) : IComparable<Satoshi>
{
    /// <summary>The number of satoshi in one bitcoin.</summary>
    public const long PerBitcoin = 100_000_000;

    /// <summary>Gets an amount of nothing.</summary>
    public static Satoshi Zero => new(0);

    /// <summary>Creates an amount from bitcoin, which must be a whole number of satoshi.</summary>
    /// <param name="bitcoin">The amount in bitcoin, for example 0.5120.</param>
    /// <returns>The same amount in satoshi.</returns>
    /// <exception cref="ArgumentException">The amount has more than eight decimals.</exception>
    public static Satoshi FromBitcoin(decimal bitcoin)
    {
        var satoshi = bitcoin * PerBitcoin;
        if (satoshi != decimal.Truncate(satoshi))
        {
            throw new ArgumentException("A bitcoin amount has at most eight decimals.", nameof(bitcoin));
        }

        return new Satoshi((long)satoshi);
    }

    /// <summary>Gets the amount in bitcoin.</summary>
    /// <returns>The amount in bitcoin, exact to the satoshi.</returns>
    public decimal ToBitcoin() => (decimal)Value / PerBitcoin;

    /// <summary>Gives the same amount as an amount of <see cref="Asset.Bitcoin"/>, which the graph counts in.</summary>
    /// <returns>The amount.</returns>
    public AssetAmount ToAmount() => AssetAmount.Of(this);

    /// <summary>Adds two amounts.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>The sum.</returns>
    public static Satoshi operator +(Satoshi left, Satoshi right) => new(checked(left.Value + right.Value));

    /// <summary>Subtracts one amount from another.</summary>
    /// <param name="left">The amount to subtract from.</param>
    /// <param name="right">The amount to subtract.</param>
    /// <returns>The difference.</returns>
    public static Satoshi operator -(Satoshi left, Satoshi right) => new(checked(left.Value - right.Value));

    /// <summary>Compares two amounts.</summary>
    /// <param name="other">The amount to compare with.</param>
    /// <returns>Less than zero, zero or more than zero, as for <see cref="long.CompareTo(long)"/>.</returns>
    public int CompareTo(Satoshi other) => Value.CompareTo(other.Value);

    /// <summary>Writes the amount in bitcoin with eight decimals and invariant digits, as on the chain.</summary>
    /// <returns>For example <c>0.51200000</c>.</returns>
    public override string ToString() => ToBitcoin().ToString("0.00000000", CultureInfo.InvariantCulture);
}
