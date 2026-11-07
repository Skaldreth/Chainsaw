using System.Globalization;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// How the view Bevakning, the form and the graph write watches and hits, in Swedish (sketches BV1a, BV2a).
/// </summary>
public static class WatchWords
{
    /// <summary>The honest state of the watching in the view's head while it runs (stage 26).</summary>
    public const string RunningText = "Bevakar medan Chainsaw är öppet";

    /// <summary>The head's tooltip while it runs.</summary>
    public const string RunningTip =
        "Bevakningarna kontrolleras bara medan Chainsaw är öppet och upplåst, genom utgången och bara mot en " +
        "endpoint " +
        "i fas med kedjan. Att bevaka när Chainsaw är stängt är inte byggt än.";

    /// <summary>The head when no exit is connected.</summary>
    public const string NoExitText = "Väntar på utgången · inga uppslag görs";

    /// <summary>The head's tooltip when no exit is connected.</summary>
    public const string NoExitTip = "Utgången är inte ansluten. Bevakningen frågar inget förrän den är det.";

    /// <summary>The head when a chain's sources are off.</summary>
    public const string ChainOffText = "Källorna är av för {0} · inga uppslag görs";

    /// <summary>The head while Chainsaw is locked (never shown, since the window is then hidden).</summary>
    public const string LockedText = "Låst · inga uppslag görs";

    /// <summary>The line in the form under the rows.</summary>
    public const string FormNote = "Bevakar medan Chainsaw är öppet, genom utgången.";

    /// <summary>The text of a watch never checked.</summary>
    public const string NeverCheckedText = "Inte kontrollerad än";

    /// <summary>Gives a direction as the list and the form write it.</summary>
    /// <param name="direction">The direction.</param>
    /// <returns><c>Inkommande</c>, <c>Utgående</c> or <c>In och ut</c>.</returns>
    public static string Direction(WatchDirection direction) => direction switch
    {
        WatchDirection.In => "Inkommande",
        WatchDirection.Out => "Utgående",
        _ => "In och ut",
    };

    /// <summary>Gives a state as the list writes it.</summary>
    /// <param name="watch">The watch.</param>
    /// <param name="gate">Whether lookups may go for its chain.</param>
    /// <returns>For example <c>Aktiv</c> or <c>Väntar på utgången</c>.</returns>
    public static string State(Watch watch, WatchGate gate)
    {
        ArgumentNullException.ThrowIfNull(watch);
        if (watch.State == WatchState.Paused)
        {
            return "Pausad";
        }

        return gate switch
        {
            WatchGate.NoExit => "Väntar på utgången",
            WatchGate.ChainOff => "Källorna är av",
            WatchGate.Locked => "Låst",
            _ => watch.StartHeight is null ? "Startar vid nästa kontroll" : "Aktiv",
        };
    }

    /// <summary>Shortens an address as the graph names a node without a name.</summary>
    /// <param name="address">The address.</param>
    /// <returns>For example <c>bc1q…x0wl</c>.</returns>
    public static string Short(ChainAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return SubjectText.Fit(address.Value, SubjectText.Shortest);
    }

    /// <summary>Gives the asset part of a watch, as the list writes it.</summary>
    /// <param name="filter">The filter.</param>
    /// <returns>For example <c>BTC</c>, <c>Bara USDT</c> or <c>ETH och tokens</c>.</returns>
    public static string Asset(WatchFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return filter switch
        {
            { Chain: Chain.Bitcoin } => "BTC",
            { Asset: { } asset } => "Bara " + asset.Symbol,
            _ => "ETH och tokens",
        };
    }

    /// <summary>Gives how often a watch asks the source, as the form and the details write it.</summary>
    /// <param name="pace">The pace.</param>
    /// <returns>For example <c>Varje block</c> or <c>5 min</c>.</returns>
    public static string Pace(WatchPace pace) => pace switch
    {
        WatchPace.OneMinute => "1 min",
        WatchPace.FiveMinutes => "5 min",
        WatchPace.FifteenMinutes => "15 min",
        _ => "Varje block",
    };

    /// <summary>Gives the minimum as the details write it.</summary>
    /// <param name="filter">The filter.</param>
    /// <returns>The amount with its unit, or <c>Inget</c>.</returns>
    public static string Minimum(WatchFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return filter.Minimum is { } minimum ? AmountText.WithUnit(minimum) : "Inget";
    }

    /// <summary>Describes the counterparties in words, for a tooltip and the details.</summary>
    /// <param name="filter">The filter.</param>
    /// <param name="name">Names an address: its node's name in the graph, or the address shortened.</param>
    /// <returns>For example <c>Till Börs A, bc1q…9fd2</c>, or <c>Alla motparter</c>.</returns>
    public static string Parties(WatchFilter filter, Func<ChainAddress, string> name)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(name);
        var parts = new List<string>();
        if (filter.Receivers.Count > 0)
        {
            parts.Add("Till " + string.Join(", ", filter.Receivers.Select(name)));
        }

        if (filter.Senders.Count > 0)
        {
            parts.Add("Från " + string.Join(", ", filter.Senders.Select(name)));
        }

        return parts.Count == 0 ? "Alla motparter" : string.Join(" · ", parts);
    }

    /// <summary>Writes a hit in one line.</summary>
    /// <param name="hit">The hit.</param>
    /// <param name="name">Names an address.</param>
    /// <returns>For example <c>Utgående 0,42 BTC till Börs A</c>.</returns>
    public static string Hit(WatchHit hit, Func<ChainAddress, string> name)
    {
        ArgumentNullException.ThrowIfNull(hit);
        ArgumentNullException.ThrowIfNull(name);
        var match = hit.Match;
        var preposition = match.Direction == WatchDirection.Out ? "till" : "från";
        var parties = match.Counterparties.Count switch
        {
            0 => string.Empty,
            1 => $" {preposition} {name(match.Counterparties[0])}",
            var count => $" {preposition} {name(match.Counterparties[0])} och {count - 1} till",
        };
        return $"{Direction(match.Direction)} {AmountText.WithUnit(match.Amount)}{parties}";
    }

    /// <summary>Writes a block height with spaces between thousands.</summary>
    /// <param name="height">The height.</param>
    /// <returns>For example <c>921 344</c>.</returns>
    public static string Block(long height) =>
        height.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');

    /// <summary>
    /// Writes when something happened, in local time: <c>14:02</c> today, otherwise <c>7 nov 14:02</c>.
    /// </summary>
    /// <param name="utc">When, in UTC.</param>
    /// <param name="now">Now, in UTC.</param>
    /// <returns>The text.</returns>
    public static string When(DateTimeOffset utc, DateTimeOffset now)
    {
        var local = utc.ToLocalTime();
        var swedish = CultureInfo.GetCultureInfo("sv-SE");
        return local.Date == now.ToLocalTime().Date
            ? local.ToString("HH:mm", swedish)
            : local.ToString("d MMM HH:mm", swedish);
    }
}
