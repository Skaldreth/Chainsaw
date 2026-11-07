using System.Globalization;
using Chainsaw.Modules.Monitoring.Contracts;

namespace Chainsaw.Modules.Monitoring;

/// <summary>How the journal writes watches and hits, in Swedish.</summary>
internal static class WatchTexts
{
    /// <summary>Describes what a watch looks for in a few words.</summary>
    /// <param name="filter">The filter.</param>
    /// <returns>For example <c>ut · till 2 mottagare · BTC</c>.</returns>
    public static string Describe(WatchFilter filter)
    {
        var parts = new List<string> { DirectionText(filter.Direction) };
        if (filter.Receivers.Count > 0)
        {
            parts.Add($"till {filter.Receivers.Count.ToString(CultureInfo.InvariantCulture)} mottagare");
        }

        if (filter.Senders.Count > 0)
        {
            parts.Add($"från {filter.Senders.Count.ToString(CultureInfo.InvariantCulture)} avsändare");
        }

        if (filter.HasNoCounterparties)
        {
            parts.Add("alla motparter");
        }

        parts.Add(filter.Asset?.Symbol ?? "alla tillgångar");
        if (filter.Minimum is { } minimum)
        {
            parts.Add($"minst {minimum}");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Gives a direction in Swedish.</summary>
    /// <param name="direction">The direction.</param>
    /// <returns><c>in</c>, <c>ut</c> or <c>in och ut</c>.</returns>
    public static string DirectionText(WatchDirection direction) => direction switch
    {
        WatchDirection.In => "in",
        WatchDirection.Out => "ut",
        _ => "in och ut",
    };

    /// <summary>Gives the verb of a record.</summary>
    /// <param name="action">What it does.</param>
    /// <returns>For example <c>Bevakning pausad</c>.</returns>
    public static string Verb(WatchAction action) => action switch
    {
        WatchAction.Changed => "Bevakning ändrad",
        WatchAction.Paused => "Bevakning pausad",
        WatchAction.Resumed => "Bevakning återupptagen",
        WatchAction.Removed => "Bevakning tillbakatagen",
        _ => "Bevakning",
    };

    /// <summary>Lists hits in a few words.</summary>
    /// <param name="matches">The hits.</param>
    /// <returns>For example <c>ut 0.42 BTC i block 921 340</c>, separated by commas.</returns>
    public static string Hits(IEnumerable<WatchMatch> matches) => string.Join(", ", matches.Select(match =>
        $"{DirectionText(match.Direction)} {match.Amount} i block {Number(match.Block.Height)}"));

    /// <summary>Writes a number with spaces between thousands.</summary>
    /// <param name="value">The number.</param>
    /// <returns>For example <c>921 344</c>.</returns>
    public static string Number(long value) =>
        value.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');
}
