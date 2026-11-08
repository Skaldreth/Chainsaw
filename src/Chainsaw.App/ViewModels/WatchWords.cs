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
    /// <summary>The empty view's title with a case open and no watches.</summary>
    public const string EmptyTitle = "Inga bevakningar än";

    /// <summary>The empty view's muted lines: what a watch does and how one is added.</summary>
    public const string EmptyText =
        "En bevakning säger till när pengarna på en adress eller ett kluster rör sig. " +
        "Välj en nod i grafen och tryck B, eller lägg till en här.";

    /// <summary>The empty view's title without an open case.</summary>
    public const string NoCaseTitle = "Inget ärende öppet";

    /// <summary>The empty view's muted line without an open case.</summary>
    public const string NoCaseText = "Bevakningarna hör till ett ärende. Öppna eller skapa ett för att bevaka adresser.";

    /// <summary>The honest state of the watching in the view's head while it runs (stage 26).</summary>
    public const string RunningText = "Bevakar medan Chainsaw är öppet";

    /// <summary>The head's tooltip while it runs.</summary>
    public const string RunningTip =
        "Bevakningarna kontrolleras medan Chainsaw är öppet och upplåst, genom utgången och bara mot en endpoint " +
        "i fas med kedjan.";

    /// <summary>The head while the background process watches through Windows (sketch BV1a).</summary>
    public const string BackgroundRunningText = "Går i bakgrunden · genom Windows";

    /// <summary>What follows when the background says which endpoint in step it asks.</summary>
    public const string InStepText = " · i fas med {0}";

    /// <summary>The head's tooltip while the background process watches.</summary>
    public const string BackgroundRunningTip =
        "Bakgrundsprocessen vet bara adresserna och ärendets id. Den ser inget av ärendet. Det den ser förs in " +
        "i ärendet när det öppnas och låses upp.";

    /// <summary>The head when VPN is the way out, which the background cannot use (sketch BV1a, waiting).</summary>
    public const string BackgroundWaitsText =
        "Bakgrunden väntar på utgången · bevakar bara medan Chainsaw är öppet";

    /// <summary>The head's tooltip when VPN is the way out.</summary>
    public const string BackgroundWaitsTip =
        "VPN-utgången ligger krypterad i valvet, så bakgrunden kan inte ansluta den när Chainsaw är stängt och " +
        "gör då inga uppslag. Välj Windows som utgång för att bevaka också då.";

    /// <summary>The head when the user has turned the background off (sketch BV1a, off).</summary>
    public const string BackgroundOffText = "Bakgrunden är av · bevakar bara medan Chainsaw är öppet";

    /// <summary>The head's tooltip when the background is off.</summary>
    public const string BackgroundOffTip =
        "Slå på bakgrunden i Inställningar för att bevaka när Chainsaw är stängt.";

    /// <summary>The head when the background runs but its lookups fail.</summary>
    public const string BackgroundFailingText = "Går i bakgrunden · uppslagen misslyckas";

    /// <summary>The head's tooltip when the background's lookups fail.</summary>
    public const string BackgroundFailingTip =
        "Bakgrundsprocessen kunde inte fråga källorna vid senaste kontrollen och försöker igen. Inget av ärendet " +
        "skickades.";

    /// <summary>The head when the background runs but the watched chains' sources are off.</summary>
    public const string BackgroundSourcesOffText = "Går i bakgrunden · källorna är av, inga uppslag görs";

    /// <summary>The head's tooltip when the watched chains' sources are off.</summary>
    public const string BackgroundSourcesOffTip =
        "Källorna för de bevakade kedjorna är avstängda i Inställningar → Källor. " +
        "Bakgrunden frågar inget förrän de slås på.";

    /// <summary>The head when the background should run but does not.</summary>
    public const string BackgroundStoppedText = "Bakgrunden går inte · bevakar bara medan Chainsaw är öppet";

    /// <summary>The head's tooltip when the background should run but does not.</summary>
    public const string BackgroundStoppedTip =
        "Bakgrundsprocessen kunde inte startas. Chainsaw försöker igen en gång i minuten.";

    /// <summary>What follows the wait in the head while nothing can leave.</summary>
    public const string NoLookupsText = " · inga uppslag görs";

    /// <summary>The head when no exit is connected.</summary>
    public const string NoExitText = OfflineWords.NoExitWaiting + NoLookupsText;

    /// <summary>The head when a chain's sources are off.</summary>
    public const string ChainOffText = "Källorna är av för {0} · inga uppslag görs";

    /// <summary>The head when lookups were sent and failed.</summary>
    public const string FailingText = "Uppslagen misslyckas · försöker igen";

    /// <summary>The head's tooltip when lookups fail.</summary>
    public const string FailingTip =
        "Källan kunde inte frågas eller svarade fel vid senaste kontrollen; bevakningens rad säger varför. " +
        "Bevakningen försöker igen vid nästa kontroll.";

    /// <summary>
    /// The head when the last check could not ask the source (it was offline, or only the case answered).
    /// </summary>
    public const string NoSourceText = "Kunde inte fråga källan · försöker igen";

    /// <summary>
    /// The head's tooltip when the last check could not ask the source (offline, or only the case answered).
    /// </summary>
    public const string NoSourceTip =
        "Källan kunde inte frågas vid senaste kontrollen; inget nytt är känt. " +
        "Bevakningen försöker igen vid nästa kontroll.";

    /// <summary>The head while Chainsaw is locked (never shown, since the window is then hidden).</summary>
    public const string LockedText = "Låst · inga uppslag görs";

    /// <summary>The line in the form under the rows.</summary>
    public const string FormNote = "Frågar källan genom utgången; vyns huvud säger om det går också i bakgrunden.";

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
    /// <param name="waiting">
    /// What it waits for while nothing can leave (<see cref="OfflineWords.Waiting"/>); an exit when
    /// <see langword="null"/>.
    /// </param>
    /// <returns>For example <c>Aktiv</c> or <c>Väntar på utgången</c>.</returns>
    public static string State(Watch watch, WatchGate gate, string? waiting = null)
    {
        ArgumentNullException.ThrowIfNull(watch);
        if (watch.State == WatchState.Paused)
        {
            return "Pausad";
        }

        return gate switch
        {
            WatchGate.NoExit => waiting ?? OfflineWords.NoExitWaiting,
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

    /// <summary>
    /// Says what a watch does in one sentence (sketch BV3a): the direction, the counterparties when there are any, the
    /// minimum when one is given, and how often the source is asked.
    /// </summary>
    /// <param name="direction">The direction.</param>
    /// <param name="receivers">The receivers' names; empty for all.</param>
    /// <param name="senders">The senders' names; empty for all.</param>
    /// <param name="minimum">The minimum with its unit; empty for none.</param>
    /// <param name="pace">How often the source is asked.</param>
    /// <returns>The sentence.</returns>
    public static string Sentence(
        WatchDirection direction,
        IReadOnlyList<string> receivers,
        IReadOnlyList<string> senders,
        string minimum,
        WatchPace pace)
    {
        ArgumentNullException.ThrowIfNull(receivers);
        ArgumentNullException.ThrowIfNull(senders);
        var verb = direction switch
        {
            WatchDirection.In => "kommer in",
            WatchDirection.Out => "går ut",
            _ => "rör sig",
        };
        var text = new System.Text.StringBuilder("Säger till när pengar ").Append(verb);
        if (direction != WatchDirection.In && receivers.Count > 0)
        {
            text.Append(" till ").Append(Names(receivers));
        }

        if (direction != WatchDirection.Out && senders.Count > 0)
        {
            text.Append(" från ").Append(Names(senders));
        }

        if (minimum.Length > 0)
        {
            text.Append(" över ").Append(minimum);
        }

        var asked = pace == WatchPace.EveryBlock ? "vid varje nytt block" : $"högst en gång per {Pace(pace)}";
        return text.Append(". Frågar ").Append(asked).Append('.').ToString();
    }

    /// <summary>Names counterparties in a sentence: one or two by name, more by their number.</summary>
    /// <param name="names">The names.</param>
    /// <returns>The words.</returns>
    private static string Names(IReadOnlyList<string> names) => names.Count switch
    {
        1 => names[0],
        2 => $"{names[0]} eller {names[1]}",
        _ => $"{names.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} motparter",
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
            parts.Add("Till " + string.Join(", ", filter.Receivers.Select(name).Distinct()));
        }

        if (filter.Senders.Count > 0)
        {
            parts.Add("Från " + string.Join(", ", filter.Senders.Select(name).Distinct()));
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
