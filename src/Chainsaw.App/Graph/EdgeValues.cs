using System.Globalization;
using Chainsaw.Modules.Pricing.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>
/// What a line was worth at the time (sketch KU1a): the sum of its transfers' values, or honestly why there is none.
/// The rule for each is written here once, so that the graph and its tests say the same.
/// </summary>
public static class EdgeValues
{
    /// <summary>What a line with an unconfirmed transfer says instead of a value.</summary>
    public const string UnconfirmedText =
        "En tx på linjen är obekräftad. Kursen gäller blockets tid, så den hämtas när tx:en har ett block.";

    /// <summary>Sums the values of every transfer on a line, rounded to whole units, with their steps.</summary>
    /// <param name="parts">Every transfer on the line, valued, in time order; at least one.</param>
    /// <returns>The line's value.</returns>
    /// <exception cref="ArgumentException">There is no part.</exception>
    public static EdgeValue Sum(IReadOnlyList<EdgeValuePart> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0)
        {
            throw new ArgumentException("A line's value needs at least one transfer.", nameof(parts));
        }

        var total = parts.Sum(part => part.Valuation.Value);
        return new EdgeValue(
            Math.Round(total, 0, MidpointRounding.AwayFromZero),
            parts[0].Valuation.Price.Currency.Code(),
            [.. parts.SelectMany(part => part.Valuation.Steps).Distinct()])
        {
            Parts = parts,
        };
    }

    /// <summary>Gives what a line says when a transfer on it has no block yet.</summary>
    /// <param name="currency">The currency's code.</param>
    /// <returns>The gap.</returns>
    public static EdgeValueGap Unconfirmed(string currency) => new(currency, UnconfirmedText, false);

    /// <summary>
    /// Gives what a line says when a transfer's rate could not be had, or <see langword="null"/> when its asset is
    /// one no source prices: then there is nothing to wait for, and nothing stands under the amount.
    /// </summary>
    /// <param name="lookup">Pricing's answer, without a value.</param>
    /// <param name="at">The transfer's time.</param>
    /// <param name="currency">The currency's code.</param>
    /// <returns>The gap, or <see langword="null"/>.</returns>
    public static EdgeValueGap? GapOf(PriceLookup<Price> lookup, DateTimeOffset at, string currency)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        var when = at.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
        var problem = lookup.Problem is { Length: > 0 } text ? " " + text : string.Empty;
        return lookup.Status switch
        {
            PriceLookupStatus.Offline => new(currency, $"Kursen för {when} är inte hämtad · Offline.", true),
            PriceLookupStatus.NotInCase => new(
                currency, $"Kursen för {when} är inte hämtad än. Den hämtas när grafen expanderas.", false),
            PriceLookupStatus.NotAvailable => new(currency, $"Källan har ingen kurs för {when} än.{problem}", false),
            PriceLookupStatus.Failed => new(currency, $"Kursen för {when} kunde inte hämtas.{problem}", false),
            _ => null,
        };
    }
}
