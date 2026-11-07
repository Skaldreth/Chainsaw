using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>Writes amounts, shares, times and txids the way the analyses' texts say them, in Swedish.</summary>
internal static class CheckText
{
    /// <summary>Swedish, for the decimal comma.</summary>
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>Writes an amount in bitcoin with at least four decimals, never rounded, for example <c>0,1462</c>.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns>The text, without the unit.</returns>
    public static string Btc(Satoshi amount) => amount.ToBitcoin().ToString("0.0000####", Swedish);

    /// <summary>Writes a share as a percentage with one decimal, for example <c>2,5 %</c>.</summary>
    /// <param name="share">The share, from 0 to 1.</param>
    /// <returns>The text.</returns>
    public static string Percent(double share) =>
        (share * 100).ToString("0.#", Swedish) + " %";

    /// <summary>Writes a time span the way a person says it: minutes, hours or days.</summary>
    /// <param name="span">The span; not negative.</param>
    /// <returns>For example <c>41 min</c>, <c>5 h</c> or <c>3 dygn</c>.</returns>
    public static string Span(TimeSpan span)
    {
        if (span < TimeSpan.FromHours(1))
        {
            return ((int)Math.Round(span.TotalMinutes)).ToString(CultureInfo.InvariantCulture) + " min";
        }

        return span < TimeSpan.FromHours(48)
            ? ((int)Math.Round(span.TotalHours)).ToString(CultureInfo.InvariantCulture) + " h"
            : ((int)Math.Round(span.TotalDays)).ToString(CultureInfo.InvariantCulture) + " dygn";
    }

    /// <summary>Shortens a txid to its first and last eight characters.</summary>
    /// <param name="txid">The txid.</param>
    /// <returns>For example <c>4a5e1e4b…a33b3c44</c>.</returns>
    public static string Short(Txid txid) => txid.Value[..8] + "…" + txid.Value[^8..];

    /// <summary>Writes a count.</summary>
    /// <param name="count">The count.</param>
    /// <returns>The digits.</returns>
    public static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);
}
