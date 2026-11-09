using System.Globalization;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.App.Guide;

/// <summary>
/// What most likely made a case not match this computer's anchor (sketch AK1a). Chainsaw sees only the file and the
/// anchor, so this is always an assessment, never a fact (principle 2), and is shown dashed with what else it can be.
/// </summary>
public enum AnchorCause
{
    /// <summary>The anchor is ahead of where the file ends: an older copy was opened.</summary>
    OlderCopy,

    /// <summary>The file has another record where the anchor is: it went one way and this computer another.</summary>
    TwoComputers,

    /// <summary>This computer had no anchor for the case.</summary>
    AnchorsMissing,

    /// <summary>The anchor file was older than the one the vault wrote last: it was put back from a copy.</summary>
    AnchorsRolledBack,

    /// <summary>The anchor file could not be read.</summary>
    AnchorsUnreadable,
}

/// <summary>One end of the chain as the Guide's panel shows it: the anchor before, or the file's head after.</summary>
/// <param name="Role"><em>Före</em> or <em>Efter</em>.</param>
/// <param name="Who">Whose: <em>Ankaret</em>, <em>Den här datorn</em> or <em>Filen</em>.</param>
/// <param name="Row">The journal line, or <em>–</em> when there is none.</param>
/// <param name="Hash">The hash shortened, or <em>saknas</em>.</param>
/// <param name="FullHash">The whole hash, for copying; <see langword="null"/> when there is none.</param>
public sealed record GuideAnchorEnd(string Role, string Who, string Row, string Hash, string? FullHash)
{
    /// <summary>Gets whether the hash can be copied.</summary>
    public bool CanCopy => FullHash is not null;
}

