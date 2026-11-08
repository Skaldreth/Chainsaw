using System.Globalization;
using Chainsaw.Modules.Network.Contracts;

namespace Chainsaw.App.Exits;

/// <summary>One line of the box <em>Kontroll</em> under an exit (sketch UT3b): a key and its value.</summary>
/// <param name="Key">The key, such as <em>Syns som</em>.</param>
/// <param name="Value">The value.</param>
/// <param name="IsAddress">Whether the value is an address, written in the mono font.</param>
/// <param name="Tone">How the value is coloured.</param>
public sealed record ExitFact(string Key, string Value, bool IsAddress = false, ExitFactTone Tone = ExitFactTone.Plain);

/// <summary>How a line of the box <em>Kontroll</em> is coloured.</summary>
public enum ExitFactTone
{
    /// <summary>In the text's colour.</summary>
    Plain,

    /// <summary>Quiet, for what is not an answer.</summary>
    Quiet,

    /// <summary>In the warning colour, for a country other than the place says.</summary>
    Warning,
}

/// <summary>
/// The words of an exit's check (sketch UT3b), in one place, so that the column <em>Syns som</em>, the box
/// <em>Kontroll</em> and the toast say the same thing.
/// </summary>
public static class ExitCheckWords
{
    /// <summary>The column when the exit has never been checked.</summary>
    public const string NotCheckedText = "Inte kontrollerad";

    /// <summary>The column's tip when the exit has never been checked.</summary>
    public const string NotCheckedTip = "Kontrolleras när den är ansluten";

    /// <summary>The column while the trace is asked.</summary>
    public const string CheckingText = "Frågar 1.1.1.1 …";

    /// <summary>The column when the trace did not answer.</summary>
    public const string FailedText = "Kontrollen gick inte";

    /// <summary>The line under it when the trace did not answer.</summary>
    public const string FailedSubText = "1.1.1.1 svarade inte";

    /// <summary>The tip when the trace did not answer.</summary>
    public const string FailedTip = "Ingen adress att visa. Uppslagen går fortfarande genom utgången.";

    /// <summary>What the box says under the button: what the check sends, and where its answer goes.</summary>
    public const string AboutText =
        "Frågar 1.1.1.1 genom utgången vilken adress uppslagen kommer ifrån. Skickar inget om ärendet, och svaret "
        + "sparas inte i ärendet.";

    /// <summary>The toast's title when an exit is seen in another country than its place says.</summary>
    public const string MismatchTitle = "Utgången syns i ett annat land";

    /// <summary>The key of the first line, the address.</summary>
    private const string SeenKey = "Syns som";

    /// <summary>The words <em>som angivet</em>, when the country is the one the place says.</summary>
    private const string AsGivenText = "som angivet";

    /// <summary>The day of an answer that came today.</summary>
    private const string TodayText = "i dag";

    /// <summary>The trace's host, as the box names where the answer came from.</summary>
    private const string TraceHost = "1.1.1.1";

    /// <summary>Swedish, for the dates.</summary>
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>Gives the state of a check: seen, or seen elsewhere than the place says.</summary>
    /// <param name="sighting">The answer.</param>
    /// <param name="place">The exit's place.</param>
    /// <returns><see cref="ExitCheckState.Mismatch"/> or <see cref="ExitCheckState.Seen"/>.</returns>
    public static ExitCheckState StateOf(ExitSighting sighting, string place)
    {
        ArgumentNullException.ThrowIfNull(sighting);
        var given = ExitPlace.CountryOf(place);
        var differs = given is not null && sighting.HasCountry
            && !given.Code.Equals(sighting.CountryCode, StringComparison.Ordinal);
        return differs ? ExitCheckState.Mismatch : ExitCheckState.Seen;
    }

    /// <summary>Gives the country of an answer as it is shown, with the place's country when they differ.</summary>
    /// <param name="sighting">The answer.</param>
    /// <param name="place">The exit's place.</param>
    /// <returns>For example <em>Nederländerna · angivet Sverige</em>; empty when the answer named none.</returns>
    public static string CountryText(ExitSighting sighting, string place)
    {
        ArgumentNullException.ThrowIfNull(sighting);
        if (!sighting.HasCountry)
        {
            return string.Empty;
        }

        var seen = ExitPlace.NameOf(sighting.CountryCode);
        return StateOf(sighting, place) == ExitCheckState.Mismatch
            ? $"{seen} · angivet {ExitPlace.CountryOf(place)!.Name}"
            : seen;
    }

