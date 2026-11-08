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

    /// <summary>What a line waiting for a rate says: it is asked for through the exit.</summary>
    private const string FetchingText = "hämtas genom utgången.";

    /// <summary>What it says instead while no exit is connected.</summary>
    private const string NotFetchedOfflineText = "är inte hämtad · Offline.";

    /// <summary>What a line says when its block time is ahead of the clock, so that no rate is set for it.</summary>
    public const string AheadText =
        "ligger i framtiden enligt datorns klocka, så ingen kurs är satt för den än. Blockets tid sätts av den som "
        + "bröt det och kan ligga upp till två timmar fel.";

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
    /// <param name="now">The time now, which tells a block time ahead of the clock from a missing source.</param>
    /// <returns>The gap, or <see langword="null"/>.</returns>
    public static EdgeValueGap? GapOf(
        PriceLookup<Price> lookup, DateTimeOffset at, string currency, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        var when = at.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
        var problem = lookup.Problem is { Length: > 0 } text ? " " + text : string.Empty;
        return lookup.Status switch
        {
            PriceLookupStatus.Offline => new(currency, $"Kursen för {when} är inte hämtad · Offline.", true),
            PriceLookupStatus.NotInCase => new(currency, $"Kursen för {when} {FetchingText}", false)
            {
                IsPending = true,
            },
            PriceLookupStatus.NotAvailable => new(currency, $"Källan har ingen kurs för {when} än.{problem}", false),
            PriceLookupStatus.Failed => new(currency, $"Kursen för {when} kunde inte hämtas.{problem}", false),
            PriceLookupStatus.Unsupported when at > now => new(currency, $"{when} {AheadText}", false),
            _ => null,
        };
    }

    /// <summary>
    /// Gives the gaps as they are while no exit is connected (principle 9): a rate that waits for the source is
    /// missing offline, and is asked for when the exit is back; the other reasons stand.
    /// </summary>
    /// <param name="gaps">The gaps by line key.</param>
    /// <returns>The gaps, with those that waited marked offline.</returns>
    public static IReadOnlyDictionary<string, EdgeValueGap> Offline(IReadOnlyDictionary<string, EdgeValueGap> gaps)
    {
        ArgumentNullException.ThrowIfNull(gaps);
        return gaps.ToDictionary(
            pair => pair.Key,
            pair => pair.Value is { IsPending: true, IsOffline: false } ? WaitingOffline(pair.Value) : pair.Value,
            StringComparer.Ordinal);
    }

    /// <summary>Turns a gap that waited for the source into one that says offline.</summary>
    /// <param name="gap">The gap.</param>
    /// <returns>The gap offline.</returns>
    private static EdgeValueGap WaitingOffline(EdgeValueGap gap) =>
        new(gap.Currency, gap.Tip.Replace(FetchingText, NotFetchedOfflineText, StringComparison.Ordinal), true)
        {
            IsPending = true,
        };
}