/// <summary>
/// The Guide's answer to <em>Varför?</em> about a replaced anchor (sketch AK1a): when and on which journal line, that the
/// chain in the file is whole, the anchor before and the file's head after, and the likely cause with what to do and
/// what else it can be.
/// </summary>
/// <param name="Heading">When, the journal line and its code.</param>
/// <param name="ChainText">That the file's chain is whole, from line 1 to its head.</param>
/// <param name="Before">The anchor before, in yellow.</param>
/// <param name="After">The file's head after, in green.</param>
/// <param name="Cause">The likely cause.</param>
/// <param name="CauseShort">The cause in two or three words, for <em>Troligast: …</em>.</param>
/// <param name="CauseName">The cause as a heading.</param>
/// <param name="What">What Chainsaw saw, in plain words.</param>
/// <param name="Todo">What the user can do.</param>
/// <param name="Also">What else it can be, for <em>Kan också vara: …</em>.</param>
public sealed record GuideAnchorChange(
    string Heading,
    string ChainText,
    GuideAnchorEnd Before,
    GuideAnchorEnd After,
    AnchorCause Cause,
    string CauseShort,
    string CauseName,
    string What,
    string Todo,
    string Also)
{
    /// <summary>Gets when the anchor was replaced, for example <c>Ankaret byttes i dag 09:12</c>.</summary>
    public string Replaced { get; init; } = string.Empty;

    /// <summary>The text for a line or hash that is not there.</summary>
    private const string None = "–";

    /// <summary>Gives the likely cause of a replaced anchor.</summary>
    /// <param name="replacement">The replacement as the journal tells it.</param>
    /// <returns>The cause.</returns>
    public static AnchorCause CauseOf(AnchorReplacement replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        return replacement.Problem switch
        {
            AnchorReplacementProblem.Missing => AnchorCause.AnchorsMissing,
            AnchorReplacementProblem.RolledBack => AnchorCause.AnchorsRolledBack,
            AnchorReplacementProblem.Unreadable or AnchorReplacementProblem.Unknown => AnchorCause.AnchorsUnreadable,
            _ => DivergedCause(replacement),
        };
    }

    /// <summary>Builds the answer for a replaced anchor.</summary>
    /// <param name="replacement">The replacement as the journal tells it.</param>
    /// <param name="fileHead">The journal's head in the file now, from the verification, or <see langword="null"/>.</param>
    /// <param name="nowUtc">The time now, for <em>i dag 09:12</em>.</param>
    /// <param name="zone">The time zone times are told in.</param>
    /// <returns>The answer.</returns>
    public static GuideAnchorChange Of(
        AnchorReplacement replacement, ChainVerification? fileHead, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(zone);
        var (before, after) = Ends(replacement);
        var cause = CauseOf(replacement);
        var beforeRow = before?.Sequence;
        var afterRow = after?.Sequence;
        var replaced = $"Ankaret byttes {EnvelopeWords.When(replacement.Utc, nowUtc, zone)}";
        var heading = $"{replaced} · journalrad {Number(replacement.Sequence)} · {CaseJournalKinds.AnchorsReplaced}";
        var texts = Texts(cause, beforeRow, afterRow);
        return new GuideAnchorChange(
            heading,
            ChainWords(fileHead),
            End("Före", BeforeWho(cause), before),
            End("Efter", "Filen", after),
            cause,
            texts.Short,
            texts.Name,
            texts.What,
            texts.Todo,
            texts.Also)
        {
            Replaced = replaced,
        };
    }

    /// <summary>Shortens a hash to its first and last four characters, as the sketch does.</summary>
    /// <param name="hash">The hash.</param>
    /// <returns>The short form.</returns>
    public static string ShortHash(string hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        return hash.Length <= 12 ? hash : $"{hash[..4]}…{hash[^4..]}";
    }

    /// <summary>The likely cause when the case no longer reached the anchor.</summary>
    /// <param name="replacement">The replacement.</param>
    /// <returns>An older copy when the anchor is ahead of the file's head, two computers otherwise.</returns>
    private static AnchorCause DivergedCause(AnchorReplacement replacement)
    {
        var (before, after) = Ends(replacement);
        return before is null || after is null || before.Sequence > after.Sequence
            ? AnchorCause.OlderCopy
            : AnchorCause.TwoComputers;
    }

    /// <summary>
    /// Picks the chain the answer is told by: the journal, which every case has, or the first chain the anchor no
    /// longer reached.
    /// </summary>
    /// <param name="replacement">The replacement.</param>
    /// <returns>The anchor before and the head after, either of which may be missing.</returns>
    private static (AnchorPoint? Before, AnchorPoint? After) Ends(AnchorReplacement replacement)
    {
        var chain = replacement.BeforeOf(CaseChains.Journal)?.Chain
            ?? replacement.Before.FirstOrDefault()?.Chain
            ?? CaseChains.Journal;
        return (replacement.BeforeOf(chain), replacement.AfterOf(chain));
    }

    /// <summary>Says whose the end before is: the anchor, or this computer when the file went another way.</summary>
    /// <param name="cause">The cause.</param>
    /// <returns>The words.</returns>
    private static string BeforeWho(AnchorCause cause) =>
        cause == AnchorCause.TwoComputers ? "Den här datorn" : "Ankaret";

    /// <summary>Builds an end of the chain.</summary>
    /// <param name="role">Före or Efter.</param>
    /// <param name="who">Whose.</param>
    /// <param name="point">The point, or <see langword="null"/>.</param>
    /// <returns>The end.</returns>
    private static GuideAnchorEnd End(string role, string who, AnchorPoint? point) => point is null
        ? new GuideAnchorEnd(role, who, None, "saknas", null)
        : new GuideAnchorEnd(role, who, Number(point.Sequence), ShortHash(point.Hash), point.Hash);

    /// <summary>Says that the file's chain is whole, from the verification.</summary>
    /// <param name="fileHead">The journal's verification, or <see langword="null"/>.</param>
    /// <returns>The text.</returns>
    private static string ChainWords(ChainVerification? fileHead) => fileHead is { Break: null } journal
        ? $"Filens hashkedja är hel från rad 1 till {Number(journal.HeadSequence)}. Inget i den har ändrats i efterhand."
        : "Kör Verifiera för att se att filens hashkedja är hel.";

    /// <summary>The cause's texts, with the lines filled in.</summary>
    /// <param name="cause">The cause.</param>
    /// <param name="before">The anchor's line, or <see langword="null"/>.</param>
    /// <param name="after">The file's line, or <see langword="null"/>.</param>
    /// <returns>The texts.</returns>
    private static (string Short, string Name, string What, string Todo, string Also) Texts(
        AnchorCause cause, long? before, long? after)
    {
        var at = after is { } head ? Number(head) : None;
        var seen = before is { } anchor ? Number(anchor) : None;
        var restingAt =
            $"Inget att göra. Från och med nu står ankaret på rad {at}, och nästa öppning jämförs mot det.";
        return cause switch
        {
            AnchorCause.OlderCopy => (
                "äldre kopia",
                "En äldre kopia öppnades",
                $"Filen slutar på rad {at}, men den här datorn har sett ärendet fram till rad {seen}. Filen är "
                + "troligen en äldre kopia, till exempel från en säkerhetskopia eller ett mejl.",
                $"Har du en nyare fil, öppna den i stället. {Missing(before, after)}",
                "Kan också vara: ändringar på två datorer"),
            AnchorCause.TwoComputers => (
                "två datorer",
                "Ärendet ändrades på två datorer",
                $"Filen har rad {seen}, men med en annan hash än den här datorn såg. Filen har gått åt ett håll och "
                + $"datorn åt ett annat; filen slutar nu på rad {at}.",
                "Båda grenarna är hela var för sig. Den andra finns kvar i den kopia där den gjordes; Chainsaw slår "
                + "inte ihop dem.",
                "Kan också vara: en äldre kopia som sedan ändrats"),
            AnchorCause.AnchorsMissing => (
                "ankaret saknas",
                "Ankarfilen saknades",
                "Den här datorn hade inget ankare för ärendet: ankarfilen var borttagen, ny eller tillbakalagd från en "
                + "säkerhetskopia. Det fanns inget att jämföra med.",
                restingAt,
                "Kan också vara: första gången ärendet öppnas på datorn"),
            AnchorCause.AnchorsRolledBack => (
                "ankaret tillbakalagt",
                "Ankarfilen rullades tillbaka",
                "Ankarfilen på den här datorn var äldre än den valvet skrev senast: den har lagts tillbaka från en "
                + "kopia.",
                restingAt,
                "Kan också vara: en säkerhetskopia av hela datorn som lades tillbaka"),
            _ => (
                "ankaret oläsligt",
                "Ankaret gick inte att läsa",
                "Ankarfilen på den här datorn var skadad eller ändrad, så den gick inte att lita på.",
                restingAt,
                "Kan också vara: ett fel på disken"),
        };
    }

    /// <summary>Says how many lines the older copy lacks.</summary>
    /// <param name="before">The anchor's line.</param>
    /// <param name="after">The file's line.</param>
    /// <returns>The sentence, or an empty text when the lines are not known.</returns>
    private static string Missing(long? before, long? after) => (before, after) switch
    {
        ({ } anchor, { } head) when anchor > head =>
            $"De {Number(anchor - head)} raderna efter {Number(head)} finns inte i den här filen.",
        _ => string.Empty,
    };

    /// <summary>Writes a number with Swedish digit grouping.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The text.</returns>
    private static string Number(long number) => number.ToString("#,##0", CultureInfo.GetCultureInfo("sv-SE"));
}