    /// <summary>Gives the box's lines for an answer.</summary>
    /// <param name="sighting">The answer.</param>
    /// <param name="place">The exit's place.</param>
    /// <param name="now">Now, so that today's answer says <em>i dag</em>.</param>
    /// <returns>The address, the country and where and when the answer came from.</returns>
    public static IReadOnlyList<ExitFact> FactsFor(ExitSighting sighting, string place, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sighting);
        var facts = new List<ExitFact> { new(SeenKey, sighting.Address, IsAddress: true) };
        if (sighting.HasCountry)
        {
            var mismatch = StateOf(sighting, place) == ExitCheckState.Mismatch;
            var land = mismatch || ExitPlace.CountryOf(place) is null
                ? CountryText(sighting, place)
                : $"{ExitPlace.NameOf(sighting.CountryCode)} · {AsGivenText}";
            facts.Add(new("Land", land, Tone: mismatch ? ExitFactTone.Warning : ExitFactTone.Plain));
        }

        facts.Add(new("Källa", $"{TraceHost} · {When(sighting.CheckedAt, now)}", Tone: ExitFactTone.Quiet));
        return facts;
    }

    /// <summary>
    /// Gives what the exit's tooltip in the title bar adds about the way out (sketch UT3b): the address and country
    /// the last check saw, and the day when it was not today.
    /// </summary>
    /// <param name="sighting">The answer.</param>
    /// <param name="now">Now, so that an earlier answer says its day.</param>
    /// <returns>
    /// For example <em>syns som 185.213.154.68, Sverige</em> or <em>syns som 81.226.17.40 · 6 nov</em>.
    /// </returns>
    public static string TipText(ExitSighting sighting, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sighting);
        var country = sighting.HasCountry ? ", " + ExitPlace.NameOf(sighting.CountryCode) : string.Empty;
        var day = Day(sighting.CheckedAt, now);
        var earlier = day == TodayText ? string.Empty : " · " + day;
        return $"syns som {sighting.Address}{country}{earlier}";
    }

    /// <summary>Gives the box's line before any check.</summary>
    /// <returns>One quiet line.</returns>
    public static IReadOnlyList<ExitFact> NotCheckedFacts() =>
        [new(SeenKey, "Inte kontrollerad än", Tone: ExitFactTone.Quiet)];

    /// <summary>Gives the box's line while the trace is asked.</summary>
    /// <returns>One quiet line.</returns>
    public static IReadOnlyList<ExitFact> CheckingFacts() => [new(SeenKey, "Frågar …", Tone: ExitFactTone.Quiet)];

    /// <summary>Gives the box's lines when the trace did not answer.</summary>
    /// <returns>Two quiet lines.</returns>
    public static IReadOnlyList<ExitFact> FailedFacts() =>
    [
        new("Svar", "Inget. 1.1.1.1 svarade inte", Tone: ExitFactTone.Quiet),
        new("Uppslagen", "går ändå genom utgången", Tone: ExitFactTone.Quiet),
    ];

    /// <summary>Gives the toast's line when an exit is seen in another country than its place says.</summary>
    /// <param name="name">The exit's name.</param>
    /// <param name="sighting">The answer.</param>
    /// <param name="place">The exit's place.</param>
    /// <returns>For example <em>Stockholm syns som 89.45.90.12 i Nederländerna, angivet Sverige.</em></returns>
    public static string MismatchText(string name, ExitSighting sighting, string place)
    {
        ArgumentNullException.ThrowIfNull(sighting);
        var given = ExitPlace.CountryOf(place)?.Name ?? place;
        return $"{name} syns som {sighting.Address} i {ExitPlace.NameOf(sighting.CountryCode)}, angivet {given}.";
    }

    /// <summary>Gives when an answer came, short: <em>i dag 10:42</em> or <em>6 nov 14:20</em>, local time.</summary>
    /// <param name="at">When.</param>
    /// <param name="now">Now.</param>
    /// <returns>The words.</returns>
    public static string When(DateTimeOffset at, DateTimeOffset now)
    {
        var local = at.ToLocalTime();
        var today = local.Date == now.ToLocalTime().Date;
        return today
            ? $"i dag {local.ToString("HH:mm", Swedish)}"
            : local.ToString("d MMM HH:mm", Swedish).Replace(".", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>Gives the day an answer came, short, for the column: <em>i dag</em> or <em>6 nov</em>.</summary>
    /// <param name="at">When.</param>
    /// <param name="now">Now.</param>
    /// <returns>The words.</returns>
    public static string Day(DateTimeOffset at, DateTimeOffset now)
    {
        var local = at.ToLocalTime();
        return local.Date == now.ToLocalTime().Date
            ? TodayText
            : local.ToString("d MMM", Swedish).Replace(".", string.Empty, StringComparison.Ordinal);
    }
}
