using System.Globalization;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>
/// Writes amounts as the graph shows them, in Swedish: <c>0,5120</c>. At least four decimals, and never fewer than
/// the amount needs, so that a small amount is never written as zero.
/// </summary>
public static class AmountText
{
    /// <summary>The fewest decimals written, as in the sketch.</summary>
    private const int MinimumDecimals = 4;

    /// <summary>Swedish numbers: a decimal comma and a space between thousands.</summary>
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>Writes an amount in bitcoin.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns>For example <c>0,5120</c>, <c>0,00012345</c> or <c>1 250,0000</c>.</returns>
    public static string Btc(Satoshi amount)
    {
        var bitcoin = amount.ToBitcoin();
        var decimals = MinimumDecimals;
        while (decimals < 8 && decimal.Round(bitcoin, decimals) != bitcoin)
        {
            decimals++;
        }

        return bitcoin.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), Swedish);
    }

    /// <summary>Writes an amount with its unit, as the inspector does.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns>For example <c>0,5120 BTC</c>.</returns>
    public static string BtcWithUnit(Satoshi amount) => Btc(amount) + " BTC";

    /// <summary>
    /// Writes what a line carries: the whole amount, or the traced part of it when only some is traced.
    /// </summary>
    /// <param name="amount">The amount on the chain.</param>
    /// <param name="traced">The traced part, a conclusion.</param>
    /// <returns>For example <c>0,5120</c> or <c>0,1570 av 0,2400</c>.</returns>
    public static string OnLine(Satoshi amount, Satoshi traced) =>
        traced.Value > 0 && traced.CompareTo(amount) < 0 ? $"{Btc(traced)} av {Btc(amount)}" : Btc(amount);
}

/// <summary>Which amounts the lines carry (sketch GR4): all, only the traced, or none when the graph is large.</summary>
public enum AmountLabelMode
{
    /// <summary>Every line carries its amount.</summary>
    All,

    /// <summary>Only the lines that carry traced money.</summary>
    Traced,

    /// <summary>No line; the amount shows on hover and when something is chosen.</summary>
    None,
}

/// <summary>Reads <see cref="AmountLabelMode"/>.</summary>
public static class AmountLabelModes
{
    /// <summary>Gives the next mode, as the button in the graph's corner steps through them.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The next one, back to the first after the last.</returns>
    public static AmountLabelMode Next(this AmountLabelMode mode) => mode switch
    {
        AmountLabelMode.All => AmountLabelMode.Traced,
        AmountLabelMode.Traced => AmountLabelMode.None,
        _ => AmountLabelMode.All,
    };

    /// <summary>Gives the mode as the button says it.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>A short text.</returns>
    public static string Describe(this AmountLabelMode mode) => mode switch
    {
        AmountLabelMode.All => "Alla belopp",
        AmountLabelMode.Traced => "Spårade belopp",
        _ => "Inga belopp",
    };

    /// <summary>Gives the mode as the case stores it with the graph's view.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The stored value.</returns>
    public static GraphAmountLabels ToStored(this AmountLabelMode mode) => mode switch
    {
        AmountLabelMode.Traced => GraphAmountLabels.Traced,
        AmountLabelMode.None => GraphAmountLabels.None,
        _ => GraphAmountLabels.All,
    };

    /// <summary>Reads the mode the case stores with the graph's view.</summary>
    /// <param name="stored">The stored value.</param>
    /// <returns>The mode.</returns>
    public static AmountLabelMode FromStored(GraphAmountLabels stored) => stored switch
    {
        GraphAmountLabels.Traced => AmountLabelMode.Traced,
        GraphAmountLabels.None => AmountLabelMode.None,
        _ => AmountLabelMode.All,
    };

    /// <summary>
    /// Says whether a line's amount is written: always under the pointer; while something is chosen, only on the lines
    /// around it; otherwise as the mode says.
    /// </summary>
    /// <param name="mode">The mode.</param>
    /// <param name="isTraced">Whether the line carries traced money.</param>
    /// <param name="isHovered">Whether the pointer is on the line.</param>
    /// <param name="focus">What is chosen, which decides what is near.</param>
    /// <param name="isNear">Whether the line is near what is chosen.</param>
    /// <returns><see langword="true"/> when the amount is written.</returns>
    public static bool Shows(this AmountLabelMode mode, bool isTraced, bool isHovered, bool focus, bool isNear)
    {
        if (isHovered)
        {
            return true;
        }

        if (focus)
        {
            return isNear;
        }

        return mode == AmountLabelMode.All || (mode == AmountLabelMode.Traced && isTraced);
    }
}

/// <summary>A line's thickness is its amount (sketch GR3a): thick is much of the traced money, thin is little.</summary>
public static class EdgeWidth
{
    /// <summary>The thickness of a line that carries none of the traced money.</summary>
    public const double Untraced = 1.1;

    /// <summary>The thinnest a line with traced money is.</summary>
    public const double ThinnestTraced = 1.2;

    /// <summary>How much thicker the line with the most traced money is than the thinnest.</summary>
    public const double TracedSpan = 4;

    /// <summary>Gives a line's thickness from its traced amount, against the largest traced amount on any line.</summary>
    /// <param name="traced">The line's traced amount.</param>
    /// <param name="largest">The largest traced amount on any line in the graph.</param>
    /// <returns>The thickness in the surface's units.</returns>
    public static double For(Satoshi traced, Satoshi largest)
    {
        if (traced.Value <= 0 || largest.Value <= 0)
        {
            return Untraced;
        }

        var share = Math.Min(1, (double)traced.Value / largest.Value);
        return ThinnestTraced + (TracedSpan * share);
    }
}
